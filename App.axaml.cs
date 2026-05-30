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

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RenText.Services;
using RenText.ViewModels;
using RenText.Views;

namespace RenText;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        // 言語とテーマをウィンドウ生成前に適用（チラツキ防止）
        LocalizationService.Instance.SetLanguage(settings.Language);
        RequestedThemeVariant = settings.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(settings, settingsService),
            };

            if (settings.WindowWidth.HasValue)  mainWindow.Width  = settings.WindowWidth.Value;
            if (settings.WindowHeight.HasValue) mainWindow.Height = settings.WindowHeight.Value;
            if (settings.IsMaximized)           mainWindow.WindowState = WindowState.Maximized;

            desktop.MainWindow = mainWindow;

            // 起動時に開くパスを決定：引数 > 前回パス（どちらも存在しないディレクトリなら開かない）
            var argPath = desktop.Args is { Length: > 0 } args ? args[0] : null;
            var startupPath =
                argPath != null && Directory.Exists(argPath)         ? argPath :
                !string.IsNullOrEmpty(settings.LastOpenedPath) &&
                    Directory.Exists(settings.LastOpenedPath)        ? settings.LastOpenedPath :
                null;

            if (startupPath != null && mainWindow.DataContext is MainWindowViewModel vm)
            {
                mainWindow.Opened += async (_, _) =>
                {
                    vm.CurrentPath = startupPath;
                    await vm.LoadFolderAsync(startupPath);
                    await vm.ExpandTreeToPath(startupPath);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
