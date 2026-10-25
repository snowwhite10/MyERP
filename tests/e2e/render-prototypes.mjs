import { chromium } from 'playwright';
import { readdir, stat } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const directory = resolve(process.argv[2] ?? '');
const expected = Number(process.argv[3]);
if (!process.argv[2] || !Number.isSafeInteger(expected) || expected < 0) {
  throw new Error('Usage: node render-prototypes.mjs DIRECTORY EXPECTED_COUNT');
}
const pages = (await readdir(directory)).filter(name => /^4\.5\.1\.\d+\.\d+\.html$/.test(name));
if (pages.length !== expected) throw new Error(`Expected ${expected} prototype pages; found ${pages.length}.`);

const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: 1, colorScheme: 'light' });
  for (const name of pages) {
    const file = join(directory, name);
    await page.goto(pathToFileURL(file).href, { waitUntil: 'load' });
    await page.screenshot({ path: file.replace(/\.html$/, '.png'), fullPage: true, animations: 'disabled' });
  }
  await page.close();
} finally {
  await browser.close();
}
const screenshots = (await readdir(directory)).filter(name => /^4\.5\.1\.\d+\.\d+\.png$/.test(name));
for (const name of screenshots) {
  if (!pages.includes(name.replace(/\.png$/, '.html'))) throw new Error(`Stale screenshot: ${name}`);
  if ((await stat(join(directory, name))).size < 24) throw new Error(`Empty screenshot: ${name}`);
}
if (screenshots.length !== expected) throw new Error(`Expected ${expected} screenshots; found ${screenshots.length}.`);
