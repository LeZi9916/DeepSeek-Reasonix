// Captures the Electron workbench file bar for local comparison.
import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = process.env.MENU_SHOTS ?? join(HERE, "..", "..", "..", "avalonia", "bin", "parity", "reference-menus");
const BASE = process.env.PERF_URL ?? "http://127.0.0.1:4399/perf.html";
await mkdir(OUT, { recursive: true });

async function capture(browser, scheme) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1, colorScheme: scheme });
  await page.goto(`${BASE}?screen=workbench`, { waitUntil: "networkidle" });
  const file = page.locator('[data-action-click="workbench.file"]').first();
  await file.waitFor();
  await file.click();
  const bar = page.locator(".workbench-filebar");
  await bar.waitFor();
  const box = await bar.boundingBox();
  if (!box) throw new Error("file bar has no box");
  await page.screenshot({
    path: join(OUT, `electron-filebar-${scheme}.png`),
    clip: { x: Math.max(0, box.x - 12), y: Math.max(0, box.y - 12), width: box.width + 24, height: box.height + 24 },
  });
  await page.close();
}

const browser = await chromium.launch({ channel: "msedge", headless: true });
try {
  await capture(browser, "dark");
  await capture(browser, "light");
} finally {
  await browser.close();
}
console.log("captured", OUT);
