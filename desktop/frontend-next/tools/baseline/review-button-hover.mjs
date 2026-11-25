import { chromium } from "playwright";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const tabs = process.argv.includes("--tabs");
const kind = tabs ? "tab-hover" : "button-hover";
const output = process.env.STUDIO_MOTION_OUTPUT ?? fileURLToPath(new URL(`../../../avalonia/bin/parity/${kind}/`, import.meta.url));
const review = fileURLToPath(new URL(`../../../avalonia/reviews/${kind}/`, import.meta.url));
const base = process.env.PERF_URL ?? "http://127.0.0.1:4399/perf.html";
const viewport = { width: 1440, height: 900 };
const phases = [["rest", "静止"], ["enter", "移入约 35 ms"], ["hover", "悬浮稳定"], ["exit", "移开约 35 ms"], ["restored", "恢复静止"]];
const cases = tabs ? [
  ["session-tab", "未选中会话标签", '.ptab[aria-selected="false"]'],
  ["session-current", "当前会话标签", '.ptab[aria-selected="true"]'],
  ["rail-tab", "侧栏最近会话", '.rail [data-action="session.recent"]'],
  ["file-tab", "工作台文件标签", ".workbench-tab", "workbench"],
  ["browser-current", "当前浏览器标签", '.workbench-tab[aria-selected="true"]', "workbench"],
  ["browser-tab", "未选中浏览器标签", '.workbench-tab[aria-selected="false"]', "workbench-two-browsers"],
  ["settings-tab", "设置导航标签", '.prefs-nav button[aria-selected="false"]:not(:disabled)', "settings:ext"],
  ["extension-tab", "扩展发现标签", '.mkt-tabs button[aria-selected="false"]', "settings:ext"],
] : [
  ["chrome", "工作台入口", '.chrome [data-action="pane.view"][data-value="browser"]'],
  ["settings", "侧栏设置", ".studio-settings"],
  ["attach", "添加附件", ".compose .turntools > .studio-attach:first-child"],
  ["side", "辅助会话", '.compose [data-action="side.toggle"]'],
  ["refine", "优化提示词", '.compose [data-action="prompt.refine"]'],
  ["mode", "模式胶囊", ".compose .studio-mode-picker"],
  ["context", "上下文指标", ".studio-meter-context"],
  ["send", "发送", ".compose .btn.send"],
  ["stop", "停止", ".compose .btn.stop", "running"],
  ["file-tab", "工作台文件标签", ".workbench-tab", "workbench"],
  ["dismiss", "关闭工作台", ".workbench-dismiss", "workbench"],
];

