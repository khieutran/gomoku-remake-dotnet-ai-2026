import { describe, expect, it } from 'vitest';
import { pickFinishScene } from './finishScenes';

describe('pickFinishScene', () => {
  it('chooses either human cutscene when the human wins as X or O', () => {
    expect(pickFinishScene('XWon', 'X', () => 0)?.wide).toBe('/videos/finish/sword-wide.webm');
    expect(pickFinishScene('OWon', 'O', () => 0.99)?.wide).toBe('/videos/finish/arrow-wide.webm');
  });

  it('chooses either robot cutscene when the bot wins as X or O', () => {
    expect(pickFinishScene('OWon', 'X', () => 0)?.wide).toBe('/videos/finish/fire-wide.webm');
    expect(pickFinishScene('XWon', 'O', () => 0.99)?.tall).toBe('/videos/finish/electric-tall.webm');
  });

  it('does not select a win scene for draws or unfinished games', () => {
    expect(pickFinishScene('Draw', 'X', () => 0)).toBeNull();
    expect(pickFinishScene('InProgress', 'X', () => 0)).toBeNull();
  });
});
