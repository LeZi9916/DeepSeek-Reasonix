import { chromium } from "playwright";
import { mkdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const svg = await readFile(join(here, "../../src/assets/logo-wordmark.svg"), "utf8");
const target = join(here, "../../../avalonia/src/Reasonix.Studio.Shell/Assets/wordmark.png");
await mkdir(dirname(target), { recursive: true });
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ deviceScaleFactor: 4 });
  await page.setContent(svg);
  await page.locator("svg").evaluate(root => {
    const box = root.getBBox();
    root.setAttribute("viewBox", [box.x, box.y, box.width, box.height].join(" "));
    root.style.width = "180px";
    root.style.height = Math.ceil(180 * box.height / box.width) + "px";
  });
  await page.locator("svg").screenshot({ path: target, omitBackground: true });
} finally {
  await browser.close();
}
