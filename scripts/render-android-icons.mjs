// Renders the Android launcher icons and splash screens from the in-app "X O" brand mark.
// Usage: node scripts/render-android-icons.mjs   (needs Google Chrome installed)
import { chromium } from '@playwright/test';
import { readdirSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';

const res = new URL('../frontend/android/app/src/main/res/', import.meta.url).pathname;
// Colors match frontend/src/styles/tokens.css.
const yellow = '#ffd36e', ink = '#493039', line = '#70494c', bg = '#dce9ff';
const mark = (fontPx) => `<span style="font: 900 ${fontPx}px/1 'Courier New', monospace; letter-spacing: -.1em; color: ${ink}; white-space: nowrap">X&nbsp;O</span>`;

const designs = {
  // Adaptive icon foreground: 108dp canvas, launcher masks to ~66dp, so keep the mark inside the middle.
  foreground: (s) => `<div style="width:${s}px;height:${s}px;display:grid;place-items:center">${mark(s * 0.27)}</div>`,
  // Legacy icons (Android 7.x launchers) are not masked, so draw the bordered tile ourselves.
  square: (s) => `<div style="width:${s}px;height:${s}px;display:grid;place-items:center"><div style="box-sizing:border-box;width:${s * 0.84}px;height:${s * 0.84}px;border-radius:${s * 0.16}px;background:${yellow};border:${s * 0.05}px solid ${line};display:grid;place-items:center">${mark(s * 0.3)}</div></div>`,
  round: (s) => `<div style="width:${s}px;height:${s}px;display:grid;place-items:center"><div style="box-sizing:border-box;width:${s * 0.88}px;height:${s * 0.88}px;border-radius:50%;background:${yellow};border:${s * 0.05}px solid ${line};display:grid;place-items:center">${mark(s * 0.3)}</div></div>`,
  splash: (w, h) => {
    const u = Math.min(w, h) / 100;
    return `<div style="width:${w}px;height:${h}px;background:${bg};display:grid;place-items:center"><div style="display:flex;align-items:center;gap:${u * 4}px">
      <div style="background:${yellow};border:${u * 1.2}px solid ${line};box-shadow:${u * 1.2}px ${u * 1.2}px 0 ${line};padding:${u * 4}px ${u * 2.4}px">${mark(u * 7)}</div>
      <span style="font: 900 ${u * 11}px/1 'Courier New', monospace; letter-spacing: -.06em; color: ${ink}">GOMOKU</span></div></div>`;
  },
};

const size = (file) => execFileSync('sips', ['-g', 'pixelWidth', '-g', 'pixelHeight', file], { encoding: 'utf8' })
  .match(/\d+/g).slice(-2).map(Number);

const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
async function render(html, w, h, file, transparent) {
  await page.setViewportSize({ width: w, height: h });
  await page.setContent(`<body style="margin:0;background:transparent">${html}</body>`);
  await page.screenshot({ path: file, omitBackground: transparent, clip: { x: 0, y: 0, width: w, height: h } });
}

for (const dir of readdirSync(res)) {
  const targets = [
    ['ic_launcher_foreground.png', (s) => designs.foreground(s), true],
    ['ic_launcher.png', (s) => designs.square(s), true],
    ['ic_launcher_round.png', (s) => designs.round(s), true],
  ];
  for (const [name, design, transparent] of targets) {
    const file = join(res, dir, name);
    if (!existsSync(file)) continue;
    const [w] = size(file);
    await render(design(w), w, w, file, transparent);
  }
  const splash = join(res, dir, 'splash.png');
  if (existsSync(splash)) {
    const [w, h] = size(splash);
    await render(designs.splash(w, h), w, h, splash, false);
  }
}
await browser.close();
console.log('Rendered launcher icons and splash screens into', res);
