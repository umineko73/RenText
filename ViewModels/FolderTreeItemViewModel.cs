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
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RenText.Services;

namespace RenText.ViewModels;

public partial class FolderTreeItemViewModel : ObservableObject
{
    private readonly FileSystemService _fsService;
    private readonly Func<bool> _showHidden;
    private bool _childrenLoaded;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _fullPath = "";
    public bool IsDrive { get; }

    public ObservableCollection<FolderTreeItemViewModel> Children { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    // ドライブ・フォルダ問わず同じフォルダアイコンを使用
    public string Icon => IsExpanded ? "\U0001F4C2" : "\U0001F4C1";

    public FolderTreeItemViewModel(string path, FileSystemService fsService, Func<bool> showHidden)
    {
        _fullPath = path;
        _name = Path.GetFileName(path) is { Length: > 0 } n ? n : path;
        IsDrive = Path.GetPathRoot(path) == path;
        _fsService = fsService;
        _showHidden = showHidden;

        if (_fsService.HasSubDirectories(path, showHidden()))
            Children.Add(new FolderTreeItemViewModel("", fsService, showHidden));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded)
            LoadChildren();
    }

    private void LoadChildren()
    {
        Children.Clear();
        foreach (var dir in _fsService.GetSubDirectories(FullPath, _showHidden()))
            Children.Add(new FolderTreeItemViewModel(dir, _fsService, _showHidden));
        _childrenLoaded = true;
    }

    /// <summary>外部リネーム追従用：パスと表示名を更新する。</summary>
    public void UpdatePath(string newPath)
    {
        FullPath = newPath;
        Name = Path.GetFileName(newPath) is { Length: > 0 } n ? n : newPath;
    }
}
