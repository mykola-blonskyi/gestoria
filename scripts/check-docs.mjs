// Checks the READMEs a reader follows by hand (run from anywhere: node scripts/check-docs.mjs):
// - every relative link points at a file that exists, and every #anchor at a heading that exists;
// - the headings that code and other docs cite by name are still there;
// - a command block marked <!-- cmd:<id> --> is identical wherever the same id appears. The root README repeats the setup
//   commands in "Local development" so a developer never leaves that section; the marks keep the two copies from drifting.
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const files = ["README.md", "web/README.md", "docs/specs/SPEC-009-api.md"];
const cited = [
  ["README.md", "database"], // Program.cs and compose.yaml
  ["README.md", "tax-year-configuration"], // SPEC-009
  ["README.md", "the-api-key"], // web/README.md
  ["web/README.md", "the-api-key"], // ApiKey.cs
  ["web/README.md", "the-taxpayer-profile"], // ProfilesEndpoint.cs
  ["web/README.md", "export-restore-and-delete"], // SPEC-009
];

const problems = [];
const read = (file) => readFileSync(path.join(root, file), "utf8");
const withoutCode = (text) => text.replace(/```[\s\S]*?```/g, "").replace(/`[^`\n]*`/g, "");

const anchors = new Map();
function anchorsOf(file) {
  if (!anchors.has(file)) {
    const seen = new Map();
    const set = new Set();
    for (const [, heading] of withoutCode(read(file)).matchAll(/^#{1,6} (.+)$/gm)) {
      const slug = heading.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc} -]/gu, "").replace(/ /g, "-");
      const n = seen.get(slug) ?? 0;
      seen.set(slug, n + 1);
      set.add(n ? `${slug}-${n}` : slug);
    }
    anchors.set(file, set);
  }
  return anchors.get(file);
}

let links = 0;
for (const file of files) {
  for (const [, target] of withoutCode(read(file)).matchAll(/\]\(([^)\s]+)\)/g)) {
    if (/^(https?|mailto):/.test(target)) continue;
    links++;
    const [to, hash] = target.split("#");
    const dest = to ? path.relative(root, path.resolve(root, path.dirname(file), to)) : file;
    if (!existsSync(path.join(root, dest))) problems.push(`${file}: ${target} points at a file that does not exist`);
    else if (hash && dest.endsWith(".md") && !anchorsOf(dest).has(hash)) problems.push(`${file}: ${target} points at a heading that does not exist`);
  }
}

for (const [file, anchor] of cited) {
  if (!anchorsOf(file).has(anchor)) problems.push(`${file}: the heading behind #${anchor} is cited by name elsewhere and is gone`);
}

const commands = new Map();
for (const file of files) {
  const text = read(file);
  for (const mark of text.matchAll(/<!-- cmd:([\w-]+) -->\n/g)) {
    const block = text.slice(mark.index + mark[0].length).match(/^```[^\n]*\n[\s\S]*?\n```/);
    const where = `${file}:${text.slice(0, mark.index).split("\n").length}`;
    if (!block) {
      problems.push(`${where}: cmd:${mark[1]} is not followed by a code block`);
      continue;
    }
    commands.set(mark[1], [...(commands.get(mark[1]) ?? []), { where, block: block[0] }]);
  }
}
for (const [id, copies] of commands) {
  if (copies.length < 2) problems.push(`${copies[0].where}: cmd:${id} appears once; a shared command needs its other copy`);
  for (const copy of copies.slice(1)) {
    if (copy.block !== copies[0].block) problems.push(`cmd:${id} differs between ${copies[0].where} and ${copy.where}`);
  }
}

if (problems.length) {
  console.error(problems.join("\n"));
  process.exit(1);
}
console.log(`${links} links, ${cited.length} cited headings and ${commands.size} shared commands are consistent.`);
