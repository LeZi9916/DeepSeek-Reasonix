// Writes the reference frontend's message tables as a manifest the shell's
// cases can reconcile against.
//
// The case cannot reach into TypeScript from .NET, and re-reading the .ts files
// with a regular expression would be a second parser that disagrees with the
// first one exactly when it matters. This imports what Node actually evaluated
// and records the shape of it: how many messages, how many per area, and one
// digest over every key and both phrases. The text itself is not copied — the
// shell already carries it in generated files, and a second copy in the baseline
// is a copy that can drift.
//
// Run: node desktop/avalonia/tools/dump-messages.mjs
// Check: node desktop/avalonia/tools/dump-messages.mjs -check
//
// Output: desktop/avalonia/baseline/messages.json
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const SOURCE = join(HERE, "..", "..", "frontend-next", "src", "i18n", "msg");
const OUT = join(HERE, "..", "baseline", "messages.json");
const SKIPPED = new Set(["index.ts", "types.ts"]);

/** FNV-1a over UTF-16 code units, which is what both runtimes iterate. */
function digest(messages) {
  let hash = 0x811c9dc5;
  const feed = (text) => {
    for (let i = 0; i < text.length; i += 1) {
      hash ^= text.charCodeAt(i);
      hash = Math.imul(hash, 0x01000193) >>> 0;
    }
  };

  for (const message of messages) {
    feed(message.key);
    feed("\u0000");
    feed(message.zh);
    feed("\u0000");
    feed(message.en);
    feed("\u0001");
  }

  return "0x" + hash.toString(16).padStart(8, "0");
}

const files = readdirSync(SOURCE).filter((name) => name.endsWith(".ts") && !name.endsWith(".test.ts") && !SKIPPED.has(name));
const messages = [];

for (const file of files.sort()) {
  const module = await import(`file:///${SOURCE.replace(/\\/g, "/")}/${file}`);
  for (const table of Object.values(module)) {
    if (!table || typeof table !== "object") continue;
    for (const [key, message] of Object.entries(table)) {
      messages.push({ key, zh: message.zh, en: message.en });
    }
  }
}

messages.sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));

const areas = {};
for (const message of messages) {
  const dot = message.key.indexOf(".");
  const area = dot > 0 ? message.key.slice(0, dot) : "misc";
  areas[area] = (areas[area] ?? 0) + 1;
}

const manifest = {
  capturedFrom: "desktop/frontend-next/src/i18n/msg",
  files: files.length,
  count: messages.length,
  digest: digest(messages),
  areas: Object.fromEntries(Object.entries(areas).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))),
};
const text = JSON.stringify(manifest, null, 1) + "\n";

if (process.argv.includes("-check")) {
  let current = null;
  try {
    current = readFileSync(OUT, "utf8");
  } catch {
    console.error(`missing: ${OUT}; run node desktop/avalonia/tools/dump-messages.mjs`);
    process.exit(1);
  }

  if (current !== text) {
    console.error(`stale: ${OUT}; the reference tables changed since it was written`);
    console.error("run node desktop/avalonia/tools/dump-messages.mjs");
    process.exit(1);
  }

  console.log(`Message manifest is up to date: ${manifest.count} messages, ${manifest.digest}.`);
  process.exit(0);
}

writeFileSync(OUT, text, "utf8");
console.log(`${manifest.count} messages from ${files.length} files, ${manifest.digest}, ${Object.keys(areas).length} areas -> ${OUT}`);
