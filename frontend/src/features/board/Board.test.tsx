import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Board } from './Board';
import { BoardOverview } from './BoardOverview';
import { ZoomableBoard } from './ZoomableBoard';

let resize: ResizeObserverCallback;
class Observer {
  constructor(callback: ResizeObserverCallback) { resize = callback; }
  observe() {}
  disconnect() {}
  unobserve() {}
}
vi.stubGlobal('ResizeObserver', Observer);
afterEach(() => vi.clearAllMocks());
const moves = [{ step: 1, row: 2, column: 3, stone: 'X' as const }, { step: 2, row: 4, column: 5, stone: 'O' as const }];
function width(value: number) {
  act(() => resize([{ contentRect: { width: value } } as ResizeObserverEntry], {} as ResizeObserver));
}

describe('Board', () => {
  it('renders all 400 cells, text stones and the latest move', () => {
    render(<Board size={20} moves={moves} interactive />);
    expect(screen.getAllByRole('gridcell')).toHaveLength(400);
    expect(screen.getByRole('gridcell', { name: 'Hàng 3, cột 4, quân X' })).toHaveTextContent('X');
    expect(screen.getByRole('gridcell', { name: 'Hàng 5, cột 6, quân O, nước cuối' })).toHaveAttribute('data-last-move', 'true');
  });
  it('only submits empty cells during an interactive turn', () => {
    const onMove = vi.fn();
    const { rerender } = render(<Board size={20} moves={moves} interactive onMove={onMove} />);
    width(820);
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 3, cột 4, quân X' }));
    expect(onMove).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
    expect(onMove).toHaveBeenCalledExactlyOnceWith(0, 0);
    rerender(<Board size={20} moves={moves} interactive={false} onMove={onMove} />);
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 2, ô trống' }));
    expect(onMove).toHaveBeenCalledTimes(1);
  });
  it('keeps a compact board read-only until zoom is opened', () => {
    const onMove = vi.fn();
    render(<Board size={20} moves={[]} interactive onMove={onMove} />);
    width(320);
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
    expect(onMove).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Phóng bàn cờ' }));
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
    expect(onMove).toHaveBeenCalledExactlyOnceWith(0, 0);
    fireEvent.click(screen.getByRole('button', { name: 'Toàn cảnh' }));
    expect(screen.getByRole('button', { name: 'Phóng bàn cờ' })).toHaveFocus();
  });
  it('requires 28px mouse cells before enabling inline interaction', () => {
    const onMove = vi.fn();
    render(<Board size={20} moves={[]} interactive onMove={onMove} />);
    width(500);
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
    expect(onMove).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Phóng bàn cờ' })).toBeInTheDocument();
    width(563);
    expect(screen.getByRole('button', { name: 'Phóng bàn cờ' })).toBeInTheDocument();
    width(600);
    expect(screen.queryByRole('button', { name: 'Phóng bàn cờ' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' }));
    expect(onMove).toHaveBeenCalledExactlyOnceWith(0, 0);
  });
  it('overview never submits a move', () => {
    const onMove = vi.fn();
    render(<BoardOverview size={20} moves={[]} interactive onMove={onMove} />);
    fireEvent.click(screen.getAllByRole('gridcell')[0]);
    expect(onMove).not.toHaveBeenCalled();
  });
  it('moves keyboard focus with arrows and submits once for Enter or Space', () => {
    const onMove = vi.fn();
    render(<ZoomableBoard size={20} moves={[]} interactive onMove={onMove} />);
    const center = screen.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' });
    expect(center).toHaveFocus();
    fireEvent.keyDown(center, { key: 'ArrowRight' });
    const next = screen.getByRole('gridcell', { name: 'Hàng 11, cột 12, ô trống' });
    expect(next).toHaveFocus();
    fireEvent.keyDown(next, { key: 'Enter' });
    expect(onMove).toHaveBeenLastCalledWith(10, 11);
    fireEvent.keyDown(next, { key: ' ', repeat: true });
    expect(onMove).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(next, { key: ' ' });
    expect(onMove).toHaveBeenCalledTimes(2);
  });
  it('preserves focus when server moves update and blocks occupied keyboard activation', () => {
    const onMove = vi.fn();
    const { rerender } = render(<ZoomableBoard size={20} moves={moves} interactive onMove={onMove} />);
    const last = screen.getByRole('gridcell', { name: 'Hàng 5, cột 6, quân O, nước cuối' });
    expect(last).toHaveFocus();
    fireEvent.keyDown(last, { key: 'Enter' });
    expect(onMove).not.toHaveBeenCalled();
    fireEvent.keyDown(last, { key: 'ArrowDown' });
    const focused = screen.getByRole('gridcell', { name: 'Hàng 6, cột 6, ô trống' });
    rerender(<ZoomableBoard size={20} moves={[...moves, { step: 3, row: 8, column: 8, stone: 'X' }]} interactive onMove={onMove} />);
    expect(focused).toHaveFocus();
  });
  it('clamps arrow navigation at board edges', () => {
    render(<ZoomableBoard size={2} moves={[]} interactive />);
    const corner = screen.getByRole('gridcell', { name: 'Hàng 2, cột 2, ô trống' });
    fireEvent.keyDown(corner, { key: 'ArrowRight' });
    fireEvent.keyDown(corner, { key: 'ArrowDown' });
    expect(corner).toHaveFocus();
    fireEvent.keyDown(corner, { key: 'ArrowUp' });
    const top = screen.getByRole('gridcell', { name: 'Hàng 1, cột 2, ô trống' });
    expect(top).toHaveFocus();
    fireEvent.keyDown(top, { key: 'ArrowLeft' });
    const origin = screen.getByRole('gridcell', { name: 'Hàng 1, cột 1, ô trống' });
    fireEvent.keyDown(origin, { key: 'ArrowLeft' });
    fireEvent.keyDown(origin, { key: 'ArrowUp' });
    expect(origin).toHaveFocus();
  });
  it('blocks keyboard submission when the server turn is not interactive', () => {
    const onMove = vi.fn();
    render(<ZoomableBoard size={20} moves={[]} interactive={false} onMove={onMove} />);
    fireEvent.keyDown(screen.getByRole('gridcell', { name: 'Hàng 11, cột 11, ô trống' }), { key: 'Enter' });
    expect(onMove).not.toHaveBeenCalled();
  });

});
