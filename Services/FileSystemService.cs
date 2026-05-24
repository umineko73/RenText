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

using RenText.Models;

namespace RenText.Services;

public class FileSystemService
{
    // ディレクトリを先に、ファイルを後に並べて返す。
    // ソート順は ViewModel の ApplySort() で一元管理するためここでは固定しない。
    // 例外（UnauthorizedAccessException など）は呼び出し側で処理する。
    public IReadOnlyList<FileEntry> GetEntries(string path, bool showHidden = false)
    {
        var dirs = Directory.GetDirectories(path)
            .Select(d => new DirectoryInfo(d))
            .Where(d => showHidden || !IsHiddenOrSystem(d.Attributes))
            // Windows の ReadOnly 属性はフォルダに対しては「カスタムビュー設定あり」を意味するだけで
            // リネーム可否とは無関係のため、ディレクトリは常に IsReadOnly = false とする。
            .Select(d => new FileEntry(d.Parent!.FullName, d.Name, d.LastWriteTime, 0,
                                       false, true));

        var files = Directory.GetFiles(path)
            .Select(f => new FileInfo(f))
            .Where(f => showHidden || !IsHiddenOrSystem(f.Attributes))
            .Select(f => new FileEntry(f.DirectoryName!, f.Name, f.LastWriteTime, f.Length,
                                       f.Attributes.HasFlag(FileAttributes.ReadOnly), false));

        return dirs.Concat(files).ToList();
    }

    public IReadOnlyList<string> GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToList();
    }

    public IReadOnlyList<string> GetSubDirectories(string path, bool showHidden = false)
    {
        try
        {
            return Directory.GetDirectories(path)
                .Select(d => new DirectoryInfo(d))
                .Where(d => showHidden || !IsHiddenOrSystem(d.Attributes))
                .OrderBy(d => d.FullName)
                .Select(d => d.FullName)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public bool HasSubDirectories(string path, bool showHidden = false)
    {
        try
        {
            return Directory.GetDirectories(path)
                .Select(d => new DirectoryInfo(d))
                .Any(d => showHidden || !IsHiddenOrSystem(d.Attributes));
        }
        catch { return false; }
    }

    private static bool IsHiddenOrSystem(FileAttributes attrs) =>
        attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System);
}
