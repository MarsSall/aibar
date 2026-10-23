#!/usr/bin/env bash
# Regenerate every AIBar icon asset from tools/icon/aibar_icon.py.
# Needs python3, resvg and ImageMagick (`convert`). On Nix: nix shell nixpkgs#resvg nixpkgs#imagemagick.
#
#   src/AIBar.Desktop/Assets/aibar.svg, aibar-small.svg   vector sources
#   src/AIBar.Desktop/Assets/AIBar.ico                    16-48 untiled tray/taskbar glyph + 64/256 tile
#   packaging/Assets/*.png                                MSIX logos
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
assets="$root/src/AIBar.Desktop/Assets"
packaging="$root/packaging/Assets"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

python3 -I "$root/tools/icon/aibar_icon.py" "$assets"

render() { resvg -w "$2" -h "$2" "$assets/$1" "$work/$3"; }

frames=()
for size in 16 20 24 32 40 48; do render aibar-small.svg "$size" "$size.png"; frames+=("$work/$size.png"); done
for size in 64 256; do render aibar.svg "$size" "$size.png"; frames+=("$work/$size.png"); done
convert "${frames[@]}" "$assets/AIBar.ico"

render aibar.svg 50 store.png
render aibar.svg 44 square44.png
render aibar.svg 150 square150.png
convert "$work/store.png" -strip "png32:$packaging/StoreLogo.png"
convert "$work/square44.png" -strip "png32:$packaging/Square44x44Logo.png"
convert "$work/square150.png" -strip "png32:$packaging/Square150x150Logo.png"
convert "$work/square150.png" -background none -gravity center -extent 310x150 -strip "png32:$packaging/Wide310x150Logo.png"
