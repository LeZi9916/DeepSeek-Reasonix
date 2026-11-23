import { readFile, writeFile, access, readdir, cp } from "node:fs/promises";
import { resolve, join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(process.argv[2] ?? join(here, "../bin/parity"));
const index = JSON.parse(await readFile(join(here, "../baseline/index.json"), "utf8"));
const rows = [];
const responsive = (await readdir(join(here, "../baseline/responsive"))).filter(f => f.endsWith(".json")).sort();
for (const { name, file } of [...index.domains, ...responsive.map(file => ({ name: file.slice(0, -5), file }))]) {
  const stem = file.slice(0, -5);
  const report = JSON.parse(await readFile(join(root, "native", file), "utf8"));
  if (report.domain !== file || !report.observed) throw new Error(`Missing native measurements for ${name}`);
  const image = stem + ".png";
  await Promise.all(["reference", "native"].map(folder => access(join(root, folder, image))));
  const counts = { missing: 0, extra: 0, box: 0, style: 0, token: 0 };
  for (const finding of report.findings) counts[finding.Kind] = (counts[finding.Kind] ?? 0) + 1;
  rows.push({ name, image, file, observed: report.observed, reference: report.nodes, counts });
}
const manualShot = await access(join(root, "manual-webview-macos.png")).then(() => true, () => false);
const data = JSON.stringify(rows).replaceAll("<", "\\u003c");
const html = `<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>Reasonix Studio 界面对照</title>
<style>
:root{color-scheme:light dark;font:15px system-ui}
body{margin:1.5rem}header{display:flex;gap:1rem;align-items:center;flex-wrap:wrap}
h1{font-size:1.4rem}select{font:inherit;padding:.35rem}main{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem}
figure{margin:0}figcaption{margin:.6rem 0;font-weight:600}img{display:block;width:100%;border:1px solid #8886}a{color:inherit}
@media(max-width:800px){main{grid-template-columns:1fr}}
</style><header><h1>Reasonix Studio 界面对照</h1><label>场景 <select id="scene"></select></label><a href="overview.html">全部页面总览</a></header>
<p id="summary"></p><main><figure><figcaption>Electron 的 React 渲染器</figcaption><a id="oldLink"><img id="oldImage" alt="React 参考界面"></a></figure>
<figure><figcaption>Avalonia 原生窗口</figcaption><a id="newLink"><img id="newImage" alt="Avalonia 实际渲染"></a></figure></main>
<p><a id="detail">查看该场景的节点、几何、样式及资源差异</a></p>${manualShot ? '<p><a href="manual-webview-macos.png">macOS 系统 WebView 真窗口截图</a></p>' : ""}<script>
const rows=${data};const scene=document.getElementById("scene");
for(const row of rows)scene.add(new Option(row.name,row.name));
function show(){const row=rows.find(r=>r.name===scene.value)||rows[0];const {missing,extra,box,style,token}=row.counts;
 document.getElementById("summary").textContent="参考节点 "+row.reference+" · 已观测原生节点 "+row.observed+" · 缺少映射 "+missing+" · 额外映射 "+extra+" · 几何差异 "+box+" · 样式差异 "+style+" · 资源差异 "+token;
 for(const [key,dir] of [["old","reference"],["new","native"]]){const path=dir+"/"+row.image;
 document.getElementById(key+"Image").src=path;document.getElementById(key+"Link").href=path;}
 document.getElementById("detail").href="native/"+row.file;location.hash=encodeURIComponent(row.name);}
scene.value=decodeURIComponent(location.hash.slice(1))||rows[0].name;scene.onchange=show;show();
</script></html>`;
await writeFile(join(root, "index.html"), html);

const pageNames = {
  "settings-common": "常规", "settings-model": "模型服务", "settings-session": "会话与代理",
  "settings-session-persona": "系统人格", "settings-tools": "工具与权限", "settings-ext": "扩展",
  "settings-ext-market": "扩展市场", "settings-display": "会话显示", "settings-controls": "操作与性能",
  "settings-appearance": "外观", "settings-storage": "数据存储", "settings-network": "连接",
  "settings-account": "账户与关于",
  workbench: "工作台", analysis: "运行分析", onboarding: "首次引导", conversation: "会话",
};
const overview = `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Reasonix Studio 全部页面</title><style>
:root{font:15px system-ui;color-scheme:light dark}body{max-width:1600px;margin:auto;padding:24px}
header,nav{display:flex;gap:16px;align-items:center;flex-wrap:wrap}h1{font-size:24px;margin-right:auto}
select{font:inherit;padding:4px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,560px),1fr));gap:24px 20px}
article{border-top:1px solid #8886;padding-top:8px}h2{font-size:17px;margin:6px 0}figure{margin:0;min-width:0}
section{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}figcaption{font-size:13px;margin-bottom:4px}
img{display:block;width:100%;height:auto;border:1px solid #8885;box-sizing:border-box}a{color:inherit}
</style><header><h1>Reasonix Studio 全部页面</h1><nav><label>主题 <select id="scheme"><option value="light">浅色</option><option value="dark">深色</option></select></label>
<label>窗口 <select id="size"><option value="">1440 × 900</option><option value="-760x480">760 × 480</option></select></label>
<a href="reviews/index.html">数据页与工作台 Git</a><a href="editors.html">记忆与代理预设</a><a href="index.html">逐页查看尺寸和样式差异</a></nav></header><main></main><script>
const rows=${data}, pages=${JSON.stringify(pageNames)}, main=document.querySelector("main"), scheme=document.getElementById("scheme"), size=document.getElementById("size");
function element(tag, content){const node=document.createElement(tag);if(content)node.textContent=content;return node;}
function render(){
  main.replaceChildren();
  for(const [key,title] of Object.entries(pages)){
    const row=rows.find(r=>r.image===key+size.value+"."+scheme.value+".png");
    if(!row)continue;
    const card=element("article"), link=element("a",title), heading=element("h2"), pair=element("section");
    link.href="index.html#"+encodeURIComponent(row.name);heading.append(link);card.append(heading,pair);
    for(const [folder,label] of [["reference","Electron"],["native","Avalonia"]]){
      const fig=element("figure"), image=element("img"), full=element("a");
      image.src=folder+"/"+row.image;image.alt=title+" · "+label;image.loading="lazy";
      image.width=size.value?760:1440;image.height=size.value?480:900;full.href=image.src;full.append(image);fig.append(element("figcaption",label),full);pair.append(fig);
    }
    main.append(card);
  }
}
scheme.onchange=size.onchange=render;render();</script></html>`;
await writeFile(join(root, "overview.html"), overview);

const flows = [
  ["memory-edit", "编辑记忆"], ["memory-history", "记忆历史与恢复"],
  ["preset-create", "创建代理预设"], ["preset-copy", "复制代理预设"],
];
for (const [name] of flows) {
  await Promise.all(["native", "reference"].map(folder => access(join(root, folder, name + ".light.png"))));
}
const panels = flows.map(([name,title]) => `<article><h2>${title}</h2><section>${[["reference","Electron"],["native","Avalonia"]].map(([dir,label]) => `<figure><figcaption>${label}</figcaption><a href="${dir}/${name}.light.png"><img alt="${title} · ${label}" src="${dir}/${name}.light.png" width="1440" height="900"></a></figure>`).join("")}</section></article>`).join("");
await writeFile(join(root, "editors.html"), `<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>记忆与代理预设对照</title><style>:root{font:15px system-ui;color-scheme:light dark}body{margin:24px}h1{font-size:23px}h2{font-size:18px}section{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}figure{margin:0}figcaption{margin:0 0 6px}img{width:100%;height:auto}a{color:inherit}article{margin:24px 0}@media(max-width:760px){section{grid-template-columns:1fr}}</style>
<a href="overview.html">全部页面</a><h1>记忆与代理预设</h1>${panels}</html>`);
await cp(join(here, "../reviews"), join(root, "reviews"), { recursive: true });
console.log(`${rows.length} paired renders in ${join(root, "index.html")}`);
