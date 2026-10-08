// Pre-fills the TOC field's cached result with real entries and page numbers, so the contents list
// shows even before Word updates fields. Usage: node fill-toc.js <in.docx> <headings.json> <pages.txt> <out.docx>
// pages.txt = pdftotext output of a render (pages separated by form feeds).
const JSZip = require("jszip");
const fs = require("fs");

const [, , inDocx, headingsFile, pagesFile, outDocx] = process.argv;
const headings = JSON.parse(fs.readFileSync(headingsFile, "utf8"));
const squash = (s) => s.replace(/[\s ]+/g, "").replace(/[–—]/g, "-").toLowerCase();
const pages = fs.readFileSync(pagesFile, "utf8").split("\f").map(squash);

let p = 2; // page index 2 = page 3; the cover and contents come first
const entries = [];
for (const h of headings) {
  const key = squash(h.text).slice(0, 28);
  let found = -1;
  for (let i = p; i < pages.length; i++) if (pages[i].includes(key)) { found = i; break; }
  if (found < 0) { console.error("NOT FOUND:", h.text); process.exit(1); }
  p = found;
  entries.push({ ...h, page: found + 1 });
}

const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const xmlEntries = entries.map((e) =>
  `<w:p><w:pPr><w:pStyle w:val="TOC${e.level}"/><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9350"/></w:tabs></w:pPr>` +
  `<w:r><w:t xml:space="preserve">${esc(e.text)}</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>${e.page}</w:t></w:r></w:p>`).join("");

JSZip.loadAsync(fs.readFileSync(inDocx)).then(async (zip) => {
  let xml = await zip.file("word/document.xml").async("string");
  const sep = '<w:fldChar w:fldCharType="separate"/></w:r></w:p>';
  const at = xml.indexOf(sep, xml.indexOf("TOC \\h"));
  if (at < 0) throw new Error("TOC field not found");
  xml = xml.slice(0, at + sep.length) + xmlEntries + xml.slice(at + sep.length);
  zip.file("word/document.xml", xml);
  fs.writeFileSync(outDocx, await zip.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }));
  console.log(entries.map((e) => `${e.page}`.padStart(3) + "  " + (e.level === 2 ? "   " : "") + e.text).join("\n"));
});
