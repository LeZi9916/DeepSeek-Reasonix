import { chromium } from "playwright";
import { readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktop = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const svg = await readFile(resolve(desktop, "frontend-next/src/assets/qq-group-qr.svg"), "utf8");
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "msedge" });
try {
  const page = await browser.newPage();
  const png = await page.evaluate(async (source) => {
    const image = new Image();
    image.src = `data:image/svg+xml;base64,${btoa(unescape(encodeURIComponent(source)))}`;
    await image.decode();
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 296;
    canvas.getContext("2d").drawImage(image, 0, 0, 296, 296);
    return canvas.toDataURL("image/png").split(",")[1];
  }, svg);
  await writeFile(resolve(desktop, "avalonia/src/Reasonix.Studio.Shell/Assets/Community/qq-group-qr.png"), Buffer.from(png, "base64"));
} finally {
  await browser.close();
}
