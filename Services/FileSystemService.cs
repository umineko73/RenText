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
    // 例外（UnauthorizedAccessException など）は呼び出し側で処理する。
    // 並び順は ViewModel の ApplySort() で一元管理するためここではソートしない。
    public IReadOnlyList<FileEntry> GetFiles(string path, bool showHidden = false)
    {
        return Directory.GetFiles(path)
            .Select(f => new FileInfo(f))
            .Where(f => showHidden || !IsHiddenOrSystem(f.Attributes))
            .Select(f => new FileEntry(f.DirectoryName!, f.Name, f.LastWriteTime, f.Length,
                                       f.Attributes.HasFlag(FileAttributes.ReadOnly)))
            .ToList();
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
