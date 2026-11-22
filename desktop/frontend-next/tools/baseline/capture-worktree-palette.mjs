// Captures the Electron worktree palette in both modes for style comparison.
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
  await page.goto(`${BASE}?screen=conversation`, { waitUntil: "networkidle" });
  const trigger = page.locator('[data-action="workspace.configure"]').last();
  await trigger.click();
  const palette = page.locator(".workspace-palette");
  await palette.waitFor();
  let box = await palette.boundingBox();
  await page.screenshot({ path: join(OUT, `electron-worktree-current-${scheme}.png`), clip: { x: box.x - 12, y: box.y - 12, width: box.width + 24, height: box.height + 24 } });
  const isolated = page.locator('[data-action="workspace.worktree-toggle"]').last();
  if (await isolated.count()) {
    await isolated.click();
    await page.waitForTimeout(250);
    box = await palette.boundingBox();
    await page.screenshot({ path: join(OUT, `electron-worktree-isolated-${scheme}.png`), clip: { x: box.x - 12, y: box.y - 12, width: box.width + 24, height: box.height + 24 } });
  }

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
