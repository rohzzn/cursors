# Changelog

All notable changes to Cursors are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **More from the catalog script.** `tools/update-catalog.ps1` now lists every set that downloads as one file unless told otherwise (`-Count 500 -MinRoles 3` gives the shipped list), and records each set's cursor count, roles, animation and the site's top-rated mark.
- **Sidebar.** Browse All packs, Built-in, Community or My packs, or go straight to a built-in style, with a count beside each.
- **Sort and filters.** Sort community sets by downloads, rating or name. Filter them to full sets, animated sets, top-rated sets or open licenses. The header shows how many sets match.

### Changed

- **No more category chips.** The row of tags above the grid is replaced by the sidebar and the Sort and Filters menus.
- **Built for thousands of cards.** Only cards on screen animate and load previews, and decoded previews are capped while you scroll. Search compares prepared text, so the whole library opens, filters and searches without a pause.
- **A cleaner community list.** Besides sets the site marks as adult content, sets with explicit or hateful words in their names are left out.

## [1.1.0] - 2026-09-15

### Added

- **Search.** Filter the library as you type by pack name, style or author, combined with the category chips. Start typing anywhere in the window, or press <kbd>Ctrl</kbd>+<kbd>F</kbd>.
- **Import from a link.** Add a cursor set straight from a set page, such as one on rw-designer.com, or from a direct `.zip`, `.cur` or `.ani` link. Use **Add pack → From a link…**, press <kbd>Ctrl</kbd>+<kbd>L</kbd>, paste a link, or drag one from your browser onto the window. The set's name, license and source are saved with it and shown when you right-click it.
- **Community section.** The 500 most-downloaded complete cursor sets on rw-designer.com, listed in `packs/catalog.tsv` by the new `tools/update-catalog.ps1`. Previews load as cards scroll into view. Clicking a set downloads it, adds it to your library and applies it. Right-click shows its author, license and download count.
- **Start menu and desktop shortcuts.** The first launch adds both, so Cursors shows up in Windows search, and moves them along if the app folder moves. `Cursors.exe --uninstall` restores the Windows cursors and removes them.

### Changed

- **Better role detection.** Imports understand cursor-site file names such as `Diamond Sword - Normal Select.cur`, where the role comes after the last dash. Sets from rw-designer.com use the role the site tags each cursor with.
- **Credits for added packs.** Packs you add keep their author, license and source link.

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
