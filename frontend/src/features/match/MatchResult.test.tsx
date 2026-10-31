import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { MatchDto } from '../../shared/api/contracts';
import { MatchResult } from './MatchResult';

const game = {
  id: '933412da-c99d-45be-a7e9-328fb464380b',
  number: 1, openingStone: 'X', aiVersion: null, status: 'XWon',
  nextStone: null, turnStatus: 'finished', aiErrorCode: null, aiRetryAt: null,
  moves: [],
} as MatchDto['currentGame'];

function renderResult(status: MatchDto['currentGame']['status'] = 'XWon', humanStone: 'X' | 'O' = 'X') {
  const onNext = vi.fn();
  const view = render(<MemoryRouter><MatchResult game={{ ...game, status }} humanStone={humanStone}
    scope="guest" busy={false} onNext={onNext} /></MemoryRouter>);
  return { ...view, onNext, video: () => view.container.querySelector('video') };
}

beforeEach(() => {
  vi.spyOn(Math, 'random').mockReturnValue(0);
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers(); });

it('plays one human cutscene and then reveals the existing result actions', () => {
  const view = renderResult();
  expect(view.video()).toHaveAttribute('src', expect.stringMatching(/sword-wide.*\.webm$/));
  expect(view.video()?.muted).toBe(true);
  expect(screen.queryByRole('region', { name: 'Kết quả ván' })).not.toBeInTheDocument();
  fireEvent.ended(view.video()!);
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Bạn thắng!');
  expect(screen.getByRole('button', { name: 'Chơi tiếp' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Xem lại' })).toBeInTheDocument();
});

it('keeps the same robot video across rerenders and lets the player skip it', () => {
  vi.mocked(Math.random).mockReturnValueOnce(0.99).mockReturnValue(0);
  const view = renderResult('XWon', 'O');
  expect(view.video()).toHaveAttribute('src', expect.stringMatching(/electric-wide.*\.webm$/));
  view.rerender(<MemoryRouter><MatchResult game={{ ...game, status: 'XWon' }} humanStone="O"
    scope="guest" busy={false} onNext={view.onNext} /></MemoryRouter>);
  expect(view.video()).toHaveAttribute('src', expect.stringMatching(/electric-wide.*\.webm$/));
  fireEvent.playing(view.video()!);
  fireEvent.click(screen.getByRole('button', { name: 'Bỏ qua hoạt cảnh' }));
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Bot thắng!');
});

it('shows the result immediately for a draw or reduced motion', () => {
  const draw = renderResult('Draw');
  expect(draw.video()).toBeNull();
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Ván hòa');
  draw.unmount();
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  const reduced = renderResult();
  expect(reduced.video()).toBeNull();
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Bạn thắng!');
});

it('falls back to the result if playback errors or autoplay is blocked', async () => {
  const failedMedia = renderResult();
  fireEvent.error(failedMedia.video()!);
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toBeInTheDocument();
  failedMedia.unmount();
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockRejectedValue(new Error('autoplay blocked'));
  renderResult();
  await waitFor(() => expect(screen.getByRole('region', { name: 'Kết quả ván' })).toBeInTheDocument());
});

it('does not trap the player if the browser never sends a video end event', () => {
  vi.useFakeTimers();
  renderResult();
  act(() => vi.advanceTimersByTime(6500));
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toBeInTheDocument();
});

it('names who won and taunts the bot, not the winner, with the winner character', () => {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  renderResult('OWon', 'O');
  const result = screen.getByRole('region', { name: 'Kết quả ván' });
  expect(result).toHaveTextContent('Bạn thắng!');
  expect(result).toHaveTextContent('Bot quá gà, chưa đủ tuổi!');
  expect(result).not.toHaveTextContent('XXXXX');
  expect(result.querySelector('img')).toHaveAttribute('src', expect.stringContaining('knight'));
});

it('picks a random taunt from the winner side, including the new lines, and keeps it for the game', () => {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  const taunts = (status: 'XWon' | 'OWon') => new Set([0, 0.17, 0.34, 0.5, 0.67, 0.84, 0.99].map(value => {
    vi.mocked(Math.random).mockReturnValue(value);
    const view = render(<MemoryRouter><MatchResult game={{ ...game, status }} humanStone="X" scope="guest" busy={false} onNext={vi.fn()} /></MemoryRouter>);
    const text = view.container.querySelector('.match-result-taunt')?.textContent ?? '';
    view.unmount();
    return text.replace(/[“”]/g, '');
  }));
  const fresh = ['Còn non lắm, lại đi con gà', 'Kém vậy, chơi hết lực xem nào!', 'Nhường đúng không ní!'];
  const botWins = taunts('OWon');
  const humanWins = taunts('XWon');
  for (const line of fresh) {
    expect(botWins).toContain(line);
    expect(humanWins).toContain(line);
  }
  expect(botWins).toContain('Chơi tiếp không ní, thấy hơi gà');
  expect(humanWins).toContain('Bot quá gà, chưa đủ tuổi!');

  vi.mocked(Math.random).mockReturnValue(0);
  const view = renderResult('OWon');
  const first = view.container.querySelector('.match-result-taunt')?.textContent;
  vi.mocked(Math.random).mockReturnValue(0.99);
  view.rerender(<MemoryRouter><MatchResult game={{ ...game, status: 'OWon' }} humanStone="X" scope="guest" busy={false} onNext={vi.fn()} /></MemoryRouter>);
  expect(view.container.querySelector('.match-result-taunt')?.textContent).toBe(first);
});

it('plays the tall cutscene on a phone-width board', () => {
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({ matches: query.includes('max-width'), addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  const view = renderResult();
  expect(view.video()).toHaveAttribute('src', expect.stringMatching(/sword-tall.*\.webm$/));
});

it('plays the finish cutscene once per game, even after leaving to replay and coming back', () => {
  localStorage.clear();
  const first = renderResult();
  expect(first.video()).not.toBeNull();
  first.unmount();
  const again = renderResult();
  expect(again.video()).toBeNull();
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Bạn thắng!');
  again.unmount();
  const otherGame = render(<MemoryRouter><MatchResult game={{ ...game, id: 'aaaa12da-c99d-45be-a7e9-328fb464380b' }}
    humanStone="X" scope="guest" busy={false} onNext={vi.fn()} /></MemoryRouter>);
  expect(otherGame.container.querySelector('video')).not.toBeNull();
});

it('crossfades from the cutscene into the result card instead of cutting', () => {
  vi.useFakeTimers();
  const view = renderResult();
  fireEvent.playing(view.video()!);
  expect(view.container.querySelector('.match-result-overlay--cutscene')).toHaveClass('match-result-overlay--entering');
  fireEvent.ended(view.video()!);
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toBeInTheDocument();
  expect(view.container.querySelector('.match-result-overlay--cutscene')).toHaveClass('match-result-overlay--leaving');
  expect(screen.queryByRole('button', { name: 'Bỏ qua hoạt cảnh' })).not.toBeInTheDocument();
  act(() => vi.advanceTimersByTime(400));
  expect(view.video()).toBeNull();
});

it('shows the first frame as poster and opens the cutscene only once the video is really playing', () => {
  vi.useFakeTimers();
  const view = renderResult();
  const overlay = () => view.container.querySelector('.match-result-overlay--cutscene');
  expect(view.video()).toHaveAttribute('poster', expect.stringMatching(/sword-wide.*\.jpg$/));
  expect(overlay()).toHaveClass('match-result-overlay--waiting');
  expect(screen.queryByRole('button', { name: 'Bỏ qua hoạt cảnh' })).not.toBeInTheDocument();
  fireEvent.playing(view.video()!);
  expect(overlay()).toHaveClass('match-result-overlay--entering');
  expect(screen.getByRole('button', { name: 'Bỏ qua hoạt cảnh' })).toBeInTheDocument();
  view.unmount();
  localStorage.clear();
  const slow = renderResult();
  act(() => vi.advanceTimersByTime(900));
  expect(slow.container.querySelector('.match-result-overlay--cutscene')).toHaveClass('match-result-overlay--entering');
});
