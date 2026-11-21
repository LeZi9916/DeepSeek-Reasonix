// Renders the captured tokens into Avalonia resource dictionaries.
//
// The baseline records what the browser resolved each custom property to, so a
// replacement shell reads the value a designer chose rather than the recipe
// (oklch(), calc()) the stylesheet wrote. Rendering them here, once per scheme,
// is what keeps the two shells' vocabularies the same set of names.
//
// Inputs:  desktop/avalonia/baseline/*.json  (captured by
//          desktop/frontend-next/tools/baseline/collect.mjs)
// Outputs: desktop/avalonia/src/Reasonix.Studio.Shell/Themes/Tokens.<scheme>.axaml
//
// Run: node desktop/avalonia/tools/render-tokens.mjs
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const BASELINE = join(HERE, "..", "baseline");
// The dictionary lives beside the control themes that consume it, not in the
// shell: a page borrows the controls and gets their colours with them.
const OUT = join(HERE, "..", "src", "Reasonix.Studio.Controls", "Themes");

const COLOR = /^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)$/;
const PX = /^(-?[\d.]+)px$/;
const NUMBER = /^-?[\d.]+$/;
const TIME_MS = /^([\d.]+)ms$/;
const TIME_S = /^([\d.]+)s$/;

/** Turns `--rail-open` into the resource key `RailOpen`. */
function keyOf(name) {
  return name
    .replace(/^--/, "")
    .split("-")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join("");
}

/** Reads one token value as the Avalonia type that can hold it. */
function resourceOf(name, value) {
  const key = keyOf(name);

  const color = COLOR.exec(value);
  if (color) {
    const [, r, g, b, a] = color;
    const rgb = [r, g, b].map((c) => Number(c).toString(16).padStart(2, "0")).join("");
    const alpha = a === undefined || Number(a) >= 1 ? "" : Math.round(Number(a) * 255).toString(16).padStart(2, "0");
    const hex = ("#" + alpha + rgb).toUpperCase();
    // A colour is emitted twice: as the Color a painting operation takes, and as
    // the SolidColorBrush a Background or BorderBrush property takes. Both are
    // derived from the one captured value so neither can drift from the other,
    // and the shell never has to spell a converter at a call site.
    return [
      { key, element: "Color", value: hex },
      { key: `${key}Brush`, element: "SolidColorBrush", value: null, from: key },
    ];
  }

  const px = PX.exec(value);
  if (px) {
    // A `--r-*` token is a length a Border rounds by, and Border.CornerRadius is
    // an Avalonia.CornerRadius rather than a Double. A dynamic resource hands
    // over the boxed value with no conversion, so a Double here is refused at
    // runtime instead of being coerced — the dictionary has to hold the type the
    // property takes.
    if (name.startsWith("--r-")) {
      return [{ key, element: "CornerRadius", value: px[1] }];
    }
    return [{ key, element: "x:Double", value: px[1] }];
  }

  if (NUMBER.test(value)) {
    return [{ key, element: "x:Double", value }];
  }

  const ms = TIME_MS.exec(value);
  if (ms) {
    return [{ key, element: "sys:TimeSpan", value: `0:0:0.${String(Math.round(Number(ms[1]))).padStart(3, "0")}` }];
  }

  const seconds = TIME_S.exec(value);
  if (seconds) {
    return [{ key, element: "sys:TimeSpan", value: `0:0:${Number(seconds[1]).toFixed(3)}` }];
  }

  // Everything else — a font stack, a shadow list, a transform, an easing
  // curve, an inline SVG — has no Avalonia scalar form. It is carried as text so
  // the name survives into the shell's vocabulary; a control that consumes one
  // parses it where that control lives, rather than here inventing a type.
  return [{ key, element: "x:String", value }];
}

