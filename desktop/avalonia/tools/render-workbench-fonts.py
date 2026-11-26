"""Build static workbench faces from the reference frontend's licensed fonts."""
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen

desktop = Path(__file__).resolve().parents[2]
source = desktop / "frontend-next/public/fonts"
output = desktop / "avalonia/src/Reasonix.Studio.Shell/Assets/WorkbenchFonts"
output.mkdir(parents=True, exist_ok=True)
styles = {400: "Regular", 500: "Medium", 600: "SemiBold", 700: "Bold"}

for stem, filename, family, weights in (
    ("WorkbenchCjk", "noto-studio-core.woff2", "Reasonix Workbench CJK", (400, 500, 600, 700)),
    ("WorkbenchFallback", "noto-studio.woff2", "Reasonix Workbench Full", (400,)),
    ("WorkbenchUi", "inter-studio-{weight}.ttf", "Reasonix Workbench UI", (400, 500, 600)),
    ("WorkbenchMono", "IBMPlexMonoVariable.woff2", "Reasonix Workbench Mono", (400, 500, 600)),
):
    for weight in weights:
        with TTFont(source / filename.format(weight=weight), recalcTimestamp=False) as font:
            if "fvar" in font:
                axes = {axis.axisTag: weight if axis.axisTag == "wght" else axis.defaultValue for axis in font["fvar"].axes}
                instantiateVariableFont(font, axes, inplace=True)
            font.flavor = None
            if stem == "WorkbenchUi":
                # The UI subset omits ellipsis; native trimming uses the primary face.
                with TTFont(output / f"WorkbenchCjk-{max(500, weight)}.ttf", recalcTimestamp=False) as fallback:
                    scale = font["head"].unitsPerEm / fallback["head"].unitsPerEm
                    glyph_name = fallback.getBestCmap()[0x2026]
                    recording = DecomposingRecordingPen(fallback.getGlyphSet())
                    fallback.getGlyphSet()[glyph_name].draw(recording)
                    pen = TTGlyphPen(None)
                    recording.replay(TransformPen(pen, (scale, 0, 0, scale, 0, 0)))
                    name = "reference.ellipsis"
                    order = list(font.getGlyphOrder())
                    font["glyf"][name] = pen.glyph()
                    font["hmtx"][name] = tuple(round(value * scale) for value in fallback["hmtx"][glyph_name])
                    font.setGlyphOrder([*order, name])
                    for table in font["cmap"].tables:
                        if table.isUnicode():
                            table.cmap[0x2026] = name
            style = styles[weight]
            names = {1: family, 2: style, 3: family + " " + style, 4: family + " " + style,
                     6: stem + "-" + style, 16: family, 17: style}
            for record in font["name"].names:
                if record.nameID in names:
                    record.string = names[record.nameID].encode(record.getEncoding())
            font.save(output / f"{stem}-{weight}.ttf")
            print(f"{stem}-{weight}.ttf", flush=True)
