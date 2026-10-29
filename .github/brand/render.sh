#!/bin/sh
# Renders the PNG files from the SVGs draw.py writes, with headless Chromium. Every SVG is
# 100 x 100, so the device scale factor sets the size. README.md in this folder runs it in a
# container. Chromium will not scale below half size, so favicon.ico is made from icon-512.png.
set -eu
cd "$(dirname "$0")"

shot() { # svg, size in px, out
  chromium-browser --headless --no-sandbox --disable-gpu --hide-scrollbars \
    --default-background-color=00000000 --window-size=100,100 \
    --force-device-scale-factor="$(awk "BEGIN{print $2/100}")" \
    --screenshot="$3" "file://$PWD/$1" >/dev/null 2>&1
}

shot icon.svg 512 icon-512.png
shot icon.svg 192 icon-192.png
shot icon.svg 128 icon-128.png
shot icon-square.svg 180 apple-touch-icon.png