await mkdir(output, { recursive: true });
await mkdir(review, { recursive: true });
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "chromium" });
try {
  for (const scheme of ["light", "dark"]) {
    const context = await browser.newContext({ viewport, colorScheme: scheme, deviceScaleFactor: 1 });
    const report = [];
    if (tabs) await context.addInitScript(() => localStorage.setItem("rx-show-session-tabs", "on"));
    for (const scene of new Set(cases.map(item => item[3] ?? "conversation"))) {
      const page = await context.newPage();
      const url = new URL(base);
      url.search = new URLSearchParams({ pref: "zh", ws: "2", sess: "6", turns: "2", screen: scene.startsWith("workbench") ? "workbench" : scene === "running" ? "conversation" : scene });
      if (tabs && scene === "conversation") url.searchParams.set("panes", "2");
      if (scene === "running") url.searchParams.set("running", "");
      await page.goto(url.href, { waitUntil: "networkidle" });
      await page.evaluate(() => document.fonts.ready);
      await page.addStyleTag({ content: "* { caret-color: transparent !important; }" });
      if (!tabs && scene === "conversation") {
        await page.locator(".compose textarea").fill("Hover");
        await page.locator(".compose textarea").blur();
      }
      if (scene.startsWith("workbench")) {
        await page.locator('[data-action-click="workbench.file"]').first().click();
        await page.locator('[data-action="workbench.new"]').click();
        await page.locator('[data-action="workbench.launch-browser"]').click();
        if (scene === "workbench-two-browsers") {
          await page.locator('[data-action="workbench.new"]').click();
          await page.locator('[data-action="workbench.launch-browser"]').click();
        }
      }
      for (const [stem, label, selector] of cases.filter(item => (item[3] ?? "conversation") === scene)) {
        let target = page.locator(selector);
        if (stem === "file-tab") target = target.filter({ hasText: "README.md" });
        if (stem === "browser-tab") target = target.filter({ hasText: "浏览器" });
        target = target.first();
        await target.waitFor();
        await page.mouse.move(700, 80);
        await page.waitForTimeout(150);
        const box = await target.boundingBox();
        const clip = { x: Math.floor(box.x) - 4, y: Math.floor(box.y) - 4, width: Math.ceil(box.width) + 8, height: Math.ceil(box.height) + 8 };
        const frames = {};
        await capture("rest");
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await capture("enter", 35);
        await capture("hover", "finish");
        await page.mouse.move(700, 80);
        await capture("exit", 35);
        await capture("restored", "finish");
        report.push({ stem, label, frames });

        async function capture(phase, time) {
          frames[phase] = await target.evaluate((node, { time, hover }) => {
            const style = getComputedStyle(node);
            // Reading style starts transitions for the current pointer state.
            const transition = style.transition;
            if (time !== "finish" && node.matches(":hover") !== hover) throw new Error("Unexpected pointer state");
            for (const animation of node.getAnimations({ subtree: true }).filter(animation => animation instanceof CSSTransition)) {
              if (time === "finish") animation.finish();
              else if (typeof time === "number") {
                animation.pause();
                animation.currentTime = time;
              }
            }
            return { background: style.backgroundColor, color: style.color, filter: style.filter, transition };
          }, { time, hover: phase === "enter" || phase === "hover" });
          await page.screenshot({ path: join(output, `electron-button-${stem}-${scheme}-${phase}.png`), clip });
        }
      }
      await page.close();
    }
    await writeFile(join(output, `${scheme}.json`), JSON.stringify(report, null, 2) + "\n");
    await compare(context, scheme);
    await context.close();
  }
} finally {
  await browser.close();
}

async function compare(context, scheme) {
  const page = await context.newPage();
  const rows = await Promise.all(cases.map(async ([stem, label]) => {
    const pair = await Promise.all([["electron", "Electron 前端"], ["native", "Avalonia"]].map(async ([engine, title]) => {
      const cells = await Promise.all(phases.map(async ([phase, frame]) => {
        const png = await readFile(join(output, `${engine}-button-${stem}-${scheme}-${phase}.png`));
        return `<td><img alt="${label} · ${title} · ${frame}" src="data:image/png;base64,${png.toString("base64")}"></td>`;
      }));
      return `<tr><th scope="row">${title}</th>${cells.join("")}</tr>`;
    }));
    return `<tbody><tr><th colspan="6">${label}</th></tr>${pair.join("")}</tbody>`;
  }));
  await page.setViewportSize({ width: 1040, height: tabs ? 900 : 1260 });
  await page.setContent(`<!doctype html><html lang="zh-CN"><meta charset="utf-8">
    <style>
      :root { color-scheme: ${scheme}; font: 14px system-ui; }
      body { margin: 0; padding: 20px 24px; color: CanvasText; background: Canvas; }
      h1 { margin: 0 0 4px; font-size: 22px; }
      p { margin: 0 0 16px; }
      table { width: 100%; border-collapse: collapse; table-layout: fixed; }
      th, td { padding: 2px 5px; text-align: center; }
      thead th { padding-bottom: 7px; }
      tbody tr:first-child th { border-top: 1px solid color-mix(in srgb, CanvasText 22%, transparent); text-align: left; }
      tbody th[scope=row] { text-align: left; font-weight: 400; }
      img { display: block; max-width: 100%; height: auto; margin: auto; }
    </style>
    <h1>${tabs ? "选项卡" : "按钮"}悬浮与移开 · ${scheme === "dark" ? "深色" : "浅色"}</h1>
    <p>1440 × 900 · 1× · Electron 前端由 Chromium 渲染，Avalonia 由 Skia 渲染</p>
    <table><col style="width: 120px"><thead><tr><th>前端</th>${phases.map(([, label]) => `<th scope="col">${label}</th>`).join("")}</tr></thead>
    ${rows.join("")}</table></html>`);
  await page.locator("img").evaluateAll(images => Promise.all(images.map(image => image.decode())));
  const path = join(review, `${scheme}.png`);
  await page.screenshot({ path, fullPage: true });
  console.log(path);
  await page.close();
}
