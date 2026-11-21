// Rebuild a review from the actual reference screen and the native render produced
// by NativeDomainTests. The committed pair retains every pixel at its original size.
import { chromium } from "playwright";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const desktop = resolve(here, "../../..");
const pageName = process.argv[2];
const screens = { "settings-storage": ["settings:storage", ".memgrid"], workbench: ["workbench", ".workbench-history .gh-row"] };
if (!Object.hasOwn(screens, pageName)) throw new Error("Choose a supported review page.");
const [screen, proof] = screens[pageName];
const root = join(desktop, "avalonia/bin/parity");
const out = join(desktop, "avalonia/reviews", pageName);
const browser = await chromium.launch({ channel: process.env.PERF_CHANNEL ?? "chromium" });
const results = [];
try {
  await mkdir(out, { recursive: true });
  for (const [width, height] of [[1440,900],[760,480]]) {
    for (const scheme of ["light","dark"]) {
      const stem = pageName + (width === 1440 ? "" : `-${width}x${height}`) + "." + scheme;
      const file = `${width}x${height}-${scheme}.png`;
      const context = await browser.newContext({ viewport: { width, height }, colorScheme: scheme, deviceScaleFactor: 1 });
      try {
        const source = await context.newPage();
        const url = new URL(process.env.PERF_URL ?? "http://localhost:4399/perf.html");
        url.search = new URLSearchParams({ pref:"zh",ws:"2",sess:"6",turns:"2",screen });
        await source.goto(url.href, { waitUntil: "networkidle" });
        await source.locator(proof).first().waitFor();
        await source.addStyleTag({ content: "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important}" });
        await source.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
        const report = JSON.parse(await readFile(join(root,"native",stem+".json"), "utf8"));
        const counts = Object.fromEntries(["box","style","missing","extra","token"].map(k => [k,report.findings.filter(v=>v.Kind===k).length]));
        const record = { file, width, height, scheme, referenceNodes: report.nodes, observedNodes: report.observed, counts };
        results.push(record);
        await pair(stem, file);
        if (pageName === "workbench") {
          const local = /^class:workbench|^action:(?:workbench\.|commit\.|push\.|browser\.full)|^role:tablist/;
          record.workbenchBoxDifferences = report.findings.filter(f => f.Kind === "box" && local.test(f.Identity)).length;
          await source.locator('.workbench-changes [data-action="workbench.changes-page"]').first().click();
          await source.locator(".wb-changes .wb-change").first().waitFor();
          record.changes = file.replace(".png","-changes.png");
          await pair(stem.replace("workbench","workbench-changes"), record.changes);
          await source.locator('.wb-changes [data-action="commit.draft"]').first().click();
          await source.locator('.wb-changes [data-action="commit.edit"]').first().waitFor();
          record.commitReview = file.replace(".png","-commit.png");
          await pair(stem.replace("workbench","workbench-commit"), record.commitReview);
        }

        async function pair(scene, filename) {
          await source.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); });
          const reference = await source.screenshot({ path: join(root, "reference", scene+".png") });
          const native = await readFile(join(root,"native",scene+".png"));
          for (const image of [reference,native]) {
            if (image.readUInt32BE(16)!==width || image.readUInt32BE(20)!==height) throw new Error("Mismatched screenshot dimensions: "+scene);
          }
          const compare = await context.newPage();
          try {
            await compare.setViewportSize({width:width*2+12, height:height+36});
            const labels=["Electron","Avalonia"];
            const columns=[reference,native].map((image,i)=>`<figure><figcaption>${labels[i]} · ${width} × ${height} · ${scheme}</figcaption><img src="data:image/png;base64,${image.toString("base64")}"></figure>`).join("");
            await compare.setContent(`<!doctype html><meta charset="utf-8"><style>:root{color-scheme:${scheme}}body{margin:0;display:flex;gap:12px;font:14px system-ui}figure{margin:0}figcaption{height:36px;box-sizing:border-box;padding:8px 12px}img{display:block}</style>${columns}`);
            await compare.locator("img").evaluateAll(images=>Promise.all(images.map(i=>i.decode())));
            await compare.screenshot({path:join(out,filename)});
          } finally { await compare.close(); }
        }
      } finally { await context.close(); }
    }
  }
  await writeFile(join(out,"report.json"),JSON.stringify({page:pageName, toleranceDip:0.5, results},null,2)+"\n");
  console.log(results.map(r=>r.file+" · "+r.counts.box+" box differences").join("\n"));
} finally { await browser.close(); }
