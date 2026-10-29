import type { ReactNode } from 'react';
export function Window({ title, children, className = '', headerActionsRef }: { title: string; children: ReactNode; className?: string; headerActionsRef?: (element: HTMLDivElement | null) => void }) {
  return <section className={`window ${className}`} aria-label={title}><div className="window-bar"><span>{title}</span>{headerActionsRef && <div className="window-bar-actions" ref={headerActionsRef} />}</div><div className="window-body">{children}</div></section>;
}
