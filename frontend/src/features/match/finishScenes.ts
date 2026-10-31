import type { MatchDto } from '../../shared/api/contracts';

// Rendered by scripts/render-finish-cutscenes.sh into src/assets/finish. Importing them through Vite gives
// content-hashed URLs, so a re-render reaches players immediately instead of waiting out browser/CDN caches.
const files = import.meta.glob('../../assets/finish/*.{webm,jpg}', { eager: true, query: '?url', import: 'default' }) as Record<string, string>;
const asset = (file: string) => {
  const url = files[`../../assets/finish/${file}`];
  if (!url) throw new Error(`Missing cutscene asset: ${file}`);
  return url;
};

export type FinishFormat = 'wide' | 'tall';
// `wide` fits the desktop board, `tall` the phone board; each poster is the video's first frame.
export type FinishScene = Record<FinishFormat, string> & { poster: Record<FinishFormat, string>; label: string };

const scene = (name: string, label: string): FinishScene => ({
  wide: asset(`${name}-wide.webm`), tall: asset(`${name}-tall.webm`),
  poster: { wide: asset(`${name}-wide.jpg`), tall: asset(`${name}-tall.jpg`) },
  label,
});

export const finishScenes: Readonly<Record<'human' | 'bot', readonly FinishScene[]>> = {
  human: [scene('sword', 'Hiệp sĩ dùng kiếm hạ robot'), scene('arrow', 'Hiệp sĩ bắn cung hạ robot')],
  bot: [scene('fire', 'Robot phun lửa vào hiệp sĩ'), scene('electric', 'Robot biến hiệp sĩ thành gà')],
};

export function pickFinishScene(
  status: MatchDto['currentGame']['status'],
  humanStone: 'X' | 'O',
  random: () => number = Math.random,
): FinishScene | null {
  if (status !== 'XWon' && status !== 'OWon') return null;
  const winner = status[0];
  const scenes = finishScenes[winner === humanStone ? 'human' : 'bot'];
  const index = Math.max(0, Math.min(scenes.length - 1, Math.floor(random() * scenes.length)));
  return scenes[index];
}

// Phone boards are square or taller; the wide cut would play letterboxed at a third of the board height.
export function finishFormat(): FinishFormat {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' &&
    window.matchMedia('(max-width: 700px)').matches ? 'tall' : 'wide';
}

const prefetched = new Set<FinishFormat>();
// Warm the HTTP cache during the game so the finish cutscene starts without a network wait.
export function prefetchFinishScenes(format: FinishFormat) {
  if (prefetched.has(format) || typeof document === 'undefined') return;
  prefetched.add(format);
  for (const item of [...finishScenes.human, ...finishScenes.bot]) {
    for (const href of [item[format], item.poster[format]]) {
      const link = document.createElement('link');
      link.rel = 'prefetch';
      link.href = href;
      document.head.appendChild(link);
    }
  }
}
