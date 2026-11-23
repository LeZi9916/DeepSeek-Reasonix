import { createRoot } from "react-dom/client";
import { createElement } from "react";
import OfficePreview from "./OfficePreview";
import type { DocumentViewState } from "./viewState";

const mount = createRoot(document.getElementById("office")!);
const view: DocumentViewState = {};
const chunks: Uint8Array[] = [];

export function append(base64: string) {
  chunks.push(Uint8Array.from(atob(base64), letter => letter.charCodeAt(0)));
}

export function theme(properties: Record<string, string>) {
  for (const [key, color] of Object.entries(properties)) document.documentElement.style.setProperty(key, color);
}

export function show(path: string, messages: Record<string, string>, properties: Record<string, string>) {
  theme(properties);
  const bytes = new Uint8Array(chunks.reduce((length, chunk) => length + chunk.length, 0));
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.length;
  }
  chunks.length = 0;
  const translate = (key: string, vars?: Record<string, string | number>) =>
    (messages[key] ?? key).replace(/\{(\w+)\}/g, (match, name) => String(vars?.[name] ?? match));
  mount.render(createElement(OfficePreview, { data: bytes.buffer, path, view, translate }));
}
