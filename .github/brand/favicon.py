"""Builds favicon.ico and site-favicon.ico (16, 32 and 48 px) from the 512 px icons. Needs Pillow."""
import os
from PIL import Image

here = os.path.dirname(os.path.abspath(__file__))
for src, out in [("icon-512.png", "favicon.ico"), ("site-icon-512.png", "site-favicon.ico")]:
    icon = Image.open(os.path.join(here, src)).convert("RGBA")
    icon.save(os.path.join(here, out), sizes=[(16, 16), (32, 32), (48, 48)])
