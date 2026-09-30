// Checks the READMEs a reader follows by hand (run from anywhere: node scripts/check-docs.mjs):
// - every relative link points at a file that exists, and every #anchor at a heading that exists;
// - every README heading that code or docs cite by name, such as README.md, "Database", is still there;
// - a command block marked <!-- cmd:<id> --> is identical wherever the same id appears. The root README repeats the setup
//   commands in "Local development" so a developer never leaves that section; the marks keep the two copies from drifting.
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const files = ["README.md", "web/README.md", "docs/specs/SPEC-009-api.md"];
const citingPlaces = ["src", "web/src", "tests", "docs", "config", "compose.yaml", ...files];
const skipDirs = new Set(["node_modules", "bin", "obj", ".next", "openapi"]);

const problems = [];
const read = (file) => readFileSync(path.join(root, file), "utf8").replace(/\r\n/g, "\n");
const withoutFences = (text) => text.replace(/```[\s\S]*?```/g, "");
const withoutCode = (text) => withoutFences(text).replace(/`[^`\n]*`/g, "");
// GitHub's anchor for a heading: lower case, punctuation dropped (a code span keeps its text, not its backticks),
// spaces to hyphens, and -1, -2 on repeats.
const slug = (heading) => heading.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc} -]/gu, "").replace(/ /g, "-");

const anchors = new Map();
function anchorsOf(file) {
  if (!anchors.has(file)) {
    const seen = new Map();
    const set = new Set();
    for (const [, heading] of withoutFences(read(file)).matchAll(/^#{1,6} (.+)$/gm)) {
      const base = slug(heading);
      const n = seen.get(base) ?? 0;
      seen.set(base, n + 1);
      set.add(n ? `${base}-${n}` : base);
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

function* walk(place) {
  const full = path.join(root, place);
  if (!existsSync(full)) return;
  if (statSync(full).isFile()) {
    yield place;
    return;
  }
  for (const entry of readdirSync(full)) {
    if (!skipDirs.has(entry)) yield* walk(path.join(place, entry));
  }
}

// README.md, "Database" / web/README.md ("The API key") / README.md "Tax-year configuration" / web/README.md's layer rules.
// A C# string escapes its quotes, hence the optional backslashes.
const citation = /(?<![\w/])(web\/)?README\.md`?(?:(?:,\s*|\s+|\s*\(\s*)\\?"([^"\\\n]+)\\?"|'s ([a-z][a-z ]*?[a-z])(?=[).,;:]|\s*$))/gm;
const cited = new Map();
for (const place of new Set(citingPlaces)) {
  for (const file of walk(place)) {
    if (!/\.(cs|ts|tsx|md|ya?ml|json|mjs)$/.test(file)) continue;
    const text = read(file);
    for (const m of text.matchAll(citation)) {
      const key = `${m[1] ? "web/README.md" : "README.md"}#${slug(m[2] ?? m[3])}`;
      if (!cited.has(key)) cited.set(key, `${file}:${text.slice(0, m.index).split("\n").length}`);
    }
  }
}
for (const [key, where] of cited) {
  const [file, anchor] = key.split("#");
  if (!anchorsOf(file).has(anchor)) problems.push(`${where} cites a heading of ${file} (#${anchor}) that is gone`);
}

const commands = new Map();
for (const file of files) {
  const text = read(file);
  for (const mark of text.matchAll(/^[ \t]*<!--\s*cmd\b[^\n]*/gm)) {
    const where = `${file}:${text.slice(0, mark.index).split("\n").length}`;
    const id = mark[0].match(/^<!-- cmd:([\w-]+) -->$/)?.[1];
    if (!id) {
      problems.push(`${where}: "${mark[0]}" is not of the form <!-- cmd:<id> -->`);
      continue;
    }
    const block = text.slice(mark.index + mark[0].length + 1).match(/^```[^\n]*\n[\s\S]*?\n```/);
    if (!block) {
      problems.push(`${where}: cmd:${id} is not followed by a code block`);
      continue;
    }
    commands.set(id, [...(commands.get(id) ?? []), { where, block: block[0] }]);
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
console.log(`${links} links, ${cited.size} cited headings and ${commands.size} shared commands are consistent.`);
