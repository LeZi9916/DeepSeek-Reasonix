import { createHash } from "node:crypto";
import { readFile, readdir, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { gunzipSync, gzipSync } from "node:zlib";
import { build } from "vite";

const documentDir = new URL("../src/ui/document/", import.meta.url);
const asset = new URL("../../avalonia/src/Reasonix.Studio.Shell/Assets/office-preview.html.gz", import.meta.url);
const notices = new URL("office-preview-licenses.txt", asset);
const compiled = await build({
  configFile: false,
  define: { "process.env.NODE_ENV": JSON.stringify("production") },
  build: {
    lib: { entry: fileURLToPath(new URL("nativeOffice.tsx", documentDir)), name: "reasonixOffice", formats: ["iife"] },
    target: ["es2022", "safari15.4"],
    write: false,
    reportCompressedSize: false,
  },
  esbuild: { jsx: "automatic" },
});
const bundle = compiled[0].output.find(item => item.type === "chunk");
const code = bundle.code.replace(/<\/script/gi, "<\\/script");
const hash = createHash("sha256").update(code).digest("base64");
const styles = await readFile(new URL("document.css", documentDir), "utf8");
const frame = "html,body,#office{display:flex;flex-direction:column;width:100%;height:100%;margin:0;overflow:hidden}"
  + ":root{--ui:system-ui,sans-serif;--w-med:600;--w-semi:700;--fs-ui:13px;--fs-meta:12px;--fs-micro:11px;--r-pill:999px;--r-sm:8px;--r-md:10px}"
  + "body{background:var(--page);color:var(--text);font:500 13px system-ui,sans-serif}"
  + ".document-office-view>p{margin:0;padding:8px 12px}";
const html = `<!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'sha256-${hash}'; frame-src about:; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; connect-src 'none'"><style>${styles}${frame}</style><div id="office"></div><script>${code}</script>`;
const roots = new Set();
for (const id of Object.keys(bundle.modules)) {
  const parts = id.replaceAll("\\", "/").split("/node_modules/");
  if (parts.length < 2) continue;
  const name = parts.at(-1).split("/").slice(0, parts.at(-1).startsWith("@") ? 2 : 1).join("/");
  roots.add(resolve(parts.slice(0, -1).join("/node_modules/"), "node_modules", name));
}
const terms = new Map();
for (const root of roots) {
  const info = JSON.parse(await readFile(join(root, "package.json"), "utf8"));
  const packageFiles = await readdir(root);
  const licenses = packageFiles.filter(file => /^(licen[cs]e|copying|notice)(?:[.-]|$)/i.test(file)).sort();
  const text = await Promise.all(licenses.map(file => readFile(join(root, file), "utf8")));
  terms.set(`${info.name} ${info.version}`, text.join("\n\n").trim() || info.license);
}
const credits = [...terms.keys()].sort().map(name => `${name}\n\n${terms.get(name)}`).join("\n\n---\n\n");
for (const [file, content] of [[asset, html], [notices, credits + "\n"]]) {
  if (process.argv.includes("--check")) {
    const stored = await readFile(file);
    if ((file === asset ? gunzipSync(stored) : stored).toString() !== content) {
      throw new Error("Run node tools/build-native-office.mjs to rebuild the embedded Office renderer.");
    }
  } else {
    await writeFile(file, file === asset ? gzipSync(content, { level: 9 }) : content);
  }
}
