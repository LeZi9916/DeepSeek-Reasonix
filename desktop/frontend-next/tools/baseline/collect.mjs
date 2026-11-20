// Captures the geometry and computed style of every named element on each page
// domain, so a replacement shell can be held to what this one actually draws.
//
// The output is a JSON baseline, not a screenshot: screenshots carry glyph
// rasterisation and antialiasing, which differ per platform and per engine, and
// a gate on them would fail for reasons the replacement shell cannot fix. What
// the replacement must reproduce is the box, the ink, the type scale and the
// spacing — values both shells can compute.
//
// Run against a production build served on PERF_URL:
//   pnpm exec vite build --config vite.perf.config.ts
//   node tools/baseline/collect.mjs
import { chromium } from "playwright";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, "..", "..", "..", "avalonia", "baseline");
const BASE = process.env.PERF_URL ?? "http://localhost:4399/perf.html";

// The domains are named here rather than discovered: a page that stops being
// captured has to be a deletion someone made, never a selector that quietly
// matched nothing.
//
// Each entry names the address a capture opens and the element that proves the
// page arrived. Both are checked after the capture: a page whose address no
// longer opens, or opens onto something else, fails the run instead of
// recording a smaller baseline under this page's name.
//
// A folded settings section is reachable only through its home page, and most
// of those anchors merely scroll the home page — the same boxes at another
// offset, which would freeze one page twice. Only the two that replace the
// page's content are listed on their own, and each was measured before it was
// written down:
//   - session:persona hides the composition picker and draws the persona block
//   - ext:market draws the market tab instead of the installed list
//
// `root` is where a capture starts. A sheet is captured from `.prefs` down,
// not from `body`, because the conversation behind it is still mounted and
// still streaming: two captures of one settings page differed by 30px on 110
// nodes because the background transcript had moved, and none of those nodes
// belonged to the page under test. The window's own frame (rail, chrome) is
// covered by the conversation domain, which is the page that really draws it.
const DOMAINS = [
  { name: "conversation", screen: "conversation", proofs: [".pane .compose"] },
  { name: "analysis", screen: "analysis", proofs: [".run-analysis"] },
  { name: "workbench", screen: "workbench", proofs: ["section.workbench .workbench-tabs"] },
  { name: "onboarding", screen: "onboarding", proofs: [".onb"] },
  ...["common", "model", "session", "tools", "ext", "display", "controls", "appearance", "storage", "network", "account", "advanced"].map(
    (section) => ({ name: `settings-${section}`, screen: `settings:${section}`, proofs: [`.prefs .prefs-main[data-sec=${section}]`], root: ".prefs" }),
  ),
  { name: "settings-session-persona", screen: "settings:session:persona", proofs: ['.prefs [data-setting="persona"]'], absent: ['.prefs [data-setting="composition-picker"]'], root: ".prefs" },
  { name: "settings-ext-market", screen: "settings:ext:market", proofs: ['.prefs [data-setting="market"]'], root: ".prefs" },
];

const STYLE_PROPS = [
  "display",
  "position",
  "boxSizing",
  "width",
  "height",
  "marginTop",
  "marginRight",
  "marginBottom",
  "marginLeft",
  "paddingTop",
  "paddingRight",
  "paddingBottom",
  "paddingLeft",
  "borderTopWidth",
  "borderRightWidth",
  "borderBottomWidth",
  "borderLeftWidth",
  "borderTopLeftRadius",
  "borderTopRightRadius",
  "borderBottomRightRadius",
  "borderBottomLeftRadius",
  "fontFamily",
  "fontSize",
  "fontWeight",
  "fontStyle",
  "lineHeight",
  "letterSpacing",
  "textAlign",
  "textTransform",
  "whiteSpace",
  "color",
  "backgroundColor",
  "backgroundImage",
  "opacity",
  "boxShadow",
  "overflowX",
  "overflowY",
  "flexDirection",
  "flexWrap",
  "justifyContent",
  "alignItems",
  "gap",
  "rowGap",
  "columnGap",
  "gridTemplateColumns",
  "gridTemplateRows",
  "zIndex",
  "visibility",
  "cursor",
  "transitionProperty",
  "transitionDuration",
  "transitionTimingFunction",
  "animationName",
  "animationDuration",
  "animationTimingFunction",
  "transform",
  "minWidth",
  "maxWidth",
  "minHeight",
  "maxHeight",
  "aspectRatio",
  "objectFit",
  "scrollbarWidth",
];

