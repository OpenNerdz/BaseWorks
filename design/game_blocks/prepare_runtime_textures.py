"""Downsample the authored 2K atlases for the one-metre in-game blocks.

The editable source/Unity textures remain at 2048px. Runtime textures are 1024px
to keep the three families near 64 MiB of GPU memory including mipmaps, rather
than roughly four times that amount. Requires Pillow on the system Python.
"""

from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parent
DEST = ROOT.parents[1] / "package" / "Assets" / "NearbyCraftBlocks"
DEST.mkdir(parents=True, exist_ok=True)

for family in ("console", "workshop", "locker"):
    for suffix in ("BaseColor", "Normal", "MetallicSmoothness", "Emission"):
        name = f"{family}_{suffix}.png"
        with Image.open(ROOT / "textures" / name) as image:
            if image.size != (2048, 2048):
                raise ValueError(f"Expected a 2048px authored atlas: {name}")
            runtime = image.resize((1024, 1024), Image.Resampling.LANCZOS)
            runtime.save(DEST / name, optimize=True)
        print(f"RUNTIME_TEXTURE {name} 1024x1024")
