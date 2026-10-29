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
  expect(view.video()).toHaveAttribute('src', '/videos/finish/frame-sword-v2.webm');
  expect(view.video()?.muted).toBe(true);
  expect(screen.queryByRole('region', { name: 'Kết quả ván' })).not.toBeInTheDocument();
  fireEvent.ended(view.video()!);
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Quá Gà, Chưa đủ tuổi!');
  expect(screen.getByRole('button', { name: 'Chơi tiếp' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Xem lại' })).toBeInTheDocument();
});

it('keeps the same robot video across rerenders and lets the player skip it', () => {
  vi.mocked(Math.random).mockReturnValueOnce(0.99).mockReturnValue(0);
  const view = renderResult('XWon', 'O');
  expect(view.video()).toHaveAttribute('src', '/videos/finish/frame-electric.webm');
  view.rerender(<MemoryRouter><MatchResult game={{ ...game, status: 'XWon' }} humanStone="O"
    scope="guest" busy={false} onNext={view.onNext} /></MemoryRouter>);
  expect(view.video()).toHaveAttribute('src', '/videos/finish/frame-electric.webm');
  fireEvent.click(screen.getByRole('button', { name: 'Bỏ qua hoạt cảnh' }));
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('BOT VICTORY');
});

it('shows the result immediately for a draw or reduced motion', () => {
  const draw = renderResult('Draw');
  expect(draw.video()).toBeNull();
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Ván hòa');
  draw.unmount();
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
  const reduced = renderResult();
  expect(reduced.video()).toBeNull();
  expect(screen.getByRole('region', { name: 'Kết quả ván' })).toHaveTextContent('Quá Gà, Chưa đủ tuổi!');
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
