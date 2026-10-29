import type { MatchDto } from '../../shared/api/contracts';

export type FinishScene = { src: string; label: string };

export const finishScenes: Readonly<Record<'human' | 'bot', readonly FinishScene[]>> = {
  human: [
    { src: '/videos/finish/frame-sword-v2.webm', label: 'Hiệp sĩ dùng kiếm hạ robot' },
    { src: '/videos/finish/frame-arrow.webm', label: 'Hiệp sĩ bắn cung hạ robot' },
  ],
  bot: [
    { src: '/videos/finish/frame-fire.webm', label: 'Robot phun lửa vào hiệp sĩ' },
    { src: '/videos/finish/frame-electric.webm', label: 'Robot biến hiệp sĩ thành gà' },
  ],
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
