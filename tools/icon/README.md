# AIBar icon

The AIBar icon is a brain filled with liquid gold: how much AI you have left. Gold is provider-neutral, so the mark stays valid as providers are added.

`aibar_icon.py` draws two SVG sources, and `build.sh` renders every asset from them. Commit the generated files together with any change to the generator.

| Output | Use |
| --- | --- |
| `src/AIBar.Desktop/Assets/aibar.svg` | Detailed icon on the rounded dark tile, for 24 px and up |
| `src/AIBar.Desktop/Assets/aibar-small.svg` | Simplified glyph without a tile, readable at 16 px in the tray |
| `src/AIBar.Desktop/Assets/AIBar.ico` | 16, 20 and 24 px from the glyph; 32, 48, 64 and 256 px from the tile. Used by the executable, the windows and the tray |
| `packaging/Assets/*.png` | MSIX logos |

## Regenerate

`build.sh` needs `python3`, [`resvg`](https://github.com/linebender/resvg) and ImageMagick. The output is byte-for-byte reproducible.

```bash
nix shell nixpkgs#resvg nixpkgs#imagemagick --command tools/icon/build.sh
```

To change the look, edit the parameters at the bottom of `aibar_icon.py`. For example, `level` sets the height of the gold surface: lower values mean a fuller brain.
