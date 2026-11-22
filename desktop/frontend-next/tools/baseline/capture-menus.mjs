// Captures the Electron reference's composer menus for local comparison.
// Requires the perf build served on :4399 and Playwright's installed Edge channel.
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
  await page.waitForSelector(".pane .compose");
  await page.screenshot({ path: join(OUT, `electron-conversation-${scheme}.png`) });

  await page.locator(".studio-mode-picker").click();
  await page.waitForSelector(".composer-menu.mode-menu");
  const mode = page.locator(".composer-menu.mode-menu");
  const modeBox = await mode.boundingBox();
  if (!modeBox) throw new Error("mode menu has no box");
  await page.screenshot({
    path: join(OUT, `electron-mode-${scheme}.png`),
    clip: { x: Math.max(0, modeBox.x - 28), y: Math.max(0, modeBox.y - 28), width: modeBox.width + 56, height: modeBox.height + 56 },
  });
  await page.keyboard.press("Escape");

  await page.locator(".policy > .mode").click();
  await page.waitForSelector(".composer-menu.polmenu");
  const policy = page.locator(".composer-menu.polmenu");
  const policyBox = await policy.boundingBox();
  if (!policyBox) throw new Error("policy menu has no box");
  await page.screenshot({
    path: join(OUT, `electron-policy-${scheme}.png`),
    clip: { x: Math.max(0, policyBox.x - 28), y: Math.max(0, policyBox.y - 28), width: policyBox.width + 56, height: policyBox.height + 56 },
  });
  await page.keyboard.press("Escape");

  await page.locator(".studio-branch").click();
  await page.waitForSelector(".workspace-palette");
  const branch = page.locator(".workspace-palette");
  const branchBox = await branch.boundingBox();
  if (!branchBox) throw new Error("branch palette has no box");
  await page.screenshot({
    path: join(OUT, `electron-branch-${scheme}.png`),
    clip: { x: Math.max(0, branchBox.x - 28), y: Math.max(0, branchBox.y - 28), width: branchBox.width + 56, height: branchBox.height + 56 },
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
