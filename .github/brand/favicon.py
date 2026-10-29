"""Builds favicon.ico (16, 32 and 48 px) from icon-512.png. Needs Pillow."""
import os
from PIL import Image

here = os.path.dirname(os.path.abspath(__file__))
icon = Image.open(os.path.join(here, "icon-512.png")).convert("RGBA")
icon.save(os.path.join(here, "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48)])
