// RenText - A file renaming tool with a text-editor feel.
// Copyright (C) 2026 umineko73 <https://github.com/umineko73>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RenText.Models;
using RenText.Services;

namespace RenText.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly FileSystemService _fsService = new();
    private readonly FileRenameService _renameService = new();
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _loadCts;
    private FileSystemWatcher? _fsWatcher;
    private FileSystemWatcher? _parentWatcher;
    private CancellationTokenSource? _watcherDebounce;
    private int _loadedFileCount = -1;   // -1 = フォルダ未読込
    private static LocalizationService L => LocalizationService.Instance;

    // --- フォルダツリー ---
    public ObservableCollection<FolderTreeItemViewModel> FolderTree { get; } = new();

    // --- ファイル一覧（編集ペイン） ---
    public ObservableCollection<FileLineViewModel> FileLines { get; } = new();

    // --- アドレスバー ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyListMessage))]
    private string _currentPath = "";

    // --- 一覧プレースホルダー / 中央エラー / 成功トースト ---
    public bool ShowEmptyPlaceholder => FileLines.Count == 0 && !IsLoading && !HasError;

    public bool ShowErrorMessage => HasError && FileLines.Count == 0;

    public string EmptyListMessage =>
        string.IsNullOrEmpty(CurrentPath) ? L.Get("Empty_NoFolder") : L.Get("Empty_NoFiles");

    [ObservableProperty]
    private bool _showSuccessMessage;

    // 連続保存時に古いタイマーが新しいトーストを早期に消さないためのトークン
    private int _successToastToken;

    private async Task ShowSuccessToastAsync()
    {
        var token = ++_successToastToken;
        ShowSuccessMessage = true;
        await Task.Delay(1000);
        if (token == _successToastToken)
            ShowSuccessMessage = false;
    }

    // --- 表示形式 ---
    public ObservableCollection<string> DisplayFormatItems { get; } = new();

    private void RefreshDisplayFormatItems()
    {
        var saved = SelectedDisplayFormatIndex;
        DisplayFormatItems.Clear();
        DisplayFormatItems.Add(L.Get("DisplayFormat_NameOnly"));
        DisplayFormatItems.Add(L.Get("DisplayFormat_WithDate"));
        DisplayFormatItems.Add(L.Get("DisplayFormat_WithSize"));
        DisplayFormatItems.Add(L.Get("DisplayFormat_WithDateAndSize"));
        SelectedDisplayFormatIndex = saved;
    }

    [ObservableProperty]
    private int _selectedDisplayFormatIndex = 0;

    partial void OnSelectedDisplayFormatIndexChanged(int value)
    {
        var fmt = (DisplayFormat)value;
        foreach (var line in FileLines)
            line.UpdateDisplayFormat(fmt);
    }

    // --- 隠し/システムフォルダ表示 ---
    [ObservableProperty]
    private bool _showHiddenItems = false;

    partial void OnShowHiddenItemsChanged(bool value)
    {
        var expandedPaths = CollectExpandedPaths(FolderTree).ToHashSet(StringComparer.OrdinalIgnoreCase);
        TreeRebuildStarting?.Invoke(this, EventArgs.Empty);
        InitializeFolderTree();
        RestoreExpansion(FolderTree, expandedPaths);
        RestoreSelection(FolderTree, CurrentPath);
        TreeRebuildCompleted?.Invoke(this, EventArgs.Empty);
        if (!string.IsNullOrEmpty(CurrentPath))
            _ = LoadFolderAsync(CurrentPath);
    }

    public event EventHandler? TreeRebuildStarting;
    public event EventHandler? TreeRebuildCompleted;

    private static IEnumerable<string> CollectExpandedPaths(IEnumerable<FolderTreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (!item.IsExpanded) continue;
            yield return item.FullPath;
            foreach (var child in CollectExpandedPaths(item.Children))
                yield return child;
        }
    }

    private static void RestoreExpansion(IEnumerable<FolderTreeItemViewModel> items, HashSet<string> expandedPaths)
    {
        foreach (var item in items)
        {
            if (!expandedPaths.Contains(item.FullPath)) continue;
            item.IsExpanded = true;
            RestoreExpansion(item.Children, expandedPaths);
        }
    }

    private static void RestoreSelection(IEnumerable<FolderTreeItemViewModel> items, string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var item in items)
        {
            if (string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
                return;
            }
            RestoreSelection(item.Children, path);
        }
    }

    // --- 検索置換パネル ---
    [ObservableProperty]
    private bool _isSearchPanelVisible;

    partial void OnIsSearchPanelVisibleChanged(bool value)
    {
        if (!value) ClearPreview();
        else UpdatePreview();
    }

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => UpdatePreview();

    [ObservableProperty]
    private string _replaceText = "";

    partial void OnReplaceTextChanged(string value) => UpdatePreview();

    [ObservableProperty]
    private bool _useRegex;

    partial void OnUseRegexChanged(bool value) => UpdatePreview();

    [ObservableProperty]
    private bool _isCaseSensitive;

    partial void OnIsCaseSensitiveChanged(bool value) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!IsSearchPanelVisible || string.IsNullOrEmpty(SearchText))
        {
            ClearPreview();
            return;
        }

        foreach (var line in FileLines)
        {
            try
            {
                line.PreviewName = UseRegex
                    ? Regex.Replace(line.EditedName, SearchText, ReplaceText,
                        IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
                    : line.EditedName.Replace(SearchText, ReplaceText,
                        IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                line.PreviewName = null;
            }
        }
    }

    private void ClearPreview()
    {
        foreach (var line in FileLines)
            line.PreviewName = null;
    }

    // --- ソート ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortNameLabel))]
    [NotifyPropertyChangedFor(nameof(SortDateLabel))]
    [NotifyPropertyChangedFor(nameof(SortExtLabel))]
    [NotifyPropertyChangedFor(nameof(SortSizeLabel))]
    private SortColumn _currentSortColumn = SortColumn.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortNameLabel))]
    [NotifyPropertyChangedFor(nameof(SortDateLabel))]
    [NotifyPropertyChangedFor(nameof(SortExtLabel))]
    [NotifyPropertyChangedFor(nameof(SortSizeLabel))]
    private bool _sortAscending = true;

    public string SortNameLabel => SortLabel(SortColumn.Name,      L.Get("Sort_Name"));
    public string SortDateLabel => SortLabel(SortColumn.Date,      L.Get("Sort_Date"));
    public string SortExtLabel  => SortLabel(SortColumn.Extension, L.Get("Sort_Ext"));
    public string SortSizeLabel => SortLabel(SortColumn.Size,      L.Get("Sort_Size"));

    private string SortLabel(SortColumn col, string name)
    {
        if (CurrentSortColumn != col) return name;
        return SortAscending ? $"{name} ↑" : $"{name} ↓";
    }

    [RelayCommand] private void SortByName()      => SortBy(SortColumn.Name);
    [RelayCommand] private void SortByDate()      => SortBy(SortColumn.Date);
    [RelayCommand] private void SortByExtension() => SortBy(SortColumn.Extension);
    [RelayCommand] private void SortBySize()      => SortBy(SortColumn.Size);

    private void SortBy(SortColumn column)
    {
        if (CurrentSortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            CurrentSortColumn = column;
            SortAscending = true;
        }
        ApplySort();
    }

    private void ApplySort()
    {
        var sorted = (CurrentSortColumn, SortAscending) switch
        {
            (SortColumn.Name, true)      => FileLines.OrderBy(f => f.EditedName, StringComparer.OrdinalIgnoreCase),
            (SortColumn.Name, false)     => FileLines.OrderByDescending(f => f.EditedName, StringComparer.OrdinalIgnoreCase),
            (SortColumn.Date, true)      => FileLines.OrderBy(f => f.Source.LastModified),
            (SortColumn.Date, false)     => FileLines.OrderByDescending(f => f.Source.LastModified),
            (SortColumn.Extension, true) => FileLines.OrderBy(f => Path.GetExtension(f.EditedName), StringComparer.OrdinalIgnoreCase)
                                                     .ThenBy(f => f.EditedName, StringComparer.OrdinalIgnoreCase),
            (SortColumn.Extension, false)=> FileLines.OrderByDescending(f => Path.GetExtension(f.EditedName), StringComparer.OrdinalIgnoreCase)
                                                     .ThenBy(f => f.EditedName, StringComparer.OrdinalIgnoreCase),
            (SortColumn.Size, true)      => FileLines.OrderBy(f => f.Source.FileSize),
            (SortColumn.Size, false)     => FileLines.OrderByDescending(f => f.Source.FileSize),
            _                            => FileLines.OrderBy(f => f.EditedName, StringComparer.OrdinalIgnoreCase),
        };

        var list = sorted.ToList();
        FileLines.Clear();
        foreach (var item in list)
            FileLines.Add(item);
    }

    // --- テーマ ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    private bool _isDarkMode = true;

    public string ThemeIcon => IsDarkMode ? "☀️" : "🌙";

    partial void OnIsDarkModeChanged(bool value)
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
        SaveSettings();
    }

    [RelayCommand]
    private void ToggleTheme() => IsDarkMode = !IsDarkMode;

    // --- 言語 ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageLabel))]
    private string _currentLanguage = "ja";

    public string LanguageLabel => CurrentLanguage == "ja" ? "EN" : "JA";

    [RelayCommand]
    private void ToggleLanguage()
    {
        var newLang = CurrentLanguage == "ja" ? "en" : "ja";
        L.SetLanguage(newLang);
        CurrentLanguage = newLang;
        SaveSettings();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshDisplayFormatItems();
        OnPropertyChanged(nameof(SortNameLabel));
        OnPropertyChanged(nameof(SortDateLabel));
        OnPropertyChanged(nameof(SortExtLabel));
        OnPropertyChanged(nameof(SortSizeLabel));
        OnPropertyChanged(nameof(EmptyListMessage));
        RefreshStatusMessage();
    }

    // 言語切替後にステータスメッセージを現在の状態に合わせて再構築する
    private void RefreshStatusMessage()
    {
        if (_loadedFileCount >= 0 && !string.IsNullOrEmpty(CurrentPath))
            StatusMessage = string.Format(L.Get("Status_FileCount"), _loadedFileCount, CurrentPath);
        else if (string.IsNullOrEmpty(CurrentPath))
            StatusMessage = L.Get("Status_SelectFolder");
        // エラー・保存完了などの一時メッセージはそのままでよい
    }

    // --- ステータス ---
    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowErrorMessage))]
    private bool _hasError;

    // --- カーソル位置 ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CursorLineText))]
    [NotifyPropertyChangedFor(nameof(HasCursorLine))]
    private int _cursorLine = 0;

    public string CursorLineText => $"Ln {CursorLine}";
    public bool HasCursorLine => CursorLine > 0;

    // ========== コンストラクタ ==========

    public MainWindowViewModel(AppSettings settings, SettingsService settingsService)
    {
        _settingsService = settingsService;
        _currentLanguage = settings.Language;
        _isDarkMode = settings.Theme != "light";
        _statusMessage = L.Get("Status_SelectFolder");

        L.LanguageChanged += OnLanguageChanged;
        FileLines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowEmptyPlaceholder));
            OnPropertyChanged(nameof(ShowErrorMessage));
        };
        RefreshDisplayFormatItems();
    }

    // ウィンドウ終了時に呼び出し、シングルトンへの購読を解除
    public void Cleanup()
    {
        L.LanguageChanged -= OnLanguageChanged;
        StopWatching();
    }

    // ========== コマンド ==========

    [RelayCommand]
    private async Task NavigateTo()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath)) return;
        await LoadFolderAsync(CurrentPath.Trim());
    }

    [RelayCommand]
    private void SelectFolder()
    {
        OpenFolderDialogRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath) || FileLines.Count == 0)
            return;

        foreach (var line in FileLines)
        {
            line.HasError = false;
            line.ErrorMessage = null;
        }

        var originals = FileLines.Select(f => f.Source).ToList();
        var newNames = FileLines.Select(f => f.EditedName.Trim()).ToList();

        var validation = _renameService.Validate(CurrentPath, originals, newNames);
        if (!validation.Success)
        {
            if (validation.ErrorIndex >= 0 && validation.ErrorIndex < FileLines.Count)
            {
                FileLines[validation.ErrorIndex].HasError = true;
                FileLines[validation.ErrorIndex].ErrorMessage = validation.ErrorMessage;
            }
            StatusMessage = string.Format(L.Get("Status_Error"), validation.ErrorMessage);
            HasError = true;
            return;
        }

        var result = _renameService.Execute(CurrentPath, originals, newNames);
        if (!result.Success)
        {
            StatusMessage = string.Format(L.Get("Status_Error"), result.ErrorMessage);
            HasError = true;
            return;
        }

        HasError = false;
        StatusMessage = L.Get("Status_Saved");
        await LoadFolderAsync(CurrentPath);
        _ = ShowSuccessToastAsync();
    }

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var line in FileLines)
            line.ResetToOriginal();
        HasError = false;
        StatusMessage = L.Get("Status_Reset");
    }

    [RelayCommand]
    private void ToggleSearchPanel()
    {
        IsSearchPanelVisible = !IsSearchPanelVisible;
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchText)) return;

        int count = 0;
        foreach (var line in FileLines)
        {
            string replaced;
            try
            {
                replaced = UseRegex
                    ? Regex.Replace(line.EditedName, SearchText, ReplaceText,
                        IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
                    : line.EditedName.Replace(SearchText, ReplaceText,
                        IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            catch (RegexParseException ex)
            {
                StatusMessage = string.Format(L.Get("Status_RegexError"), ex.Message);
                HasError = true;
                return;
            }

            if (replaced != line.EditedName)
            {
                line.EditedName = replaced;
                count++;
            }
        }

        HasError = false;
        StatusMessage = count > 0
            ? string.Format(L.Get("Status_Replaced"), count)
            : L.Get("Status_NoMatch");
    }

    [RelayCommand]
    private async Task SelectTreeFolder(FolderTreeItemViewModel? item)
    {
        if (item == null || string.IsNullOrEmpty(item.FullPath)) return;
        CurrentPath = item.FullPath;
        await LoadFolderAsync(item.FullPath);
    }

    // ========== 内部処理 ==========

    // ----- ファイルシステム監視 -----

    private void StartWatching(string path)
    {
        StopWatching();
        if (!Directory.Exists(path)) return;

        // ① 現在フォルダ内のファイル増減・リネームを監視
        _fsWatcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _fsWatcher.Created += OnFsEvent;
        _fsWatcher.Deleted += OnFsEvent;
        _fsWatcher.Renamed += OnFsEvent;
        _fsWatcher.Error   += OnFsWatcherError;

        // ② 現在フォルダ自体のリネーム・削除を監視（親ディレクトリを対象にする）
        var parent = Path.GetDirectoryName(path);
        if (parent != null && Directory.Exists(parent))
        {
            _parentWatcher = new FileSystemWatcher(parent)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _parentWatcher.Renamed += OnCurrentFolderRenamed;
            _parentWatcher.Deleted += OnCurrentFolderDeleted;
            _parentWatcher.Error   += OnFsWatcherError;
        }
    }

    private void StopWatching()
    {
        _watcherDebounce?.Cancel();
        _watcherDebounce = null;

        if (_fsWatcher != null)
        {
            _fsWatcher.EnableRaisingEvents = false;
            _fsWatcher.Created -= OnFsEvent;
            _fsWatcher.Deleted -= OnFsEvent;
            _fsWatcher.Renamed -= OnFsEvent;
            _fsWatcher.Error   -= OnFsWatcherError;
            _fsWatcher.Dispose();
            _fsWatcher = null;
        }

        if (_parentWatcher != null)
        {
            _parentWatcher.EnableRaisingEvents = false;
            _parentWatcher.Renamed -= OnCurrentFolderRenamed;
            _parentWatcher.Deleted -= OnCurrentFolderDeleted;
            _parentWatcher.Error   -= OnFsWatcherError;
            _parentWatcher.Dispose();
            _parentWatcher = null;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    // 現在フォルダ自体がリネームされた → 新パスに追従してリロード
    private void OnCurrentFolderRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.Equals(e.OldFullPath, CurrentPath, StringComparison.OrdinalIgnoreCase)) return;

        Dispatcher.UIThread.Post(async () =>
        {
            // フォルダツリーの選択ノードも新パスに合わせる
            UpdateTreeSelection(CurrentPath, e.FullPath);
            await LoadFolderAsync(e.FullPath);
        });
    }

    // 現在フォルダ自体が削除された → エラー表示
    private void OnCurrentFolderDeleted(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(e.FullPath, CurrentPath, StringComparison.OrdinalIgnoreCase)) return;

        Dispatcher.UIThread.Post(() =>
        {
            StopWatching();
            _loadedFileCount = -1;
            FileLines.Clear();
            CursorLine = 0;
            HasError = true;
            StatusMessage = string.Format(L.Get("Status_FolderNotFound"), CurrentPath);
        });
    }

    // フォルダツリー内で oldPath に一致するノードを newPath に書き換える
    private void UpdateTreeSelection(string oldPath, string newPath)
    {
        UpdateTreeNodePath(FolderTree, oldPath, newPath);
    }

    private static bool UpdateTreeNodePath(
        IEnumerable<FolderTreeItemViewModel> nodes, string oldPath, string newPath)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, oldPath, StringComparison.OrdinalIgnoreCase))
            {
                node.UpdatePath(newPath);
                return true;
            }
            if (UpdateTreeNodePath(node.Children, oldPath, newPath))
                return true;
        }
        return false;
    }

    private void OnFsWatcherError(object sender, ErrorEventArgs e)
    {
        // 内部バッファオーバーフロー時などは監視を再起動する
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
                StartWatching(CurrentPath);
        });
    }

    private void ScheduleRefresh()
    {
        _watcherDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _watcherDebounce = cts;

        Task.Run(async () =>
        {
            try
            {
                // 連続イベントをまとめるデバウンス
                await Task.Delay(600, cts.Token);

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (IsLoading) return;
                    if (string.IsNullOrEmpty(CurrentPath)) return;

                    // 未保存の編集がある場合はスキップ
                    if (FileLines.Any(f => f.IsModified))
                    {
                        StatusMessage = L.Get("Status_WatchPaused");
                        return;
                    }

                    await LoadFolderAsync(CurrentPath);
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    public async Task LoadFolderAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            _loadedFileCount = -1;
            FileLines.Clear();
            CursorLine = 0;
            StatusMessage = string.Format(L.Get("Status_FolderNotFound"), path);
            HasError = true;
            return;
        }

        // 進行中のロードをキャンセル。
        // 古い CTS はまだ Task.Run 内で使用中の可能性があるため Dispose しない
        // （タイマー・リンク無しの CTS は GC 任せで安全）。
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        StopWatching();

        CurrentPath = path;
        HasError = false;
        IsLoading = true;
        StatusMessage = L.Get("Status_Loading");

        var format = (DisplayFormat)SelectedDisplayFormatIndex;

        // 例外はワーカースレッド上（Task.Run デリゲート内）で捕捉する。
        // await 側で catch するとデバッガが第一チャンス例外で停止するため。
        (IReadOnlyList<FileEntry> Files, Exception? Error) result;
        try
        {
            result = await Task.Run(() =>
            {
                try
                {
                    return ((IReadOnlyList<FileEntry>)_fsService.GetFiles(path, ShowHiddenItems),
                            (Exception?)null);
                }
                catch (Exception ex)
                {
                    return ((IReadOnlyList<FileEntry>)Array.Empty<FileEntry>(), ex);
                }
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested) return;

        if (result.Error != null)
        {
            _loadedFileCount = -1;
            IsLoading = false;
            FileLines.Clear();
            CursorLine = 0;
            StatusMessage = result.Error is UnauthorizedAccessException
                ? string.Format(L.Get("Status_AccessDenied"), result.Error.Message)
                : string.Format(L.Get("Status_ReadFailed"), result.Error.Message);
            HasError = true;
            return;
        }

        IsLoading = false;
        CursorLine = 0;
        FileLines.Clear();
        foreach (var f in result.Files)
            FileLines.Add(new FileLineViewModel(f, format));

        _loadedFileCount = result.Files.Count;
        ApplySort();
        UpdatePreview();
        StartWatching(path);
        StatusMessage = string.Format(L.Get("Status_FileCount"), result.Files.Count, path);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaceholder))]
    private bool _isLoading;

    public void InitializeFolderTree()
    {
        FolderTree.Clear();
        foreach (var drive in _fsService.GetDrives())
            FolderTree.Add(new FolderTreeItemViewModel(drive, _fsService, () => ShowHiddenItems));
    }

    public async Task OnFolderSelected(string path)
    {
        CurrentPath = path;
        await LoadFolderAsync(path);
    }

    private void SaveSettings() =>
        _settingsService.Save(new AppSettings
        {
            Language = CurrentLanguage,
            Theme    = IsDarkMode ? "dark" : "light",
        });

    public event EventHandler? OpenFolderDialogRequested;
}
