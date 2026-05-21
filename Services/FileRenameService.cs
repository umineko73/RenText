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

public record RenameResult(bool Success, string? ErrorMessage = null, int ErrorIndex = -1);

public class FileRenameService
{
    private static LocalizationService L => LocalizationService.Instance;

    // Windows 予約名（拡張子の有無に関わらず使用不可）
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9",
    };

    // Windows のフルパス長制限（既定。長パス対応なしで安全側）
    private const int MaxPathLength = 259;

    public RenameResult Validate(string directoryPath, IList<FileEntry> originals, IList<string> newNames)
    {
        if (originals.Count != newNames.Count)
            return new RenameResult(false, L.Get("Err_CountMismatch"));

        for (int i = 0; i < newNames.Count; i++)
        {
            var name = newNames[i].Trim();
            var lineNo = i + 1;

            if (string.IsNullOrWhiteSpace(name))
                return new RenameResult(false, string.Format(L.Get("Err_EmptyName"), lineNo), i);

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return new RenameResult(false, string.Format(L.Get("Err_InvalidChars"), lineNo, name), i);

            // 末尾のピリオド・空白（Windows で作成できない）
            if (name.EndsWith('.') || name.EndsWith(' '))
                return new RenameResult(false, string.Format(L.Get("Err_TrailingDotOrSpace"), lineNo, name), i);

            // Windows 予約名（拡張子を除いたベース名で判定）
            var baseName = Path.GetFileNameWithoutExtension(name);
            if (ReservedNames.Contains(baseName))
                return new RenameResult(false, string.Format(L.Get("Err_ReservedName"), lineNo, name), i);

            // パス全体の長さ
            var targetPath = Path.Combine(directoryPath, name);
            if (targetPath.Length > MaxPathLength)
                return new RenameResult(false,
                    string.Format(L.Get("Err_PathTooLong"), lineNo, targetPath.Length, MaxPathLength), i);

            // 新しい名前同士で重複チェック（大文字小文字区別なし）
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(newNames[j].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return new RenameResult(false, string.Format(L.Get("Err_Duplicate"), lineNo, name), i);
            }

            // 変更がある場合のみ既存ファイルとの衝突チェック。
            // ただし、ターゲット名と同名のファイルが同バッチ内で別名にリネームされる予定なら衝突しない。
            if (!string.Equals(originals[i].OriginalName, name, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(targetPath) && !IsBeingRenamedAway(originals, newNames, name))
                    return new RenameResult(false, string.Format(L.Get("Err_AlreadyExists"), lineNo, name), i);
            }
        }

        return new RenameResult(true);
    }

    /// <summary>
    /// <paramref name="name"/> と同名の既存ファイルが、同バッチ内で別名にリネームされる予定かどうかを返す。
    /// </summary>
    private static bool IsBeingRenamedAway(IList<FileEntry> originals, IList<string> newNames, string name)
    {
        for (int j = 0; j < originals.Count; j++)
        {
            if (string.Equals(originals[j].OriginalName, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(newNames[j].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public RenameResult Execute(string directoryPath, IList<FileEntry> originals, IList<string> newNames)
    {
        var validation = Validate(directoryPath, originals, newNames);
        if (!validation.Success) return validation;

        // 実際に変更があるものだけ pending 辞書に登録: oldName → newName
        var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < originals.Count; i++)
        {
            var trimmed = newNames[i].Trim();
            if (!string.Equals(originals[i].OriginalName, trimmed, StringComparison.Ordinal))
                pending[originals[i].OriginalName] = trimmed;
        }

        // ロールバック用: (実行後のパス → 元のパス) の逆順リスト
        var completed = new List<(string From, string To)>();

        try
        {
            while (pending.Count > 0)
            {
                // ターゲットが他のリネームのソース（移動元）になっていないもの = 即座に実行可能
                // 例: A→B, B→C のとき B はソースなので A→B は先に実行してはいけない。
                //     C はソースでないので B→C が実行可能。
                var ready = pending
                    .Where(kv => !pending.ContainsKey(kv.Value))
                    .ToList();

                if (ready.Count > 0)
                {
                    foreach (var (from, to) in ready)
                    {
                        var src = Path.Combine(directoryPath, from);
                        var dst = Path.Combine(directoryPath, to);
                        File.Move(src, dst);
                        completed.Add((dst, src)); // 逆向きで保存
                        pending.Remove(from);
                    }
                }
                else
                {
                    // pending に残っているのはすべてサイクル（例: A→B, B→A）。
                    // 先頭の1件を一時ファイル名にリネームしてサイクルを解消する。
                    var cycleFrom = pending.Keys.First();
                    var cycleTo   = pending[cycleFrom];
                    var tempName  = GenerateTempName(directoryPath, cycleFrom);

                    var src = Path.Combine(directoryPath, cycleFrom);
                    var tmp = Path.Combine(directoryPath, tempName);
                    File.Move(src, tmp);
                    completed.Add((tmp, src));

                    pending.Remove(cycleFrom);
                    pending[tempName] = cycleTo; // 一時名 → 本来の宛先 に差し替え
                }
            }

            return new RenameResult(true);
        }
        catch (Exception ex)
        {
            // 完全ロールバック（逆順）
            for (int i = completed.Count - 1; i >= 0; i--)
            {
                try { File.Move(completed[i].From, completed[i].To); }
                catch { /* ベストエフォート */ }
            }

            return new RenameResult(false, string.Format(L.Get("Err_RenameFailed"), ex.Message));
        }
    }

    /// <summary>
    /// ディレクトリ内に存在しない一時ファイル名を生成する。
    /// </summary>
    private static string GenerateTempName(string directoryPath, string baseName)
    {
        string tempName;
        do
        {
            tempName = $"__rentext_{Guid.NewGuid():N}_{baseName}";
        } while (File.Exists(Path.Combine(directoryPath, tempName)));
        return tempName;
    }
}
