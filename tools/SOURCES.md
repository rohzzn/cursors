# Bundled pack sources

Every bundled pack is rebuilt from the openly licensed upstream files below:

```bash
powershell -ExecutionPolicy Bypass -File tools\fetch-pack-sources.ps1 -Destination D:\cursor-sources
```

```bash
powershell -ExecutionPolicy Bypass -File tools\build-packs.ps1 -Sources D:\cursor-sources -ApplyTest
```

Rebuilding from a fresh download reproduces the published `packs\` byte for byte. All 1,015 files were checked this way.

The first command downloads the sources below and checks them against pinned hashes. Release archives, the Debian package and the license texts have SHA-256 hashes in the script. Repository folders are fetched at the listed commits. The second command converts and verifies the packs and publishes them to `packs\`.

Packs with unclear licensing were left out on purpose: Figma, Minecraft, other game-branded sets, and DeviantArt uploads without a license.

## Release downloads

| Source | Version | Files | License |
| --- | --- | --- | --- |
| [ful1e5/Bibata_Cursor](https://github.com/ful1e5/Bibata_Cursor) | v2.0.7 | Bibata-{Modern,Original}-{Classic,Ice}-Windows.zip, Bibata-Modern-Amber-Windows.zip | GPL-3.0 |
| [ful1e5/Bibata_Extra_Cursor](https://github.com/ful1e5/Bibata_Extra_Cursor) | v1.0.1 | Bibata-Modern-{DodgerBlue,Turquoise,Pink}-Windows.zip | GPL-3.0 |
| [ful1e5/Bibata_Cursor_Rainbow](https://github.com/ful1e5/Bibata_Cursor_Rainbow) | v1.1.2 | Bibata-Rainbow-{Modern,Original}-Windows.zip | GPL-3.0 |
| [ful1e5/Bibata-Bee-Cursor](https://github.com/ful1e5/Bibata-Bee-Cursor) | v1.0.0 | Bibata-Bee-Modern-Windows.zip | GPL-3.0 |
| [ful1e5/Bibata-Zebra-Cursor](https://github.com/ful1e5/Bibata-Zebra-Cursor) | v1.0.0 | Bibata-Zebra-Modern-Windows.zip | GPL-3.0 |
| [ful1e5/apple_cursor](https://github.com/ful1e5/apple_cursor) | v2.0.1 | macOS-Windows.zip, macOS-White-Windows.zip | GPL-3.0 |
| [ful1e5/XCursor-pro](https://github.com/ful1e5/XCursor-pro) | v2.0.2 | XCursor-Pro-{Dark,Light,Red}-Windows.zip | GPL-3.0 |
| [ful1e5/Google_Cursor](https://github.com/ful1e5/Google_Cursor) | v2.0.0 | GoogleDot-{Black,White,Blue}-Windows.zip | GPL-3.0 |
| [ful1e5/fuchsia-cursor](https://github.com/ful1e5/fuchsia-cursor) | v2.0.1 | Fuchsia-Windows.zip, Fuchsia-Pop-Windows.zip | GPL-3.0 |
| [ful1e5/banana-cursor](https://github.com/ful1e5/banana-cursor) | v2.0.0 | Banana-Windows.zip, Banana-Blue-Windows.zip | GPL-3.0 |
| [ful1e5/BreezeX_Cursor](https://github.com/ful1e5/BreezeX_Cursor) | v2.0.1 | BreezeX-{Black,Light}-Windows.zip | GPL-3.0 |
| [ful1e5/notwaita-cursor](https://github.com/ful1e5/notwaita-cursor) | v1.0.0-alpha1 | Notwaita-{Black,White}-Windows.zip | LGPL-3.0 or CC-BY-SA-3.0 |
| [guillaumeboehm/Nordzy-cursors](https://github.com/guillaumeboehm/Nordzy-cursors) | v2.4.0 | Nordzy-cursors_windows.zip, Nordzy-cursors-white_windows.zip | GPL-3.0 |
| [useless-anvil/retrosmart-cursor](https://github.com/useless-anvil/retrosmart-cursor) | v2.0.1 | retrosmart-cursor-classic-v2.0.1-windows.zip | GPL-3.0 |
| [Tech-Tac/aosp-cursors](https://github.com/Tech-Tac/aosp-cursors) | 1.3.1 | aosp-cursors-windows-1.3.1.zip | Apache-2.0 (art) |
| [phisch/phinger-cursors](https://github.com/phisch/phinger-cursors) | v2.1 | phinger-cursors-variants.tar.bz2 | CC-BY-SA-4.0 |
| [catppuccin/cursors](https://github.com/catppuccin/cursors) | v2.0.0 | catppuccin-{mocha-dark,latte-pink,macchiato-mauve}-cursors.zip | GPL-2.0 |
| [Enthymeme/hackneyed-x11-cursors](https://gitlab.com/Enthymeme/hackneyed-x11-cursors) | 0.9.3 | Hackneyed-Windows-0.9.3.zip, Hackneyed-Dark-Windows-0.9.3.zip (release uploads) | X11 |
| [Kenney Cursor Pack](https://kenney.nl/assets/cursor-pack) | 1.1 | kenney_cursor-pack.zip | CC0-1.0 |
| [Kenney Cursor Pixel Pack](https://kenney.nl/assets/cursor-pixel-pack) | 1.0 | kenney_cursor-pixel-pack.zip | CC0-1.0 |
| [ComixCursors](https://gitlab.com/limitland/comixcursors) (Debian) | 0.9.1-3 | comixcursors-righthanded_0.9.1-3_all.deb | GPL-3.0+ |

## Repository folders

| Source | Commit | Folder | License |
| --- | --- | --- | --- |
| [keeferrourke/capitaine-cursors](https://github.com/keeferrourke/capitaine-cursors) | 06c88433 | `.windows/` | LGPL-3.0 |
| [vinceliuice/Vimix-cursors](https://github.com/vinceliuice/Vimix-cursors) | 9bc292f4 | `.windows/` | GPL-3.0 |
| [emvaized/modern_inverted_mouse_cursors](https://github.com/emvaized/modern_inverted_mouse_cursors) | 77652f49 | `src/` | MIT |
| [vinceliuice/WhiteSur-cursors](https://github.com/vinceliuice/WhiteSur-cursors) | e190baf6 | `dist/cursors/` | GPL-3.0 |
| [yeyushengfan258/Future-cursors](https://github.com/yeyushengfan258/Future-cursors) | 587c14d2 | `dist/cursors/` | GPL-3.0 |
| [yeyushengfan258/Lyra-Cursors](https://github.com/yeyushengfan258/Lyra-Cursors) | c096c540 | `dist/cursors/` | GPL-3.0 |
| [silica-dev/Bibata_Cursor_Translucent](https://github.com/silica-dev/Bibata_Cursor_Translucent) | 34df7561 | `Bibata_{Ghost,Spirit,Tinted}/cursors/` (only the 15 cursors the recipe maps) | GPL-3.0 |

## Layout produced

```
raw/<owner>__<repo>/<file>        downloads, plus _LICENSE / _COPYING texts
work/<owner>__<repo>/<zip name>/  each release zip extracted
work/kenney/<zip name>/           Kenney zips extracted
work/rs/{win,mac,term}/           the three Retrosmart variants (short paths avoid MAX_PATH)
work/phinger-full/                phinger variants, with _symlinks.tsv
work/comix-data/                  data from the ComixCursors .deb, with _symlinks.tsv
raw/bibata-translucent/<variant>/cursors/
```

Windows' `tar` can't create symlinks, so tar archives record their links in `_symlinks.tsv`. Symlinks in GitHub folders download as small text files holding the target. PackTool follows both.
