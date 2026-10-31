// Turns the high-resolution "pixel art style" sprites into true pixel art on a real grid:
// each grid cell takes its most common color (no blur), all sprites share one quantized palette,
// and alpha is hard-edged. Output is tiny PNGs meant to be scaled up with nearest-neighbor.
// Usage: node scripts/pixelate-sprites.mjs [gridWidth=96] [paletteSize=24]   (needs Google Chrome)
import { chromium } from '@playwright/test';
import { readdirSync, mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const root = new URL('..', import.meta.url).pathname;
const srcDir = join(root, 'frontend/media-src/sprites');
const outDir = join(root, 'frontend/public/images/sprites');
const gridWidth = Number(process.argv[2] ?? 96);
const paletteSize = Number(process.argv[3] ?? 24);

const names = readdirSync(srcDir).filter(file => file.endsWith('.png')).map(file => file.slice(0, -4));
const sources = Object.fromEntries(names.map(name =>
  [name, 'data:image/png;base64,' + readFileSync(join(srcDir, name + '.png')).toString('base64')]));

const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
const results = await page.evaluate(async ({ sources, gridWidth, paletteSize }) => {
  const load = src => new Promise((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = src; });
  // 1. Snap every sprite to its grid: most common (coarsely bucketed) opaque color per cell.
  const grids = {};
  for (const [name, src] of Object.entries(sources)) {
    const image = await load(src);
    const canvas = new OffscreenCanvas(image.width, image.height);
    const ctx = canvas.getContext('2d');
    ctx.drawImage(image, 0, 0);
    const { data } = ctx.getImageData(0, 0, image.width, image.height);
    const gw = gridWidth, gh = Math.round(gridWidth * image.height / image.width);
    const cells = new Array(gw * gh).fill(null);
    for (let gy = 0; gy < gh; gy++) for (let gx = 0; gx < gw; gx++) {
      const x0 = Math.floor(gx * image.width / gw), x1 = Math.floor((gx + 1) * image.width / gw);
      const y0 = Math.floor(gy * image.height / gh), y1 = Math.floor((gy + 1) * image.height / gh);
      const buckets = new Map(); let opaque = 0, total = 0;
      for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
        const i = (y * image.width + x) * 4; total++;
        if (data[i + 3] < 128) continue;
        opaque++;
        const key = (data[i] >> 3) << 10 | (data[i + 1] >> 3) << 5 | (data[i + 2] >> 3);
        const bucket = buckets.get(key) ?? { n: 0, r: 0, g: 0, b: 0 };
        bucket.n++; bucket.r += data[i]; bucket.g += data[i + 1]; bucket.b += data[i + 2];
        buckets.set(key, bucket);
      }
      if (opaque * 2 < total) continue;
      let best = null;
      for (const bucket of buckets.values()) if (!best || bucket.n > best.n) best = bucket;
      cells[gy * gw + gx] = [best.r / best.n, best.g / best.n, best.b / best.n];
    }
    grids[name] = { gw, gh, cells };
  }
  // 2. One shared palette (k-means, farthest-point seeding) so every character uses the same colors.
  const samples = Object.values(grids).flatMap(grid => grid.cells.filter(Boolean));
  const dist = (a, b) => (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2;
  const palette = [samples[0]];
  while (palette.length < paletteSize) {
    let far = samples[0], farDist = -1;
    for (const sample of samples) { const d = Math.min(...palette.map(p => dist(p, sample))); if (d > farDist) { farDist = d; far = sample; } }
    palette.push(far);
  }
  for (let round = 0; round < 12; round++) {
    const sums = palette.map(() => [0, 0, 0, 0]);
    for (const sample of samples) {
      let k = 0, kd = Infinity;
      palette.forEach((p, i) => { const d = dist(p, sample); if (d < kd) { kd = d; k = i; } });
      sums[k][0] += sample[0]; sums[k][1] += sample[1]; sums[k][2] += sample[2]; sums[k][3]++;
    }
    sums.forEach((sum, i) => { if (sum[3]) palette[i] = [sum[0] / sum[3], sum[1] / sum[3], sum[2] / sum[3]]; });
  }
  // 3. Paint each grid with the palette and export PNG.
  const out = {};
  for (const [name, grid] of Object.entries(grids)) {
    const canvas = new OffscreenCanvas(grid.gw, grid.gh);
    const ctx = canvas.getContext('2d');
    const imageData = ctx.createImageData(grid.gw, grid.gh);
    grid.cells.forEach((cell, i) => {
      if (!cell) return;
      let best = palette[0], bd = Infinity;
      for (const p of palette) { const d = dist(p, cell); if (d < bd) { bd = d; best = p; } }
      imageData.data.set([Math.round(best[0]), Math.round(best[1]), Math.round(best[2]), 255], i * 4);
    });
    ctx.putImageData(imageData, 0, 0);
    const blob = await canvas.convertToBlob({ type: 'image/png' });
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let binary = ''; for (const byte of bytes) binary += String.fromCharCode(byte);
    out[name] = btoa(binary);
  }
  return out;
}, { sources, gridWidth, paletteSize });
await browser.close();

mkdirSync(outDir, { recursive: true });
for (const [name, base64] of Object.entries(results)) writeFileSync(join(outDir, name + '.png'), Buffer.from(base64, 'base64'));
console.log(`Pixelated ${names.length} sprites to ${gridWidth}px grid, ${paletteSize} colors -> ${outDir}`);
