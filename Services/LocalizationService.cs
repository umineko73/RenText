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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace RenText.Services;

public class LocalizationService
{
    public static LocalizationService Instance { get; } = new();

    private ResourceDictionary? _currentDict;

    public string CurrentLanguage { get; private set; } = "ja";

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string language)
    {
        CurrentLanguage = language;

        var uri = new Uri($"avares://RenText/Assets/Locales/{language}.axaml");
        var newDict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);

        var resources = Application.Current!.Resources;
        if (_currentDict != null)
            resources.MergedDictionaries.Remove(_currentDict);
        resources.MergedDictionaries.Add(newDict);
        _currentDict = newDict;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (Application.Current?.Resources
                .TryGetResource(key, ThemeVariant.Default, out var val) == true
            && val is string s)
            return s;
        return $"[{key}]";
    }
}
