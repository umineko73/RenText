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

namespace RenText.Models;

public class AppSettings
{
    public string  Language    { get; set; } = "ja";
    public string  Theme       { get; set; } = "dark";
    public double? WindowWidth    { get; set; }
    public double? WindowHeight   { get; set; }
    public bool    IsMaximized    { get; set; }
    public double? TreePaneWidth  { get; set; }
    public double? PreviewPaneWidth { get; set; }
}
