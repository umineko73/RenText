# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-05-21

### Added

#### Core editing
- Text editor style filename editing — one line per file, edit directly like a text editor
- Live preview pane showing `original → new` for all pending changes
- Save with full rollback (`Ctrl+S`) — if any rename fails, all changes are undone atomically
- Cycle-aware rename execution — swapping filenames (e.g. A→B and B→A simultaneously) handled correctly via topological sort with temporary file fallback

#### Search & Replace
- VS Code-style search & replace panel (`Ctrl+H`)
- Regular expression support
- Case-sensitive matching option
- Real-time preview pane updates as you type the pattern

#### File list
- Sort by name / date modified / extension / size (ascending & descending)
- Display modes: filename only / with date modified / with size / with date + size
- Keyboard navigation with `↑` `↓` `PageUp` `PageDown` (cursor column position preserved across lines)
- Line number indicator (`Ln N`) in status bar

#### Folder tree
- Lazy-loaded folder tree with drive icons
- Horizontal scroll suppression (tree does not scroll sideways on node selection or expansion)

#### Auto-refresh
- Automatic file list refresh when files are added, removed, or renamed by external applications
- Follows current folder rename or deletion by external applications (address bar and tree node update automatically)
- Refresh paused when unsaved edits are present (status bar notification shown)

#### UI / UX
- Dark mode and Light mode toggle
- Japanese / English language toggle (persisted across sessions)
- Hidden and system file visibility toggle
- Save success toast notification (1-second overlay)
- Per-line error highlighting with tooltip message
- Settings persistence (`settings.json`) for theme and language preferences

#### Distribution
- Self-contained Windows x64 build — no .NET runtime installation required

---

[Unreleased]: https://github.com/umineko73/RenText/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/umineko73/RenText/releases/tag/v0.1.0
