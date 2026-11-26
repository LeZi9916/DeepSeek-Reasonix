import { getDocument, TextLayer, type PDFDocumentProxy } from "pdfjs-dist/legacy/build/pdf.mjs";
import { WorkerMessageHandler } from "pdfjs-dist/legacy/build/pdf.worker.mjs";
import resources from "virtual:reasonix-pdf-assets";

(globalThis as typeof globalThis & { pdfjsWorker: unknown }).pdfjsWorker = { WorkerMessageHandler };

const packets: Uint8Array[] = [];
let documentFile: PDFDocumentProxy;
let current: { cancel: () => void } | undefined;
let generation = 0;
let pages = 0;
let scale = 0;

class EmbeddedData {
  async fetch({ kind, filename }: { kind: string; filename: string }) {
    const data = resources[kind]?.[filename];
    if (!data) throw new Error("Missing PDF.js asset");
    return Uint8Array.from(atob(data), char => char.charCodeAt(0));
  }
}

export function append(packet: string) {
  packets.push(Uint8Array.from(atob(packet), char => char.charCodeAt(0)));
}

export function open() {
  void readDocument().then(value => { pages = value; }, () => { pages = -1; });
}

export function pageCount() { return pages; }

export function draw(page: number, zoom: number) {
  scale = 0;
  const mine = ++generation;
  void renderPage(page, zoom, mine).then(
    value => { if (mine === generation) scale = value; },
    () => { if (mine === generation) scale = -1; },
  );
}

export function drawnScale() { return scale; }

export function theme(page: string) {
  document.documentElement.style.setProperty("--page", page);
}

async function readDocument() {
  const bytes = new Uint8Array(packets.reduce((length, part) => length + part.length, 0));
  let offset = 0;
  for (const part of packets.splice(0)) {
    bytes.set(part, offset);
    offset += part.length;
  }
  const task = getDocument({
    data: bytes,
    enableXfa: false,
    useWorkerFetch: false,
    cMapUrl: "./cmaps/",
    cMapPacked: true,
    standardFontDataUrl: "./standard_fonts/",
    wasmUrl: "./wasm/",
    BinaryDataFactory: EmbeddedData,
  });
  task.onPassword = () => { void task.destroy(); };
  documentFile = await task.promise;
  return documentFile.numPages;
}

async function renderPage(page: number, zoom: number, mine: number) {
  current?.cancel();
  const pdfPage = await documentFile.getPage(page);
  if (mine !== generation) return 0;

  const host = document.getElementById("page")!;
  const base = pdfPage.getViewport({ scale: 1 });
  const scale = zoom || Math.max(100, host.clientWidth - 24) / base.width;
  const viewport = pdfPage.getViewport({ scale });
  const ratio = Math.min(window.devicePixelRatio || 1, Math.sqrt(16_777_216 / (viewport.width * viewport.height)));
  const canvas = document.createElement("canvas");
  canvas.width = Math.max(1, Math.floor(viewport.width * ratio));
  canvas.height = Math.max(1, Math.floor(viewport.height * ratio));
  canvas.style.width = dimensions("width");
  canvas.style.height = dimensions("height");
  const text = document.createElement("div");
  text.className = "textLayer";
  text.style.setProperty("--scale-factor", String(scale));
  text.style.setProperty("--total-scale-factor", String(scale));
  const layer = new TextLayer({ textContentSource: pdfPage.streamTextContent(), container: text, viewport });
  const render = pdfPage.render({ canvas, viewport, transform: [ratio, 0, 0, ratio, 0, 0] });
  current = { cancel: () => { render.cancel(); layer.cancel(); } };
  try {
    await Promise.all([render.promise, layer.render()]);
  } catch (error) {
    if (mine !== generation) return 0;
    throw error;
  }
  if (mine !== generation) return 0;

  const sheet = document.createElement("div");
  sheet.className = "sheet";
  sheet.style.width = dimensions("width");
  sheet.style.height = dimensions("height");
  sheet.append(canvas, text);
  host.replaceChildren(sheet);
  return Math.round(scale * 100);

  function dimensions(axis: "width" | "height") {
    return `${viewport[axis]}px`;
  }
}
