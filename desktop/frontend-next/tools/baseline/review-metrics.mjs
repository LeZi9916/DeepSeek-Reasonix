import { chromium } from "playwright";
import { readFile, writeFile, mkdir, copyFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const output = join(desktop, "avalonia/reviews/metrics");
const native = join(desktop, "avalonia/bin/parity/native");
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "msedge" });
const results = [];
try {
  await mkdir(output, { recursive: true });
  for (const [width, height] of [[1440, 900], [760, 480]]) {
    for (const scheme of ["light", "dark"]) {
      const context = await browser.newContext({ viewport: { width, height }, colorScheme: scheme, deviceScaleFactor: 1 });
      try {
        const page = await context.newPage();
        await page.goto((process.env.PERF_URL ?? "http://127.0.0.1:4400/perf.html") + "?pref=zh&ws=2&sess=6&turns=2&screen=conversation", { waitUntil: "networkidle" });
        await page.addStyleTag({ content: "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}" });
        await page.evaluate(async () => { await document.fonts.ready; });
        await page.locator(".studio-meter-speed").hover();
        await pair("speed", ".studio-speed-detail");
        await page.locator('[data-action="metrics.details"][data-value="overview"]').click();
        await pair("cache", ".studio-overview-popover");
        await page.locator('[data-action="metrics.details"][data-value="context"]').click();
        await pair("context", ".studio-context-summary");
        const ledger = JSON.parse(await readFile(join(desktop, "avalonia/tests/Reasonix.Studio.Render.Tests/ledger-inputs.json"), "utf8"));
        await page.evaluate(patch => window.__nativeStatus(patch), ledger);
        await page.locator('[data-action="metrics.details"][data-value="overview"]').click();
        await page.waitForFunction(() => document.querySelector(".studio-overview-grid dd")?.textContent === "12");
        await pair("cache-full", ".studio-overview-popover");
        const svg = await page.evaluate(points => window.__nativeSpark(points), [0, 8, 14, 12, 21, 18, 25, 19, 15, 24, 28, 26]);
        const curve = await page.evaluate(async svg => {
          const image = new Image(); image.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg); await image.decode();
          const canvas = document.createElement("canvas"); canvas.width = 44; canvas.height = 13;
          canvas.getContext("2d").drawImage(image, 0, 0);
          return canvas.toDataURL("image/png").split(",")[1];
        }, svg);
        await pair("spark", null, Buffer.from(curve, "base64"));
        await copyFile(join(native, `metrics-running-curve-${width}x${height}.${scheme}.png`), join(output, `running-curve-${width}x${height}-${scheme}.png`));

        async function pair(surface, selector, component) {
          const reference = component ?? await page.locator(selector).screenshot();
          const actual = await readFile(join(native, `metrics-${surface}-${width}x${height}.${scheme}.png`));
          const referenceBox = component ? { x: 0, y: 0, width: 44, height: 13 } : await page.locator(selector).boundingBox();
          const nativeBox = JSON.parse(await readFile(join(native, `metrics-${surface}-${width}x${height}.${scheme}.geometry.json`), "utf8"));
          const left = { width: reference.readUInt32BE(16), height: reference.readUInt32BE(20) };
          const right = { width: actual.readUInt32BE(16), height: actual.readUInt32BE(20) };
          const compare = await context.newPage();
          const raster = component ? await compare.evaluate(async ({ reference, native }) => {
            async function mask(encoded) {
              const image = new Image(); image.src = "data:image/png;base64," + encoded; await image.decode();
              const canvas = document.createElement("canvas"); canvas.width = 44; canvas.height = 13;
              const context = canvas.getContext("2d"); context.drawImage(image, 0, 0);
              const bytes = context.getImageData(0, 0, 44, 13).data;
              return Array.from({ length: 44 * 13 }, (_, index) => bytes[index * 4 + 3] > 127);
            }
            const [left, right] = await Promise.all([mask(reference), mask(native)]);
            function outside(source, target) {
              return source.reduce((count, ink, index) => {
                if (!ink) return count;
                const x = index % 44, y = Math.floor(index / 44);
                for (let row = Math.max(0, y - 1); row <= Math.min(12, y + 1); row++) for (let column = Math.max(0, x - 1); column <= Math.min(43, x + 1); column++) if (target[row * 44 + column]) return count;
                return count + 1;
              }, 0);
            }
            return { tolerancePixels: 1, unmatchedReferencePixels: outside(left, right), unmatchedNativePixels: outside(right, left) };
          }, { reference: reference.toString("base64"), native: actual.toString("base64") }) : undefined;
          await compare.setViewportSize({ width: component ? 252 : left.width + right.width + 12, height: Math.max(left.height, right.height) + 36 });
          await compare.setContent(`<!doctype html><style>:root{color-scheme:${scheme}}body{margin:0;display:flex;gap:12px;font:12px system-ui}figure{margin:0;${component ? "width:120px" : ""}}figcaption{height:36px;padding:9px;box-sizing:border-box}img{display:block;${component ? "margin:auto" : ""}}</style>${[reference, actual].map((image, index) => `<figure><figcaption>${["Electron", "Avalonia"][index]} · ${surface}${component ? "" : ` · ${width} × ${height}`}</figcaption><img src="data:image/png;base64,${image.toString("base64")}"></figure>`).join("")}`);
          await compare.locator("img").evaluateAll(images => Promise.all(images.map(image => image.decode())));
          const file = `${surface}-${width}x${height}-${scheme}.png`;
          await compare.screenshot({ path: join(output, file) });
          await compare.close();
          results.push({ file, width, height, scheme, surface, scope: component ? "reference component fixture" : "workspace floating frame", reference: left, native: right, referenceBox, nativeBox, geometryDelta: Object.fromEntries((component ? ["width", "height"] : ["x", "y", "width", "height"]).map(key => [key, nativeBox[key] - referenceBox[key]])), raster });
        }
      } finally { await context.close(); }
    }
  }
  await writeFile(join(output, "report.json"), JSON.stringify({ toleranceDip: 0.5, textRasterTolerancePixels: 1, results }, null, 2) + "\n");
  const failed = results.filter(result => Object.values(result.geometryDelta).some(delta => Math.abs(delta) > 0.5));
  if (failed.length) throw new Error("Metric frame geometry exceeded 0.5 DIP: " + failed.map(result => result.file).join(", "));
  const curves = results.filter(result => result.raster && (result.raster.unmatchedReferencePixels || result.raster.unmatchedNativePixels));
  if (curves.length) throw new Error("Curve raster geometry exceeded one pixel: " + curves.map(result => result.file).join(", "));
  console.log(results.map(result => `${result.file}: ${result.reference.width}×${result.reference.height} / ${result.native.width}×${result.native.height}`).join("\n"));
} finally { await browser.close(); }
