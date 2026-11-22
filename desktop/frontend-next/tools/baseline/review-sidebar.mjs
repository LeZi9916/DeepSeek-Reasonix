import { chromium } from "playwright";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const root = join(desktop, "avalonia/bin/parity");
const out = join(desktop, "avalonia/reviews/sidebar");
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "msedge" });
const results = [];
try {
  await mkdir(out, { recursive: true });
  for (const [width, height] of [[1440, 900], [760, 480]]) {
    for (const scheme of ["light", "dark"]) {
      const context = await browser.newContext({ viewport: { width, height }, colorScheme: scheme, deviceScaleFactor: 1 });
      try {
        const page = await context.newPage();
        await page.goto((process.env.PERF_URL ?? "http://localhost:4399/perf.html") + "?pref=zh&ws=2&sess=6&turns=2", { waitUntil: "networkidle" });
        const rail = page.locator(".rail");
        if (width < 1200) await page.locator('.studio-menu[data-action="chrome.rail"]').click();
        await page.addStyleTag({ content: "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}" });
        await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
        const reference = await rail.screenshot();
        const native = await readFile(join(root, "native", `sidebar-${width}x${height}.${scheme}.png`));
        if (reference.readUInt32BE(16) !== 264 || native.readUInt32BE(16) !== 264 || native.readUInt32BE(20) !== height) throw new Error("Unexpected sidebar dimensions");
        const rows = await page.locator('.rail .wsnode:not(.rmtws-node) .ds-proj,.rail .wsnode:not(.rmtws-node) .ds-row').evaluateAll(nodes => nodes.map(node => {
          const style = getComputedStyle(node), box = node.getBoundingClientRect();
          return { role: node.classList.contains("ds-proj") ? "project" : "session", height: box.height, fontSize: style.fontSize, fontWeight: style.fontWeight, color: style.color };
        }));
        const compare = await context.newPage();
        await compare.setViewportSize({ width: 540, height: height + 36 });
        const images = [reference, native].map((image, i) => `<figure><figcaption>${["Electron", "Avalonia"][i]} · ${width} × ${height} · ${scheme}</figcaption><img src="data:image/png;base64,${image.toString("base64")}"></figure>`).join("");
        await compare.setContent(`<!doctype html><meta charset="utf-8"><style>:root{color-scheme:${scheme}}body{margin:0;display:flex;gap:12px;font:12px system-ui}figure{margin:0;width:264px}figcaption{height:36px;box-sizing:border-box;padding:9px 8px}img{display:block}</style>${images}`);
        await compare.locator("img").evaluateAll(images => Promise.all(images.map(image => image.decode())));
        const file = `${width}x${height}-${scheme}.png`;
        await compare.screenshot({ path: join(out, file) });
        results.push({ file, viewport: { width, height }, scheme, referenceLocalRows: rows, scope: "Local and remote projects, session ages, machine identities, connection progress, shared navigation and footer." });
        await page.locator(".rail .scroll").evaluate(node => { node.scrollTop = node.scrollHeight; });
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
        const remoteReference = await rail.screenshot();
        const remoteNative = await readFile(join(root, "native", `sidebar-remote-${width}x${height}.${scheme}.png`));
        await compare.locator("img").evaluateAll((images, data) => {
          images.forEach((image, index) => { image.src = `data:image/png;base64,${data[index]}`; });
          return Promise.all(images.map(image => image.decode()));
        }, [remoteReference.toString("base64"), remoteNative.toString("base64")]);
        const remoteFile = `${width}x${height}-${scheme}-remote.png`;
        await compare.screenshot({ path: join(out, remoteFile) });
        results.push({ file: remoteFile, viewport: { width, height }, scheme, scope: "The same sidebar scrolled to its remote project and connection rows." });
        await compare.close();
      } finally { await context.close(); }
    }
  }
  await writeFile(join(out, "report.json"), JSON.stringify({ page: "sidebar", results }, null, 2) + "\n");
  console.log(results.map(result => result.file).join("\n"));
} finally { await browser.close(); }
