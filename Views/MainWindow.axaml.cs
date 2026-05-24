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

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RenText.Services;
using RenText.ViewModels;

namespace RenText.Views;

public partial class MainWindow : Window
{
    private Vector _savedTreeScrollOffset;
    private bool _treeKeyboardNavigation;
    private bool _treeScrollSetupDone;
    private double _treeSavedX;
    private bool _treeBringIntoViewActive;
    private bool _revertingTreeScroll;

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"RenText v{version?.ToString(3) ?? "0.1.0"}";
        Closed += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var grid = this.FindControl<Grid>("MainPanesGrid");
                double treeW    = grid?.ColumnDefinitions[0].ActualWidth ?? 300;
                double previewW = grid?.ColumnDefinitions[4].ActualWidth ?? 320;
                vm.SaveWindowGeometry(Width, Height, WindowState == WindowState.Maximized,
                    treeW, previewW);
                vm.Cleanup();
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenFolderDialogRequested -= OnOpenFolderDialogRequested;
            vm.OpenFolderDialogRequested += OnOpenFolderDialogRequested;

            vm.TreeRebuildStarting -= OnTreeRebuildStarting;
            vm.TreeRebuildStarting += OnTreeRebuildStarting;

            vm.TreeRebuildCompleted -= OnTreeRebuildCompleted;
            vm.TreeRebuildCompleted += OnTreeRebuildCompleted;

            vm.InitializeFolderTree();

            // 前回のペイン幅を復元する
            var grid = this.FindControl<Grid>("MainPanesGrid");
            if (grid != null)
            {
                var (treeW, previewW) = vm.GetSavedPaneWidths();
                grid.ColumnDefinitions[0].Width = new GridLength(treeW,    GridUnitType.Pixel);
                grid.ColumnDefinitions[4].Width = new GridLength(previewW, GridUnitType.Pixel);
            }

            var treeView = this.FindControl<TreeView>("FolderTreeView");
            if (treeView != null)
            {
                treeView.AddHandler(KeyDownEvent, (_, ke) =>
                {
                    _treeKeyboardNavigation = ke.Key is Key.Up or Key.Down
                        or Key.PageUp or Key.PageDown or Key.Home or Key.End;
                }, RoutingStrategies.Tunnel);

                // 展開・選択時の水平方向の自動スクロールを抑止（垂直は通常通り）。
                // 視覚ツリー構築後に遅延セットアップする。
                if (!_treeScrollSetupDone)
                {
                    _treeScrollSetupDone = true;
                    Dispatcher.UIThread.Post(
                        () => SetupTreeHorizontalScrollLock(treeView),
                        DispatcherPriority.Loaded);
                }

                treeView.SelectionChanged += (_, args) =>
                {
                    if (args.AddedItems?.Count > 0 &&
                        args.AddedItems[0] is FolderTreeItemViewModel item)
                    {
                        if (!_treeKeyboardNavigation)
                            item.IsExpanded = true;
                        _treeKeyboardNavigation = false;
                        vm.SelectTreeFolderCommand.Execute(item);
                    }
                };
            }