/** Escapes the characters XML would read as markup. */
function xml(value) {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function renderScheme(scheme) {
  const domain = JSON.parse(readFileSync(join(BASELINE, `conversation.${scheme}.json`), "utf8"));
  const entries = Object.entries(domain.tokens).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));

  const lines = [
    `  <!-- ${entries.length} tokens captured from the reference frontend (${scheme}). -->`,
  ];

  for (const [name, value] of entries) {
    const resources = resourceOf(name, value);
    // A token's name starts with `--`, which XML forbids inside a comment —
    // the same characters that open and close one. The name is written without
    // its leading dashes so the mapping stays readable without breaking the file.
    lines.push(`  <!-- token ${name.replace(/^--/, "")} -->`);
    for (const r of resources) {
      if (r.from) {
        // A brush reads its colour from the Color beside it, so the two cannot
        // disagree: the captured value is written once.
        lines.push(`  <${r.element} x:Key="${r.key}" Color="{StaticResource ${r.from}}" />`);
        continue;
      }
      lines.push(`  <${r.element} x:Key="${r.key}">${xml(r.value)}</${r.element}>`);
    }

    // The captured value itself, under the name the baseline uses. Typed
    // resources above are what a control binds to; this is what the geometry
    // gate compares against, and it has to be the same text the browser
    // resolved rather than a round-trip through a Color or a Double.
    lines.push(`  <x:String x:Key="token:${xml(name)}">${xml(value)}</x:String>`);
  }

  return lines;
}

// The contrast tiers are a third axis, and Avalonia varies on Light/Dark only.
// Each tier is therefore its own dictionary of the four ink tokens it moves,
// keyed with the same names the token dictionary uses — so merging one over it
// is an override rather than a second vocabulary.
function renderContrast() {
  const data = JSON.parse(readFileSync(join(BASELINE, "contrast.json"), "utf8"));
  const lines = [
    "<!-- Code generated by desktop/avalonia/tools/render-tokens.mjs. DO NOT EDIT. -->",
    `<ResourceDictionary xmlns="https://github.com/avaloniaui"`,
    `                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">`,
    "  <!-- Each tier is a ResourceDictionary the shell merges over Tokens.axaml. -->",
    `  <ResourceDictionary.ThemeDictionaries>`,
  ];

  for (const scheme of ["light", "dark"]) {
    lines.push(`    <ResourceDictionary x:Key="${scheme === "light" ? "Light" : "Dark"}">`);
    for (const tier of ["soft", "normal", "strong"]) {
      lines.push(`      <ResourceDictionary x:Key="Contrast.${tier}">`);
      for (const [name, value] of Object.entries(data[scheme][tier])) {
        for (const r of resourceOf(name, value)) {
          if (r.from) {
            lines.push(`        <${r.element} x:Key="${r.key}" Color="{StaticResource ${r.from}}" />`);
            continue;
          }
          lines.push(`        <${r.element} x:Key="${r.key}">${xml(r.value)}</${r.element}>`);
        }
      }
      lines.push("      </ResourceDictionary>");
    }
    lines.push("    </ResourceDictionary>");
  }

  lines.push("  </ResourceDictionary.ThemeDictionaries>", "</ResourceDictionary>");
  return lines.join("\r\n") + "\r\n";
}

// One dictionary, two variants: Avalonia picks the variant from the window's
// requested theme, so a page never switches files to change scheme. Emitting two
// documents instead would make every switch a load, and a page that forgot to
// switch would draw yesterday's ink.
function render() {
  const lines = [
    "<!-- Code generated by desktop/avalonia/tools/render-tokens.mjs. DO NOT EDIT. -->",
    `<ResourceDictionary xmlns="https://github.com/avaloniaui"`,
    `                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"`,
    `                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">`,
    `  <ResourceDictionary.ThemeDictionaries>`,
    `    <ResourceDictionary x:Key="Light">`,
    ...renderScheme("light"),
    `    </ResourceDictionary>`,
    `    <ResourceDictionary x:Key="Dark">`,
    ...renderScheme("dark"),
    `    </ResourceDictionary>`,
    `  </ResourceDictionary.ThemeDictionaries>`,
    "</ResourceDictionary>",
  ];
  // .editorconfig states CRLF for *.axaml, and a generated file is one.
  return lines.join("\r\n") + "\r\n";
}

mkdirSync(OUT, { recursive: true });
writeFileSync(join(OUT, "Tokens.axaml"), render(), "utf8");
writeFileSync(join(OUT, "Contrast.axaml"), renderContrast(), "utf8");
console.log("Tokens.axaml");
console.log("Contrast.axaml");