// A property is dropped when it holds the value an element would have had
// anyway. Recording them all made the baseline 50 MB, of which most restated
// that a div is static, visible and has no shadow; a gate reads what a designer
// changed, and the defaults are one table rather than 1.7 million copies.
const CSS_DEFAULTS = {
  display: "block",
  position: "static",
  boxSizing: "content-box",
  marginTop: "0px",
  marginRight: "0px",
  marginBottom: "0px",
  marginLeft: "0px",
  paddingTop: "0px",
  paddingRight: "0px",
  paddingBottom: "0px",
  paddingLeft: "0px",
  borderTopWidth: "0px",
  borderRightWidth: "0px",
  borderBottomWidth: "0px",
  borderLeftWidth: "0px",
  borderTopLeftRadius: "0px",
  borderTopRightRadius: "0px",
  borderBottomRightRadius: "0px",
  borderBottomLeftRadius: "0px",
  fontStyle: "normal",
  letterSpacing: "normal",
  textAlign: "start",
  textTransform: "none",
  whiteSpace: "normal",
  backgroundColor: "rgba(0, 0, 0, 0)",
  backgroundImage: "none",
  opacity: "1",
  boxShadow: "none",
  overflowX: "visible",
  overflowY: "visible",
  flexDirection: "row",
  flexWrap: "nowrap",
  justifyContent: "normal",
  alignItems: "normal",
  gap: "normal",
  rowGap: "normal",
  columnGap: "normal",
  gridTemplateColumns: "none",
  gridTemplateRows: "none",
  zIndex: "auto",
  visibility: "visible",
  cursor: "auto",
  transitionProperty: "all",
  transitionDuration: "0s",
  transitionTimingFunction: "ease",
  animationName: "none",
  animationDuration: "0s",
  animationTimingFunction: "ease",
  transform: "none",
  minWidth: "auto",
  maxWidth: "none",
  minHeight: "auto",
  maxHeight: "none",
  aspectRatio: "auto",
  objectFit: "fill",
  scrollbarWidth: "auto",
};

// A bounding box is recorded without subpixel noise: layout rounds to the
// device pixel here, and a replacement shell cannot be held to a fractional
// difference that no eye and no CSS author ever named.
const round = (n) => Math.round(n * 100) / 100;

