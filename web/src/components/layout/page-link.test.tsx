import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

vi.mock('next/link', () => ({
  default: ({
    href,
    prefetch,
    children,
    ...props
  }: {
    href: string
    prefetch?: boolean | null
    children?: React.ReactNode
  }) => (
    <a
      {...props}
      href={href}
      data-prefetch={prefetch === null ? 'auto' : String(prefetch)}
    >
      {children}
    </a>
  ),
}))

vi.mock('@/stores/use-tab-store', () => ({
  useStoredPagePath: (href: string) => `${href}?page=2`,
}))

import { PageLink } from './page-link'

describe('PageLink', () => {
  it('恢复页签存量路径，同时固定使用 Next auto 预取策略', () => {
    render(<PageLink href="/reporting/daily-output">日报</PageLink>)

    const link = screen.getByRole('link', { name: '日报' })
    expect(link).toHaveAttribute('href', '/reporting/daily-output?page=2')
    expect(link).toHaveAttribute('data-prefetch', 'auto')
  })
})
