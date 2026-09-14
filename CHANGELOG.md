# Changelog

All notable changes to Cursors are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-09-14

First public release.

### Added

- **One-click cursor schemes.** Clicking a card applies all 17 Windows cursor roles at once, and the choice persists across restarts with no background process.
- **Built-in library.** 60 openly licensed packs in nine categories: Minimal, macOS, Retro, Pixel & Gaming, Neon, Glass, Cute and Animated, plus the styles built into Windows.
- **Restore default.** One click puts back the Windows Default cursors. `Cursors.exe --restore` does the same without opening the window.
- **Your own packs.** Import `.zip` files, `install.inf` files, or loose `.cur`/`.ani` files, from a file picker or by dragging them onto the window. Roles are detected from the `install.inf` or from file names, and scrambled inf role orders are repaired.
- **Previews.** Large previews of each pack, with animated `.ani` playback and the pack's link, text and busy cursors revealed on hover.
- **Browsing.** Category filters, section headers, keyboard navigation, and a right-click menu with credits, the project page and the pack's folder.
- **Native dark window.** A custom title bar that keeps snap layouts, Aero Snap, resize borders and the DWM shadow, with per-monitor DPI awareness.
- **Reproducible library.** Tooling to rebuild the library from pinned upstream sources and verify it: `tools/fetch-pack-sources.ps1`, `tools/build-packs.ps1` and PackTool.
