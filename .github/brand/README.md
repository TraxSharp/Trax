# The Trax .NET mark

The mark is a route: a line from state A through state B to state C, the one it reached. The
wordmark is JetBrains Mono Bold, converted to outlines, with ".NET" lighter beside "Trax".

| File | Use |
|---|---|
| `lockup.svg`, `lockup-on-dark.svg` | The mark and the wordmark, on a light or a dark ground |
| `mark.svg`, `mark-on-dark.svg` | The mark alone |
| `icon.svg`, `icon-512.png`, `icon-192.png`, `icon-128.png` | The mark on a green tile: avatars, the NuGet packages (`icon-128.png`) |
| `icon-square.svg`, `apple-touch-icon.png` | The tile without rounded corners, for Apple, which rounds them itself |
| `favicon.ico` | 16, 32 and 48 px |
| `site-icon*.svg`, `site-icon-*.png`, `site-apple-touch-icon.png`, `site-favicon.ico` | The same on the lighter green, for the site |

| Colour | Where |
|---|---|
| `#1f5f41` | The mark on a light ground, and the icon tile |
| `#6cc596` | The mark on a dark ground, and the site's icon tile |
| `#eef6f1` | The mark inside the icon tile |
| `#0d2017` | The mark inside the site's tile, which is `#6cc596` |

Use the `-on-dark` files on a dark ground. Do not stretch, recolour or outline the mark, and keep
at least half its height clear around it.

## Redrawing

`draw.py` writes every SVG here, the site's `Trax.Website/src/components/layout/TraxLogo.tsx` and
the operations console's `Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/TraxLogo.tsx`.
Edit the mark there, never the output. From the repository root, with JetBrains Mono Bold
([v2.304](https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304), SIL Open Font
License) at `JetBrainsMono-Bold.ttf`:

```bash
podman run --rm -v "$PWD:/repo:Z" -v "$(dirname "$(realpath JetBrainsMono-Bold.ttf)"):/font:ro,Z" \
  docker.io/library/python:3.12-slim sh -c \
  "pip -q install fonttools==4.54.1 && python3 -I /repo/.github/brand/draw.py /repo/.github/brand /font/JetBrainsMono-Bold.ttf"
podman run --rm --userns=keep-id --shm-size=512m -v "$PWD/.github/brand:/b:Z" -w /b --entrypoint sh \
  docker.io/zenika/alpine-chrome:with-node -c ./render.sh
podman run --rm -v "$PWD/.github/brand:/b:Z" docker.io/library/python:3.12-slim sh -c \
  "pip -q install pillow==11.0.0 && python3 -I /b/favicon.py"
cp .github/brand/site-icon-192.png Trax.Website/src/app/icon.png
cp .github/brand/site-apple-touch-icon.png Trax.Website/src/app/apple-icon.png
cp .github/brand/site-favicon.ico Trax.Website/src/app/favicon.ico
```

The font is used only to draw the outlines; it is not shipped.
