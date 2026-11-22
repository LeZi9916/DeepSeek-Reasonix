import { chromium } from "playwright";
import { writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const output = resolve(here, "../../../avalonia/tests/Reasonix.Studio.Render.Tests/composer-elements.json");
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "chromium" });
const cases = [];
try {
  for (const [width, height] of [[1440, 900], [760, 480]]) {
    for (const theme of ["light", "dark"]) {
      const context = await browser.newContext({ viewport: { width, height }, colorScheme: theme });
      try {
        const page = await context.newPage();
        const url = new URL(process.env.PERF_URL ?? "http://127.0.0.1:4400/perf.html");
        url.search = new URLSearchParams({ pref: "zh", ws: "2", sess: "6", turns: "2", screen: "conversation" });
        await page.goto(url.href, { waitUntil: "networkidle" });
        await page.addStyleTag({ content: "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}" });
        await page.locator(".compose textarea").fill("对齐聊天区的字体、颜色与消息间距。");
        await page.evaluate(async () => { await document.fonts.ready; await new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))); });
        let elements = await page.evaluate(() => {
          const root = document.querySelector(".compose").getBoundingClientRect();
          const canvas = document.createElement("canvas").getContext("2d");
          const selectors = {
            ChangedFilesLabel: ".changedpill > span", ChangedAdded: ".changedpill i[data-io=up]", ChangedRemoved: ".changedpill i[data-io=down]",
            SpeedText: ".studio-meter-speed > b", SpeedUnit: ".studio-meter-speed > span", CacheLabel: ".studio-meter-cache > span", CacheText: ".studio-meter-cache > b",
            ContextLabel: ".studio-meter-context > span:not(.studio-context-ring)", MeterText: ".studio-meter-context > b", WalletText: ".studio-meter-wallet > b",
            PlanModeLabel: ".studio-mode-picker > span:not(.studio-sr-label)", ComposeBranchLabel: ".worktree-trigger .lb", ApprovalModeLabel: ".policy .mode .lb", ModelLabel: ".model-picker .nm",
            ContextRing: ".studio-context-ring",
          };
          return Object.fromEntries(Object.entries(selectors).map(([name, selector]) => {
            const node = document.querySelector(selector), box = node.getBoundingClientRect();
            const base = getComputedStyle(node), style = parseFloat(base.fontSize) === 0 ? getComputedStyle(node, "::after") : base;
            canvas.clearRect(0, 0, 1, 1); canvas.fillStyle = style.color; canvas.fillRect(0, 0, 1, 1);
            const color = "#" + [...canvas.getImageData(0, 0, 1, 1).data].slice(0, 3).map(n => n.toString(16).padStart(2, "0")).join("");
            return [name, { box: [box.x - root.x, box.y - root.y, box.width, box.height], fontSize: parseFloat(style.fontSize), fontWeight: parseInt(style.fontWeight), lineHeight: parseFloat(style.lineHeight) || box.height, color }];
          }));
        });
        const screenshot = await page.screenshot();
        elements = await page.evaluate(async ({ elements, png }) => {
          const image = new Image(); image.src = `data:image/png;base64,${png}`; await image.decode();
          const canvas = document.createElement("canvas"); canvas.width = image.width; canvas.height = image.height;
          const context = canvas.getContext("2d", { willReadFrequently: true }); context.drawImage(image, 0, 0);
          const origin = document.querySelector(".compose").getBoundingClientRect();
          const rgb = value => [1, 3, 5].map(at => parseInt(value.slice(at, at + 2), 16));
          const background = rgb(document.documentElement.dataset.theme === "dark" ? "#181918" : "#fafaf8");
          for (const [name, element] of Object.entries(elements)) {
            if (name === "ContextRing") continue;
            const [x, y, width, height] = element.box, ink = rgb(element.color);
            const clip = [Math.floor(origin.x + x), Math.floor(origin.y + y - 4), Math.ceil(width), Math.ceil(height + 9)];
            const pixels = context.getImageData(...clip).data, rows = [];
            for (let row = 0; row < clip[3]; row++) {
              for (let column = 0; column < clip[2]; column++) {
                const pixel = (row * clip[2] + column) * 4;
                const toInk = ink.reduce((sum, value, channel) => sum + (pixels[pixel + channel] - value) ** 2, 0);
                const toBackground = background.reduce((sum, value, channel) => sum + (pixels[pixel + channel] - value) ** 2, 0);
                if (toInk < toBackground) { rows.push(row + clip[1] - origin.y); break; }
              }
            }
            element.inkRows = [Math.min(...rows), Math.max(...rows)];
          }
          return elements;
        }, { elements, png: screenshot.toString("base64") });
        cases.push({ width, height, theme, elements });
      } finally { await context.close(); }
    }
  }
  await writeFile(output, JSON.stringify(cases, null, 2) + "\n");
  console.log(`Captured ${cases.length} composer element fixtures.`);
} finally { await browser.close(); }
