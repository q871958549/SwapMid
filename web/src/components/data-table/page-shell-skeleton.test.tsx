import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { PageShellSkeleton } from './page-shell-skeleton'

describe('PageShellSkeleton', () => {
  it('立刻渲染页面标题，让冷切换先看到对的页', () => {
    render(<PageShellSkeleton title="样品" description="实物追溯" />)
    expect(screen.getByRole('heading', { name: '样品' })).toBeInTheDocument()
    expect(screen.getByText('实物追溯')).toBeInTheDocument()
  })
})