const capture = () =>
  // Everything below runs inside the page, where the layout is real.
  ({ props, defaults, root: rootSelector }) => {
    const rounder = (n) => Math.round(n * 100) / 100;
    // A sheet is captured from its own element down: the conversation behind
    // it is still mounted and still streaming, and its boxes must not enter a
    // baseline that is about the sheet. The shell's frame is captured by the
    // page that really draws it (the conversation), not by every overlay.
    const start = rootSelector ? document.querySelector(rootSelector) : document.body;
    if (!start) throw new Error(`capture root ${rootSelector} not found`);
    const named = (el) => {
      const cls = el.getAttribute("class");
      if (cls && cls.trim().length > 0) return true;
      return el.hasAttribute("data-action") || el.hasAttribute("role");
    };
    const path = (el) => {
      const parts = [];
      let node = el;
      while (node && node !== document.body && parts.length < 6) {
        const parent = node.parentElement;
        if (!parent) break;
        const index = Array.prototype.indexOf.call(parent.children, node);
        parts.unshift(`${node.tagName.toLowerCase()}:${index}`);
        node = parent;
      }
      return parts.join("/");
    };
    // Text is captured for identity, not for comparison of words: a receipt
    // carries today's date, a run carries an elapsed time, and either would make
    // the baseline drift by when it was taken. Dates and clock times become
    // placeholders so two runs on different days produce the same bytes.
    const stableText = (raw) => {
      let text = (raw ?? "").replace(/\s+/g, " ").trim();
      text = text.replace(/\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?Z?)?/g, "<date>");
      text = text.replace(/\b\d{1,2}:\d{2}(?::\d{2})?\b/g, "<time>");
      text = text.replace(/\b\d+(?:\.\d+)?\s*(?:s|ms|秒|分钟|小时|分钟前|小时前|天前)\b/g, "<elapsed>");
      return text.slice(0, 60);
    };
    const nodes = [];
    // An identity is not unique: twenty `.lb` labels share one, nineteen tree
    // rows share one. Keying a comparison on the identity alone would compare
    // the first against the first and silently drop the rest, so each node also
    // carries its occurrence among the nodes sharing its identity, in document
    // order. The key stays free of coordinates, which is what leaves the gate
    // able to judge coordinates.
    const occurrences = new Map();
    const occurrenceOf = (identity) => {
      const at = occurrences.get(identity) ?? 0;
      occurrences.set(identity, at + 1);
      return at;
    };
    // The same computed style repeats across every row, button and cell, so the
    // style table is deduplicated per domain and each node carries its index.
    // Recording it inline made the baseline 20 MB of identical declarations.
    const styleTable = [];
    const styleIndex = new Map();
    const intern = (style) => {
      const key = JSON.stringify(style);
      const found = styleIndex.get(key);
      if (found !== undefined) return found;
      const at = styleTable.length;
      styleTable.push(style);
      styleIndex.set(key, at);
      return at;
    };
    for (const el of start.querySelectorAll("*")) {
      if (!named(el)) continue;
      const rect = el.getBoundingClientRect();
      if (rect.width === 0 && rect.height === 0) continue;
      const cs = getComputedStyle(el);
      const style = {};
      for (const prop of props) {
        const value = cs[prop];
        if (defaults[prop] === value) continue;
        style[prop] = value;
      }
      const action = el.getAttribute("data-action");
      const role = el.getAttribute("role");
      const cls = (el.getAttribute("class") ?? "").trim();
      const identity = action ? `action:${action}` : role ? `role:${role}` : `class:${cls}`;
      nodes.push({
        path: path(el),
        tag: el.tagName.toLowerCase(),
        cls,
        action,
        role,
        o: occurrenceOf(identity),
        text: stableText(el.textContent),
        box: {
          x: rounder(rect.x),
          y: rounder(rect.y),
          w: rounder(rect.width),
          h: rounder(rect.height),
        },
        s: intern(style),
      });
    }
    const root = document.documentElement;
    const rootStyle = getComputedStyle(root);
    // A token's authored form is a recipe, not a value: oklch(), color-mix()
    // and calc() are resolved by the browser at use time, and a shell that
    // stored the recipe would have to implement CSS colour maths to read it.
    // Each token is therefore evaluated here, through the engine that already
    // knows how, and recorded as the value it resolves to.
    //
    // Colour goes through a canvas pixel rather than getComputedStyle: this
    // engine leaves oklch() in the computed value, so reading it back returns
    // the recipe. Painting one pixel and reading its bytes is the only answer
    // that is the colour itself — the same reason perf/contrast.mjs does it.
    const ink = document.createElement("canvas").getContext("2d", { willReadFrequently: true });
    const rgba = (css) => {
      ink.clearRect(0, 0, 1, 1);
      ink.fillStyle = "#000";
      ink.fillStyle = css;
      ink.fillRect(0, 0, 1, 1);
      const [r, g, b, a] = ink.getImageData(0, 0, 1, 1).data;
      return [r, g, b, a / 255];
    };

    const probe = document.createElement("span");
    probe.style.display = "none";
    document.body.appendChild(probe);
    const normalise = (value) => {
      if (!value) return value;

      if (CSS.supports("color", value)) {
        const [r, g, b, a] = rgba(value);
        return a >= 1
          ? `rgb(${r}, ${g}, ${b})`
          : `rgba(${r}, ${g}, ${b}, ${Math.round(a * 1000) / 1000})`;
      }

      if (CSS.supports("width", value)) {
        probe.style.width = "";
        probe.style.width = value;
        const resolved = getComputedStyle(probe).width;
        return resolved || value;
      }

      // A unitless scale such as `calc(1 * 1)` is neither a colour nor a
      // length, and `width` refuses it. Opacity accepts a bare number, so it is
      // the channel that reads these.
      if (CSS.supports("opacity", value)) {
        probe.style.opacity = "";
        probe.style.opacity = value;
        const resolved = getComputedStyle(probe).opacity;
        return resolved || value;
      }

      return value;
    };
    const tokens = Object.fromEntries(
      Array.from(rootStyle)
        .filter((name) => name.startsWith("--"))
        .map((name) => [name, normalise(rootStyle.getPropertyValue(name).trim())])
        .sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0)),
    );
    probe.remove();

    return {
      viewport: { w: window.innerWidth, h: window.innerHeight },
      scheme: rootStyle.colorScheme,
      tokens,
      styles: styleTable,
      nodes,
    };
  };

