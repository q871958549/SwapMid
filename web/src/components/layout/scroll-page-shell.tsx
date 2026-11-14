import { type ReactNode } from 'react'

interface ScrollPageShellProps {
  header: ReactNode
  children: ReactNode
}

export function ScrollPageShell({ header, children }: ScrollPageShellProps) {
  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <div className="shrink-0">{header}</div>
      <div className="scrollbar-fade min-h-0 flex-1 overflow-y-auto overscroll-none pb-6 pr-2 [overflow-anchor:none]">
        {children}
      </div>
    </div>
  )
}
