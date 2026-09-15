# Bundled cursor packs

The cursor packs in this folder are separate works by their authors, redistributed unchanged in design under their own licenses. Full license texts are in `LICENSES/`. They are not part of the Cursors app code.

`catalog.tsv` is not a cursor pack. It lists the Community sets hosted on rw-designer.com, with their names, authors, download counts, licenses and page links. No files from those sets are included here. The app downloads a set from rw-designer.com only when someone picks it, and each set is licensed by its author as stated on its page.

Changes made for bundling:

- Windows builds: files renamed to Windows role names; animated cursors keep only their 32, 48 and 64 px frames.
- Linux (Xcursor) themes and Kenney PNG icons: converted to .cur/.ani; missing roles reuse the pack's own closest cursor.
- Neon packs: Bibata Modern Classic with the outline recolored and a glow added.

The conversion tool (`tools/PackTool`), the recipe (`tools/packs.recipe`) and the upstream source list (`tools/SOURCES.md`) are the corresponding source for these changes.

| Pack | Category | Author | License | Source | Notes |
| --- | --- | --- | --- | --- | --- |
| Bibata Modern Classic | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor |  |
| Bibata Modern Ice | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor |  |
| XCursor Pro Dark | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/XCursor-pro |  |
| XCursor Pro Light | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/XCursor-pro |  |
| Phinger Dark | Minimal | Philipp Schaffrath (phisch) | CC-BY-SA-4.0 | https://github.com/phisch/phinger-cursors | Converted from the Xcursor theme to .cur/.ani |
| Phinger Light | Minimal | Philipp Schaffrath (phisch) | CC-BY-SA-4.0 | https://github.com/phisch/phinger-cursors | Converted from the Xcursor theme to .cur/.ani |
| Bibata Original Classic | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor |  |
| Bibata Original Ice | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor |  |
| BreezeX Black | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/BreezeX_Cursor |  |
| BreezeX Light | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/BreezeX_Cursor |  |
| Notwaita Black | Minimal | donut2; Windows build by Kaiz Khatri (ful1e5) | LGPL-3.0-only OR CC-BY-SA-3.0 | https://github.com/ful1e5/notwaita-cursor |  |
| Notwaita White | Minimal | donut2; Windows build by Kaiz Khatri (ful1e5) | LGPL-3.0-only OR CC-BY-SA-3.0 | https://github.com/ful1e5/notwaita-cursor |  |
| Nordzy | Minimal | Guillaume Boehm, based on Vimix | GPL-3.0-only | https://github.com/guillaumeboehm/Nordzy-cursors |  |
| Nordzy White | Minimal | Guillaume Boehm, based on Vimix | GPL-3.0-only | https://github.com/guillaumeboehm/Nordzy-cursors |  |
| Android | Minimal | The Android Open Source Project; Windows build by Tech-Tac | Apache-2.0 | https://github.com/Tech-Tac/aosp-cursors |  |
| Modern Inverted | Minimal | emvaized (Max Tsyba) | MIT | https://github.com/emvaized/modern_inverted_mouse_cursors |  |
| Vimix | Minimal | Vince Liuice, based on Capitaine | GPL-3.0-only | https://github.com/vinceliuice/Vimix-cursors |  |
| Fuchsia | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/fuchsia-cursor |  |
| Google Dot Black | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Google_Cursor |  |
| Google Dot White | Minimal | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Google_Cursor |  |
| macOS | macOS | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/apple_cursor |  |
| macOS White | macOS | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/apple_cursor |  |
| WhiteSur | macOS | Vince Liuice, based on Capitaine | GPL-3.0-only | https://github.com/vinceliuice/WhiteSur-cursors | Converted from the Xcursor theme to .cur/.ani |
| Capitaine | macOS | Keefer Rourke, based on KDE Breeze | LGPL-3.0-or-later | https://github.com/keeferrourke/capitaine-cursors |  |
| Hackneyed | Retro | Richard Ferreira | X11-distribute-modifications-variant | https://gitlab.com/Enthymeme/hackneyed-x11-cursors |  |
| Hackneyed Dark | Retro | Richard Ferreira | X11-distribute-modifications-variant | https://gitlab.com/Enthymeme/hackneyed-x11-cursors |  |
| Retrosmart Win | Retro | Manuel Domínguez López, useless-anvil | GPL-3.0-only | https://github.com/useless-anvil/retrosmart-cursor |  |
| Retrosmart Mac | Retro | Manuel Domínguez López, useless-anvil | GPL-3.0-only | https://github.com/useless-anvil/retrosmart-cursor |  |
| Retrosmart Terminal | Retro | Manuel Domínguez López, useless-anvil | GPL-3.0-only | https://github.com/useless-anvil/retrosmart-cursor |  |
| Pixel | Pixel & Gaming | Kenney (kenney.nl) | CC0-1.0 | https://kenney.nl/assets/cursor-pixel-pack |  |
| Adventure | Pixel & Gaming | Kenney (kenney.nl) | CC0-1.0 | https://kenney.nl/assets/cursor-pack |  |
| Sci-Fi | Pixel & Gaming | Kenney (kenney.nl) | CC0-1.0 | https://kenney.nl/assets/cursor-pack |  |
| Future | Pixel & Gaming | yeyushengfan258, based on Capitaine | GPL-3.0-only | https://github.com/yeyushengfan258/Future-cursors | Converted from the Xcursor theme to .cur/.ani |
| XCursor Pro Red | Pixel & Gaming | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/XCursor-pro |  |
| Neon Cyan | Neon | Kaiz Khatri (ful1e5); neon glow by Cursors | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor | Derived from Bibata Modern Classic: outline recolored and a glow added |
| Neon Magenta | Neon | Kaiz Khatri (ful1e5); neon glow by Cursors | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor | Derived from Bibata Modern Classic: outline recolored and a glow added |
| Neon Lime | Neon | Kaiz Khatri (ful1e5); neon glow by Cursors | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor | Derived from Bibata Modern Classic: outline recolored and a glow added |
| Bibata Amber | Neon | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor |  |
| Bibata Dodger Blue | Neon | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Extra_Cursor |  |
| Bibata Turquoise | Neon | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Extra_Cursor |  |
| Bibata Pink | Neon | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Extra_Cursor |  |
| Google Dot Blue | Neon | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Google_Cursor |  |
| Fuchsia Pop | Neon | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/fuchsia-cursor |  |
| Lyra | Neon | yeyushengfan258, based on Capitaine | GPL-3.0-only | https://github.com/yeyushengfan258/Lyra-Cursors | Converted from the Xcursor theme to .cur/.ani |
| Ghost | Glass | silica-dev, based on Bibata by Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/silica-dev/Bibata_Cursor_Translucent | Converted from the Xcursor theme to .cur/.ani |
| Spirit | Glass | silica-dev, based on Bibata by Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/silica-dev/Bibata_Cursor_Translucent | Converted from the Xcursor theme to .cur/.ani |
| Tinted Glass | Glass | silica-dev, based on Bibata by Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/silica-dev/Bibata_Cursor_Translucent | Converted from the Xcursor theme to .cur/.ani |
| Banana | Cute | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/banana-cursor |  |
| Banana Blue | Cute | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/banana-cursor |  |
| Toon | Cute | Kenney (kenney.nl) | CC0-1.0 | https://kenney.nl/assets/cursor-pack |  |
| Catppuccin Mocha | Cute | Catppuccin, based on Volantes by varlesh | GPL-2.0-only | https://github.com/catppuccin/cursors | Converted from the Xcursor theme to .cur/.ani |
| Catppuccin Pink | Cute | Catppuccin, based on Volantes by varlesh | GPL-2.0-only | https://github.com/catppuccin/cursors | Converted from the Xcursor theme to .cur/.ani |
| Catppuccin Mauve | Cute | Catppuccin, based on Volantes by varlesh | GPL-2.0-only | https://github.com/catppuccin/cursors | Converted from the Xcursor theme to .cur/.ani |
| Comix White | Cute | Jens Luetkens, Ben Finney | GPL-3.0-or-later | https://gitlab.com/limitland/comixcursors | Converted from the Debian comixcursors-righthanded 0.9.1 Xcursor theme |
| Comix Black | Cute | Jens Luetkens, Ben Finney | GPL-3.0-or-later | https://gitlab.com/limitland/comixcursors | Converted from the Debian comixcursors-righthanded 0.9.1 Xcursor theme |
| Comix Slim Blue | Cute | Jens Luetkens, Ben Finney | GPL-3.0-or-later | https://gitlab.com/limitland/comixcursors | Converted from the Debian comixcursors-righthanded 0.9.1 Xcursor theme |
| Rainbow Modern | Animated | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor_Rainbow |  |
| Rainbow Original | Animated | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata_Cursor_Rainbow |  |
| Bee | Animated | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata-Bee-Cursor |  |
| Zebra | Animated | Kaiz Khatri (ful1e5) | GPL-3.0-only | https://github.com/ful1e5/Bibata-Zebra-Cursor |  |
