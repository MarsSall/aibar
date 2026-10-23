# AIBar icon

The AIBar icon is a brain filled with liquid gold: how much AI you have left. Gold is provider-neutral, so the mark stays valid as providers are added.

`aibar_icon.py` draws two SVG sources, and `build.sh` renders every asset from them. Commit the generated files together with any change to the generator.

Windows picks the tray frame from the display scale (16 px at 100%, 24 px at 150%, 32 px at 200%, 40 px at 250%), so every frame up to 48 px is the untiled glyph: a tile would shrink the brain next to other tray icons.

| Output | Use |
| --- | --- |
| `src/AIBar.Desktop/Assets/aibar.svg` | Detailed icon on the rounded dark tile, for 64 px and up and the MSIX logos |
| `src/AIBar.Desktop/Assets/aibar-small.svg` | Simplified glyph without a tile that fills the whole frame, for the tray and taskbar (16 to 48 px) |
| `src/AIBar.Desktop/Assets/AIBar.ico` | 16, 20, 24, 32, 40 and 48 px from the glyph; 64 and 256 px from the tile. Used by the executable, the windows and the tray |
| `packaging/Assets/*.png` | MSIX logos |

## Regenerate

`build.sh` needs `python3`, [`resvg`](https://github.com/linebender/resvg) and ImageMagick. The output is byte-for-byte reproducible.

```bash
nix shell nixpkgs#resvg nixpkgs#imagemagick --command tools/icon/build.sh
```

To change the look, edit the parameters at the bottom of `aibar_icon.py`. For example, `level` sets the height of the gold surface: lower values mean a fuller brain.