async function settle(page) {
  await page.waitForLoadState("networkidle");
  await page.evaluate(() => document.fonts.ready);
  // Animations are disabled and the layout is then polled until it stops
  // moving: a transition still running, or a lazily mounted card arriving one
  // frame late, moved every box on the page by 30px between two captures of the
  // same state. Sampling once after a fixed wait cannot tell "settled" from
  // "about to settle".
  await page.addStyleTag({
    content: "*, *::before, *::after { animation: none !important; transition: none !important; caret-color: transparent !important; }",
  });
  await page.evaluate(async () => {
    const snapshot = () =>
      Array.from(document.body.querySelectorAll("*"))
        .map((el) => {
          const r = el.getBoundingClientRect();
          return `${r.x.toFixed(2)},${r.y.toFixed(2)},${r.width.toFixed(2)},${r.height.toFixed(2)}`;
        })
        .join("|");
    let last = snapshot();
    let stableFrames = 0;
    for (let i = 0; i < 90; i += 1) {
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      const now = snapshot();
      if (now === last) {
        stableFrames += 1;
        if (stableFrames >= 3) return;
      } else {
        stableFrames = 0;
        last = now;
      }
    }
  });
  await page.evaluate(() => window.scrollTo(0, 0));
}

async function grab(page, props, root) {
  return page.evaluate(capture(), { props, defaults: CSS_DEFAULTS, root });
}

// A contrast tier moves only the four ink tokens: tokens.css declares
// --text/--muted/--faint/--ghost under each of soft, normal and strong, for both
// schemes. Capturing a whole page per tier would triple the baseline to record
// four values, so the tiers are read on their own.
async function grabContrast(page) {
  return page.evaluate(() => {
    const root = document.documentElement;
    const ink = document.createElement("canvas").getContext("2d", { willReadFrequently: true });
    const rgba = (css) => {
      ink.clearRect(0, 0, 1, 1);
      ink.fillStyle = "#000";
      ink.fillStyle = css;
      ink.fillRect(0, 0, 1, 1);
      const [r, g, b, a] = ink.getImageData(0, 0, 1, 1).data;
      return [r, g, b, a / 255];
    };
    const normalise = (value) => {
      if (!value || !CSS.supports("color", value)) return value;
      const [r, g, b, a] = rgba(value);
      return a >= 1 ? `rgb(${r}, ${g}, ${b})` : `rgba(${r}, ${g}, ${b}, ${Math.round(a * 1000) / 1000})`;
    };

    const INK = ["--text", "--muted", "--faint", "--ghost"];
    const before = root.getAttribute("data-contrast");
    const tiers = {};
    for (const tier of ["soft", "normal", "strong"]) {
      root.setAttribute("data-contrast", tier);
      const style = getComputedStyle(root);
      tiers[tier] = Object.fromEntries(INK.map((name) => [name, normalise(style.getPropertyValue(name).trim())]));
    }
    if (before === null) root.removeAttribute("data-contrast");
    else root.setAttribute("data-contrast", before);
    return tiers;
  });
}

// The page a capture opens, as a query string. The harness parses `screen`
// once and draws that page on its first paint, so the capture measures the
// page rather than whatever a click left behind.
function addressOf(screen) {
  const base = `${BASE}?pref=zh&ws=2&sess=6&turns=2`;
  return screen === "conversation" ? base : `${base}&screen=${encodeURIComponent(screen)}`;
}

