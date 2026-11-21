import { mkdir } from "node:fs/promises";
import { resolve, join } from "node:path";
import { chromium } from "playwright";

const output = resolve(process.env.STUDIO_PARITY_SHOTS ?? "../avalonia/bin/parity/reference");
const url = process.env.PERF_URL ?? "http://localhost:4399/perf.html";
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL === "bundled" ? undefined : process.env.PERF_CHANNEL ?? "chrome" });
await mkdir(output, { recursive: true });

try {
  for (const scene of ["memory-edit", "memory-history", "preset-create", "preset-copy"]) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1, colorScheme: "light", reducedMotion: "reduce" });
    try {
      const memory = scene.startsWith("memory");
      const location = new URL(url);
      location.search = new URLSearchParams({ pref: "zh", ws: "2", sess: "6", turns: "2", screen: memory ? "settings:storage" : "settings:session" });
      await page.goto(location.href);
      const act = (key) => page.locator(`[data-action="${key}"]`).first();
      if (memory) {
        await act("memory.search").fill("no-coauthored-by");
        await act("memory.edit").click();
        if (scene === "memory-history") {
          await page.locator(".memedit textarea").fill("saved through the form");
          await act("memory.save").click();
          await act("memory.history").click();
          await act("memory.restore").waitFor();
        } else {
          await page.locator(".memedit textarea").waitFor();
        }
      } else {
        await act(scene === "preset-create" ? "agent-preset.create" : "agent-preset.copy").click();
        await act("agent-preset.save").waitFor();
      }
      // Start each comparison at the top of its sheet. A browser click can scroll
      // the history trigger past the viewport before the revisions finish mounting.
      await page.locator(".prefs-main").evaluate(panel => { panel.scrollTop = 0; });
      if (scene === "memory-history") {
        await act("memory.restore").evaluate(button => button.scrollIntoView({ block: "nearest", behavior: "instant" }));
      }
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({ path: join(output, scene + ".light.png"), animations: "disabled" });
    } finally {
      await page.close();
    }
  }
} finally {
  await browser.close();
}
