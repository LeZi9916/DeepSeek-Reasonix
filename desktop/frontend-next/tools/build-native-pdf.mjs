import { createHash } from "node:crypto";
import { readFile, readdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { gunzipSync, gzipSync } from "node:zlib";
import { build } from "vite";

const root = dirname(fileURLToPath(import.meta.resolve("pdfjs-dist/package.json")));
const target = new URL("../../avalonia/src/Reasonix.Studio.Shell/Assets/", import.meta.url);
const groups = { cMapUrl: "cmaps", standardFontDataUrl: "standard_fonts", wasmUrl: "wasm" };
const data = {};
for (const [key, directory] of Object.entries(groups)) {
  const files = (await readdir(join(root, directory))).filter(name => /\.(?:bcmap|pfb|ttf|wasm|js)$/i.test(name)).sort();
  data[key] = Object.fromEntries(await Promise.all(files.map(async name => [
    name, (await readFile(join(root, directory, name))).toString("base64"),
  ])));
}

const compiled = await build({
  configFile: false,
  define: { "import.meta.url": JSON.stringify("about:blank") },
  plugins: [{
    name: "reasonix-pdf-assets",
    resolveId: id => id === "virtual:reasonix-pdf-assets" ? "\0" + id : undefined,
    load: id => id === "\0virtual:reasonix-pdf-assets" ? "export default " + JSON.stringify(data) : undefined,
  }],
  build: {
    lib: { entry: fileURLToPath(new URL("../src/ui/document/nativePdf.ts", import.meta.url)), name: "reasonixPdf", formats: ["iife"] },
    target: ["es2022", "safari15.4"],
    write: false,
    reportCompressedSize: false,
  },
});
const bundle = compiled[0].output.find(item => item.type === "chunk");
const code = bundle.code.replace(/<\/script/gi, "<\\/script");
const hash = createHash("sha256").update(code).digest("base64");
const bootstrap = `var reasonixPdfBoot=(()=>{
  const parts=[];
  return {
    append:packet=>parts.push(Uint8Array.from(atob(packet),ch=>ch.charCodeAt(0))),
    start:()=>{
      const decoder=new TextDecoder();
      const source=parts.splice(0).map(part=>decoder.decode(part,{stream:true})).join("")+decoder.decode();
      const script=document.createElement("script");
      script.textContent=source;
      document.head.append(script);
      script.remove();
    },
  };
})();`;
const startHash = createHash("sha256").update(bootstrap).digest("base64");
const style = await readFile(join(root, "web", "pdf_viewer.css"), "utf8");
const frame = "html,body,#page{width:100%;height:100%;margin:0}body{background:transparent}#page{overflow:auto;background:var(--page,#ececea)}"
  + ".sheet{position:relative;background:white;margin:12px auto;box-shadow:0 1px 4px #0002}.sheet canvas{display:block}";
const html = `<!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'sha256-${hash}' 'sha256-${startHash}' 'wasm-unsafe-eval'; style-src 'unsafe-inline'; img-src data: blob:; font-src data: blob:; base-uri 'none'; form-action 'none'; connect-src 'none'"><style>${style}${frame}</style><div id="page"></div><script>${bootstrap}</script>`;
const payload = JSON.stringify(html) + "\n" + code;

const files = ["LICENSE"];
for (const directory of Object.values(groups)) {
  const terms = (await readdir(join(root, directory))).filter(name => /^(licen[cs]e|copying|notice)/i.test(name)).sort();
  files.push(...terms.map(name => directory + "/" + name));
}
const credits = (await Promise.all(files.map(async file => file + "\n\n" + await readFile(join(root, file), "utf8")))).join("\n\n---\n\n");
for (const [file, content, compressed] of [["pdf-preview.html.gz", payload, true], ["pdf-preview-licenses.txt", credits, false]]) {
  const output = new URL(file, target);
  if (process.argv.includes("--check")) {
    const stored = await readFile(output);
    if ((compressed ? gunzipSync(stored) : stored).toString() !== content) {
      throw new Error("Run node tools/build-native-pdf.mjs to rebuild the embedded PDF renderer.");
    }
  } else {
    await writeFile(output, compressed ? gzipSync(content, { level: 9 }) : content);
  }
}