            // 編集ペインのキーボードナビゲーション・フォーカス追跡
            var fileList = this.FindControl<ItemsControl>("FileListControl");
            if (fileList != null)
            {
                fileList.AddHandler(
                    KeyDownEvent,
                    OnFileListKeyDown,
                    RoutingStrategies.Tunnel);

                fileList.AddHandler(
                    GotFocusEvent,
                    OnFileListItemFocused,
                    RoutingStrategies.Bubble);
            }
        }
    }

    private void OnFileListItemFocused(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox focused) return;
        var fileList = this.FindControl<ItemsControl>("FileListControl");
        if (fileList == null) return;

        var textBoxes = GetVisualDescendants<TextBox>(fileList).ToList();
        var index = textBoxes.IndexOf(focused);
        if (index >= 0 && DataContext is MainWindowViewModel vm)
            vm.CursorLine = index + 1;
    }

    private void OnFileListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Up && e.Key != Key.Down &&
            e.Key != Key.PageUp && e.Key != Key.PageDown) return;

        var fileList = this.FindControl<ItemsControl>("FileListControl");
        if (fileList == null) return;

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;
        if (focused == null) return;

        var textBoxes = GetVisualDescendants<TextBox>(fileList).ToList();
        var index = textBoxes.IndexOf(focused);
        if (index < 0) return;

        int step = 1;
        if (e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            var scrollViewer = this.FindControl<ScrollViewer>("EditScrollViewer");
            var rowHeight = focused.Bounds.Height;
            if (scrollViewer != null && rowHeight > 0)
                step = Math.Max(1, (int)(scrollViewer.Viewport.Height / rowHeight));
        }

        bool isUp = e.Key == Key.Up || e.Key == Key.PageUp;
        int next = Math.Clamp(isUp ? index - step : index + step, 0, textBoxes.Count - 1);

        var caretPos = focused.CaretIndex;
        var target = textBoxes[next];
        target.Focus();
        target.CaretIndex = Math.Min(caretPos, target.Text?.Length ?? 0);
        target.BringIntoView();

        if (DataContext is MainWindowViewModel vm)
            vm.CursorLine = next + 1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.SaveCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.H && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.ToggleSearchPanelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            vm.RefreshFolderTree();
            if (!string.IsNullOrEmpty(vm.CurrentPath))
                _ = vm.LoadFolderAsync(vm.CurrentPath);
            e.Handled = true;
        }
    }

    private ScrollViewer? GetTreeScrollViewer()
    {
        var treeView = this.FindControl<TreeView>("FolderTreeView");
        return treeView != null
            ? GetVisualDescendants<ScrollViewer>(treeView).FirstOrDefault()
            : null;
    }

    private void SetupTreeHorizontalScrollLock(TreeView treeView)
    {
        // ItemsPresenter は ScrollViewer の子。Bubble routing で
        // RequestBringIntoView がここを通過するのは ScrollViewer に届く「前」。
        // ここで Pre-scroll の X を保存しておき、ScrollViewer が水平方向にスクロールしたら戻す。
        var presenter = treeView.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault();
        if (presenter == null) return;

        var sv = GetTreeScrollViewer();
        if (sv == null) return;

        presenter.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
        {
            _treeSavedX = sv.Offset.X;
            _treeBringIntoViewActive = true;
        }, RoutingStrategies.Bubble);

        sv.PropertyChanged += (_, e) =>
        {
            if (e.Property != ScrollViewer.OffsetProperty) return;
            if (_revertingTreeScroll) return;
            if (!_treeBringIntoViewActive) return;
            _treeBringIntoViewActive = false;

            if (Math.Abs(sv.Offset.X - _treeSavedX) > 0.5)
            {
                _revertingTreeScroll = true;
                Dispatcher.UIThread.Post(() =>
                {
                    sv.Offset = new Vector(_treeSavedX, sv.Offset.Y);
                    _revertingTreeScroll = false;
                }, DispatcherPriority.Loaded);
            }
        };
    }

    private void OnTreeRebuildStarting(object? sender, EventArgs e)
    {
        _savedTreeScrollOffset = GetTreeScrollViewer()?.Offset ?? default;
    }

    private void OnTreeRebuildCompleted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var sv = GetTreeScrollViewer();
            if (sv != null)
                sv.Offset = _savedTreeScrollOffset;
        }, DispatcherPriority.Loaded);
    }

    private static IEnumerable<T> GetVisualDescendants<T>(Visual root) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T t) yield return t;
            foreach (var d in GetVisualDescendants<T>(child))
                yield return d;
        }
    }

    private async void OnOpenFolderDialogRequested(object? sender, EventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Instance.Get("Dialog_SelectFolder"),
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = folders[0].TryGetLocalPath();
            if (path != null)
                await vm.OnFolderSelected(path);
        }
    }
}
