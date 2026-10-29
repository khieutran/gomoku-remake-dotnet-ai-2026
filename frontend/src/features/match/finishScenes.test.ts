import { describe, expect, it } from 'vitest';
import { pickFinishScene } from './finishScenes';

describe('pickFinishScene', () => {
  it('chooses either human cutscene when the human wins as X or O', () => {
    expect(pickFinishScene('XWon', 'X', () => 0)?.src).toBe('/videos/finish/frame-sword-v2.webm');
    expect(pickFinishScene('OWon', 'O', () => 0.99)?.src).toBe('/videos/finish/frame-arrow.webm');
  });

  it('chooses either robot cutscene when the bot wins as X or O', () => {
    expect(pickFinishScene('OWon', 'X', () => 0)?.src).toBe('/videos/finish/frame-fire.webm');
    expect(pickFinishScene('XWon', 'O', () => 0.99)?.src).toBe('/videos/finish/frame-electric.webm');
  });

  it('does not select a win scene for draws or unfinished games', () => {
    expect(pickFinishScene('Draw', 'X', () => 0)).toBeNull();
    expect(pickFinishScene('InProgress', 'X', () => 0)).toBeNull();
  });
});
