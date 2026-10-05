// Opens both user guides from disk with every topic expanded and reports any
// <img> that doesn't load. Optionally saves how given screenshots look in the
// page, to check the caption and size in context:
//
//   node check-guide.mjs [shots/admin/x.png ...]   → ./.out/check-<file>.png
//
// Needs no server. Exits 1 if any image is broken.
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const GUIDE = new URL("../../user-guide/", import.meta.url).href;
const OUT = new URL("./.out/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });
const inContext = process.argv.slice(2);

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 900 } });
let broken = 0;
for (const file of ["altomatehr-admin-guide.html", "altomatehr-employee-guide.html"]) {
  await page.goto(GUIDE + file);
  await page.evaluate(() => document.querySelectorAll("details").forEach((d) => (d.open = true)));
  await page.evaluate(() => Promise.all([...document.images].map((i) => { i.loading = "eager"; return i.decode().catch(() => {}); })));
  await page.waitForTimeout(800);
  const bad = await page.evaluate(() => [...document.images].filter((i) => i.naturalWidth === 0).map((i) => i.getAttribute("src")));
  const total = await page.evaluate(() => document.images.length);
  console.log(`${file}: ${total} images, ${bad.length} broken${bad.length ? " → " + bad.join(", ") : ""}`);
  broken += bad.length;
  for (const src of inContext) {
    const fig = page.locator(`figure:has(img[src="${src}"])`);
    if ((await fig.count()) === 0) continue;
    await fig.first().scrollIntoViewIfNeeded();
    await fig.first().screenshot({ path: `${OUT}check-${src.split("/").pop()}` });
    console.log(`  saved .out/check-${src.split("/").pop()}`);
  }
}
await browser.close();
process.exit(broken ? 1 : 0);
