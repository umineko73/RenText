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

using CommunityToolkit.Mvvm.ComponentModel;
using RenText.Models;
using RenText.Services;

namespace RenText.ViewModels;

public partial class FileLineViewModel : ObservableObject
{
    public FileEntry Source { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(ShowInPreview))]
    [NotifyPropertyChangedFor(nameof(PreviewResult))]
    private string _editedName;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipMessage))]
    private string? _errorMessage;

    private DisplayFormat _displayFormat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(ShowInPreview))]
    [NotifyPropertyChangedFor(nameof(PreviewResult))]
    private string? _previewName;

    public bool IsReadOnly   => Source.IsReadOnly;
    public bool IsModified   => !IsReadOnly && EditedName.Trim() != Source.OriginalName;
    public bool HasPreview   => PreviewName != null && PreviewName != EditedName;
    public bool ShowInPreview => IsModified || HasPreview;
    public string PreviewResult => PreviewName ?? EditedName;

    // ツールチップ: エラー > 読み取り専用 > null（表示なし）の優先順
    public string? TooltipMessage =>
        ErrorMessage ?? (IsReadOnly ? LocalizationService.Instance.Get("Tooltip_ReadOnly") : null);

    // 日時・サイズの読み取り専用プレフィックス
    public string PrefixText => _displayFormat switch
    {
        DisplayFormat.WithDate        => $"{Source.LastModified:yyyy-MM-dd HH:mm}",
        DisplayFormat.WithSize        => FormatSize(Source.FileSize),
        DisplayFormat.WithDateAndSize => $"{Source.LastModified:yyyy-MM-dd HH:mm}  {FormatSize(Source.FileSize)}",
        _                             => ""
    };

    public bool HasPrefix => _displayFormat != DisplayFormat.NameOnly;

    // フォーマットごとの固定幅（ファイル名の開始位置を揃える）
    public double PrefixWidth => _displayFormat switch
    {
        DisplayFormat.WithDate        => 135,
        DisplayFormat.WithSize        => 72,
        DisplayFormat.WithDateAndSize => 210,
        _                             => 0
    };

    public FileLineViewModel(FileEntry source, DisplayFormat format)
    {
        Source = source;
        _editedName = source.OriginalName;
        _displayFormat = format;
    }

    public void UpdateDisplayFormat(DisplayFormat format)
    {
        _displayFormat = format;
        OnPropertyChanged(nameof(PrefixText));
        OnPropertyChanged(nameof(HasPrefix));
        OnPropertyChanged(nameof(PrefixWidth));
    }

    public void ResetToOriginal()
    {
        EditedName = Source.OriginalName;
        HasError = false;
        ErrorMessage = null;
    }

    private static string FormatSize(long bytes)
    {
        var s = bytes switch
        {
            >= 1_099_511_627_776 => $"{bytes / 1_099_511_627_776.0:F1}TB",
            >= 1_073_741_824     => $"{bytes / 1_073_741_824.0:F1}GB",
            >= 1_048_576         => $"{bytes / 1_048_576.0:F1}MB",
            >= 1_024             => $"{bytes / 1_024.0:F1}KB",
            _                    => $"{bytes}B"
        };
        return s.PadLeft(7); // 最大 "1023.9KB" = 7文字で統一
    }
}
