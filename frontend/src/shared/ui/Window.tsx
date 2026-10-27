import type { ReactNode } from 'react';
export function Window({ title, children, className = '' }: { title: string; children: ReactNode; className?: string }) {
  return <section className={`window ${className}`} aria-label={title}><div className="window-bar">{title}</div><div className="window-body">{children}</div></section>;
}
