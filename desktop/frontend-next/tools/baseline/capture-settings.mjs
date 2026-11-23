import { chromium } from "playwright";
const browser = await chromium.launch({ channel: "msedge", headless: true });
for (const scheme of ["dark", "light"]) {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, colorScheme: scheme });
  await page.goto("http://127.0.0.1:4399/perf.html?screen=settings:model", { waitUntil: "networkidle" });
  await page.waitForTimeout(500);
  await page.screenshot({ path: `C:/Users/LeZi/source/repos/LeZi9916/DeepSeek-Reasonix/desktop/avalonia/bin/parity/reference-menus/electron-settings-model-${scheme}.png` });
  await page.close();
}
await browser.close();