// Checks the page a domain names actually arrived. A page that stopped opening
// still photographs something — the previous page, a refused screen, an empty
// shell — and recording that under this domain's name is the failure this
// check exists to turn into a red run.
async function proveArrived(page, domain) {
  const refused = await page.locator("#screen-refused").count();
  if (refused > 0) {
    const why = (await page.locator("#screen-refused").innerText()).trim();
    throw new Error(`refused: ${why}`);
  }

  for (const selector of domain.proofs) {
    if ((await page.locator(selector).count()) === 0) {
      throw new Error(`did not arrive: no ${selector}`);
    }
  }

  // A folded block is only a page of its own when its home page's default
  // block is gone; without this, the capture would freeze the home page twice.
  for (const selector of domain.absent ?? []) {
    if ((await page.locator(selector).count()) > 0) {
      throw new Error(`still showing ${selector}, so this is the home page`);
    }
  }
}

const main = async () => {
  await mkdir(OUT, { recursive: true });
  const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "chrome" });

  const domains = {};
  const contrast = {};
  const failures = [];

  // A fresh context per capture. Two reasons, and both were measured:
  //  - switching colorScheme on a loaded page leaves the previous scheme's
  //    styles resolving for a frame or two;
  //  - localStorage outlives a page inside one context, so the workbench
  //    capture's "recently opened" row reappeared on every settings page
  //    captured after it and added three nodes to each.
  // A context per domain also means a scheme's pages cannot leak into each
  // other through anything else the app remembers.
  for (const scheme of ["light", "dark"]) {
    for (const domain of DOMAINS) {
      const context = await browser.newContext({
        viewport: { width: 1440, height: 900 },
        deviceScaleFactor: 1,
        colorScheme: scheme,
      });

      const page = await context.newPage();
      try {
        await page.goto(addressOf(domain.screen), { waitUntil: "domcontentloaded" });
        await settle(page);
        await proveArrived(page, domain);
        domains[`${domain.name}/${scheme}`] = await grab(page, STYLE_PROPS, domain.root);

        // The contrast tiers are one axis of the token set, not of any page, so
        // they are read once per scheme from the page that is already open.
        if (domain.name === "conversation") contrast[scheme] = await grabContrast(page);
      } catch (error) {
        failures.push(`${domain.name}/${scheme}: ${error instanceof Error ? error.message : String(error)}`);
      } finally {
        await page.close();
        await context.close();
      }
    }
  }

  await browser.close();

  // Nothing is written for a half-read baseline: a directory holding some
  // pages at a new capture and others at an old one compares two trees that
  // never existed together, and every difference it reports is about the
  // capture rather than about the shell.
  if (failures.length > 0) {
    for (const failure of failures) console.error(`failed ${failure}`);
    console.error(`${failures.length} of ${DOMAINS.length * 2} captures failed; nothing written`);
    process.exit(1);
  }

  // One file per domain, written compactly: the baseline is read by a gate, not
  // by a person, and pretty-printing it spent a third of its bytes on
  // indentation. Splitting it keeps a review of one domain's drift down to one
  // file, and keeps the whole baseline out of a single diff nobody can read.
  const pages = new Set();
  let nodes = 0;
  let styles = 0;
  const index = { capturedFrom: "desktop/frontend-next (React)", domains: [] };
  for (const [name, domain] of Object.entries(domains)) {
    const file = `${name.replace(/\//g, ".")}.json`;
    await writeFile(join(OUT, file), JSON.stringify(domain) + "\n", "utf8");
    index.domains.push({ name, file, nodes: domain.nodes.length, styles: domain.styles.length });
    pages.add(name.slice(0, name.lastIndexOf("/")));
    nodes += domain.nodes.length;
    styles += domain.styles.length;
  }

  // The totals ride the index so a reader — and the commit message that says
  // how much was frozen — can state the size without walking thirty-six files.
  // `pages` counts a page once and `domains` counts it per scheme, so the two
  // numbers answer different questions rather than one being a typo.
  index.pages = pages.size;
  index.totals = { domains: index.domains.length, nodes, styles };

  // The contrast tiers are their own file: four ink tokens per tier per scheme,
  // not a page, and folding them into a domain would have said the page changed
  // when only the ink did.
  await writeFile(join(OUT, "contrast.json"), JSON.stringify(contrast) + "\n", "utf8");

  await writeFile(join(OUT, "index.json"), JSON.stringify(index, null, 2) + "\n", "utf8");

  const count = Object.keys(domains).length;
  console.log(`captured ${count} domains over ${index.pages} pages, ${nodes} nodes, ${styles} styles -> ${OUT}`);
};

await main();
