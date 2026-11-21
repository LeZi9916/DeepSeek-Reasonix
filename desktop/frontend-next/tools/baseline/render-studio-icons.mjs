import { writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { pathToFileURL, fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const root = dirname(require.resolve("lucide-react/package.json"));
const icons = {
  common:"settings", model:"brain", session:"message-square", tools:"shield", ext:"plug", display:"monitor",
  controls:"keyboard", appearance:"palette", storage:"hard-drive", network:"globe", account:"user-round",
  advanced:"sliders-horizontal", back:"arrow-left", search:"search", folder:"folder", plus:"plus",
  all:"layout-grid", fresh:"sparkle", constraint:"lock", feedback:"message-square-text", reference:"link",
  galaxy:"orbit", faded:"circle-dashed", edit:"pencil", archive:"archive", check:"check",
  collapse:"panel-left-close", expand:"panel-left-open", agent:"bot",
};

function pathOf([tag, a]) {
  if (tag === "path") return a.d;
  if (tag === "line") return `M${a.x1} ${a.y1}L${a.x2} ${a.y2}`;
  if (tag === "circle") {
    const x = Number(a.cx), y = Number(a.cy), r = Number(a.r);
    return `M${x-r} ${y}a${r} ${r} 0 1 0 ${2*r} 0a${r} ${r} 0 1 0 ${-2*r} 0Z`;
  }
  if (tag === "rect") {
    const x=Number(a.x), y=Number(a.y), w=Number(a.width), h=Number(a.height), r=Number(a.rx??0);
    if (!r) return `M${x} ${y}h${w}v${h}h${-w}Z`;
    return `M${x+r} ${y}h${w-2*r}a${r} ${r} 0 0 1 ${r} ${r}v${h-2*r}a${r} ${r} 0 0 1 ${-r} ${r}h${2*r-w}a${r} ${r} 0 0 1 ${-r} ${-r}v${2*r-h}a${r} ${r} 0 0 1 ${r} ${-r}Z`;
  }
  throw new Error("Unknown Lucide element " + tag);
}
const lines = ['<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'];
for (const [key,name] of Object.entries(icons)) {
  const { __iconData } = await import(pathToFileURL(resolve(root, "dist/esm/icons", name+".mjs")));
  lines.push(`  <StreamGeometry x:Key="Glyph.${key}">${__iconData.node.map(pathOf).join(" ")}</StreamGeometry>`);
}
lines.push("</ResourceDictionary>","");
await writeFile(resolve(here,"../../../avalonia/src/Reasonix.Studio.Controls/Themes/StudioIcons.axaml"),lines.join("\r\n"));
