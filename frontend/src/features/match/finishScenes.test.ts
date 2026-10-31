import { describe, expect, it } from 'vitest';
import { pickFinishScene, prefetchFinishScenes } from './finishScenes';

describe('pickFinishScene', () => {
  it('chooses either human cutscene when the human wins as X or O', () => {
    expect(pickFinishScene('XWon', 'X', () => 0)?.wide).toMatch(/sword-wide.*\.webm$/);
    expect(pickFinishScene('OWon', 'O', () => 0.99)?.wide).toMatch(/arrow-wide.*\.webm$/);
  });

  it('chooses either robot cutscene when the bot wins as X or O', () => {
    expect(pickFinishScene('OWon', 'X', () => 0)?.wide).toMatch(/fire-wide.*\.webm$/);
    expect(pickFinishScene('XWon', 'O', () => 0.99)?.tall).toMatch(/electric-tall.*\.webm$/);
  });

  it('does not select a win scene for draws or unfinished games', () => {
    expect(pickFinishScene('Draw', 'X', () => 0)).toBeNull();
    expect(pickFinishScene('InProgress', 'X', () => 0)).toBeNull();
  });

  it('prefetches every cutscene and poster for one format, once', () => {
    prefetchFinishScenes('tall');
    prefetchFinishScenes('tall');
    const links = [...document.head.querySelectorAll<HTMLLinkElement>('link[rel="prefetch"]')].map(link => link.getAttribute('href'));
    expect(links).toHaveLength(8);
    expect(links.some(link => /fire-tall.*\.webm$/.test(link ?? ''))).toBe(true);
    expect(links.some(link => /fire-tall.*\.jpg$/.test(link ?? ''))).toBe(true);
  });
});
