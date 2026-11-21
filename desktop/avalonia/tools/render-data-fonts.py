"""Convert the reference UI's WOFF2 reading and code faces for native text shaping.

Run with fontTools (including Brotli) when the frontend fonts change.
The OFL notices are linked into the shell output beside the application.
"""
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

desktop = Path(__file__).resolve().parents[2]
source = desktop / "frontend-next/public/fonts"
destination = desktop / "avalonia/src/Reasonix.Studio.Shell/Assets/DataFonts"
destination.mkdir(parents=True, exist_ok=True)

for filename, stem, family in (
    ("noto-studio-core.woff2", "StudioCjk", "Studio CJK Text"),
    ("IBMPlexMonoVariable.woff2", "StudioMono", "Studio Code Text"),
):
    with TTFont(source / filename, recalcTimestamp=False) as font:
        axes = {a.axisTag: 500 if a.axisTag == "wght" else a.defaultValue for a in font["fvar"].axes}
        instantiateVariableFont(font, axes, inplace=True)
        font.flavor = None
        names = {1: family, 2: "Regular", 3: family + " Regular", 4: family,
                 6: stem + "-Regular", 16: family, 17: "Regular"}
        for record in font["name"].names:
            if record.nameID in names:
                record.string = names[record.nameID].encode(record.getEncoding())
        font.save(destination / (stem + ".ttf"))
