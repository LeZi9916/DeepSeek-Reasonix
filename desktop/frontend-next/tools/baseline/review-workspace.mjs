import { chromium } from "playwright";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const out = join(desktop, "avalonia/reviews/workspace");
const native = join(desktop, "avalonia/bin/parity/native");
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "msedge" });
const results = [];
try {
  await mkdir(out, { recursive: true });
  for (const [width, height] of [[1440, 900], [760, 480]]) {
    for (const scheme of ["light", "dark"]) {
      const context = await browser.newContext({ viewport: { width, height }, colorScheme: scheme, deviceScaleFactor: 1 });
      try {
        const page = await context.newPage();
        await page.goto((process.env.PERF_URL ?? "http://127.0.0.1:4400/perf.html") + "?pref=zh&ws=2&sess=6&turns=2", { waitUntil: "networkidle" });
        await page.addStyleTag({ content: "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}" });
        await page.evaluate(async () => { await document.fonts.ready; });
        if (width === 1440 && scheme === "light") {
          const reads = await page.evaluate(() => window.__nativeReference());
          const inputsPath = join(desktop, "avalonia/tests/Reasonix.Studio.Render.Tests/page-inputs.json");
          const inputs = JSON.parse(await readFile(inputsPath, "utf8"));
          inputs["/share"] = reads["/share"];
          inputs["/tree?order=recent"] = reads["/tree?order=recent"];
          await writeFile(inputsPath, JSON.stringify(inputs, null, 2) + "\n");
          if (process.env.REFERENCE_ONLY === "1") { console.log("Exported actual /share and recent-tree fixtures"); process.exitCode = 0; break; }
        }
        await page.keyboard.press("Control+k");
        await page.locator(".palette").waitFor();
        await pair("palette", await page.screenshot());
        await page.keyboard.press("Escape");
        if (width < 1200) await page.locator('.studio-menu[data-action="chrome.rail"]').click();
        await page.locator('.rail .sessrow[data-action-contextmenu="session.menu"]').first().focus();
        await page.keyboard.press("Shift+F10");
        await pair("session-menu", await page.locator(".session-pop").screenshot());
        await page.keyboard.press("Escape");
        await page.locator('[data-action="session.recent"]').click();
        await page.locator(".recent-session").first().waitFor();
        await pair("recent", await page.screenshot());
        await page.locator('.recent-sessions [data-action="layer.dismiss"]').click();
        if (width < 1200) await page.locator('.studio-collapse[data-action="chrome.rail"]').click();
        const device = page.locator('[data-action="share.card"]');
        if (!await device.isVisible()) await page.locator('[data-action="chrome.more"]').click();
        await device.click();
        await pair("device-access", await page.locator(".phonecard").screenshot());
        await page.keyboard.press("Escape");
        await page.keyboard.press("Control+k");
        await page.locator(".palette input").fill(">");
        await page.locator(".palette input").fill("community discord");
        await page.locator('.palette-hit[data-target="c:community"]').waitFor();
        await page.keyboard.press("Enter");
        await page.locator(".comm").waitFor();
        await pair("community", await page.locator(".comm").screenshot());

        async function pair(surface, reference) {
          const actual = await readFile(join(native, `workspace-${surface}-${width}x${height}.${scheme}.png`));
          const leftWidth = reference.readUInt32BE(16), leftHeight = reference.readUInt32BE(20);
          const rightWidth = actual.readUInt32BE(16), rightHeight = actual.readUInt32BE(20);
          const compare = await context.newPage();
          await compare.setViewportSize({ width: leftWidth + 12 + rightWidth, height: Math.max(leftHeight, rightHeight) + 36 });
          await compare.setContent(`<!doctype html><meta charset="utf-8"><style>:root{color-scheme:${scheme}}body{margin:0;display:flex;gap:12px;font:12px system-ui}figure{margin:0}figcaption{height:36px;padding:9px 8px;box-sizing:border-box}img{display:block}</style>${[reference, actual].map((image, i) => `<figure><figcaption>${["Electron", "Avalonia"][i]} · ${surface} · ${width} × ${height} · ${scheme}</figcaption><img src="data:image/png;base64,${image.toString("base64")}"></figure>`).join("")}`);
          await compare.locator("img").evaluateAll(images => Promise.all(images.map(image => image.decode())));
          const file = `${width}x${height}-${scheme}-${surface}.png`;
          await compare.screenshot({ path: join(out, file) });
          results.push({ file, surface, viewport: { width, height }, scheme, reference: { width: leftWidth, height: leftHeight }, observed: { width: rightWidth, height: rightHeight } });
          await compare.close();
        }
      } finally { await context.close(); }
    }
    if (process.env.REFERENCE_ONLY === "1") break;
  }
  if (process.env.REFERENCE_ONLY !== "1") await writeFile(join(out, "report.json"), JSON.stringify({ page: "workspace", results }, null, 2) + "\n");
  console.log(results.map(result => result.file).join("\n"));
} finally { await browser.close(); }
