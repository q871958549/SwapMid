import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Inbox } from 'lucide-react'
import { Button } from './button'
import { EmptyState } from './empty-state'

describe('EmptyState', () => {
  it('渲染标题、补充说明、前导图标与下一步操作按钮', () => {
    render(
      <EmptyState
        icon={<Inbox data-testid="empty-icon" />}
        title="当前筛选条件下没有记录"
        description="调整月份或清除关键词后再试。"
        action={<Button size="sm" variant="outline">清除筛选</Button>}
      />,
    )

    const root = document.querySelector('[data-slot="empty-state"]')
    expect(root).not.toBeNull()
    expect(root?.className).toContain('max-w-prose')
    expect(screen.getByTestId('empty-icon')).toBeInTheDocument()
    expect(screen.getByText('当前筛选条件下没有记录')).toBeInTheDocument()
    expect(screen.getByText('调整月份或清除关键词后再试。')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '清除筛选' })).toBeInTheDocument()
  })

  it('compact 模式收紧纵向内边距且可选插槽为空时不渲染多余节点', () => {
    render(<EmptyState compact title="暂无数据" />)
    const root = document.querySelector('[data-slot="empty-state"]')
    expect(root).toHaveAttribute('data-compact', '')
    expect(root?.className).toContain('py-5')
    expect(document.querySelector('[data-slot="empty-state-icon"]')).toBeNull()
    expect(document.querySelector('[data-slot="empty-state-description"]')).toBeNull()
    expect(document.querySelector('[data-slot="empty-state-action"]')).toBeNull()
  })
})
