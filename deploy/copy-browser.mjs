import { cp, mkdir, readdir } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';

const [source, destination] = process.argv.slice(2);
if (!source || !destination) throw new Error('Usage: copy-browser.mjs <build-output-root> <wwwroot>');
const candidates = [];
async function findIndexes(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) await findIndexes(path);
    else if (entry.name === 'index.html') candidates.push(dirname(path));
  }
}
await findIndexes(resolve(source));
if (candidates.length !== 1) {
  throw new Error(`Expected one client-rendered Angular index.html, found ${candidates.length}. Review output/SSR configuration.`);
}
await mkdir(resolve(destination), { recursive: true });
await cp(candidates[0], resolve(destination), { recursive: true });
console.log(`Copied Angular browser assets from ${candidates[0]}`);