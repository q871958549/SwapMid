import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Badge } from './badge'
import { StatusBadge } from './status-badge'

describe('StatusBadge 与 Badge 语义变体', () => {
  it('在库等 success 态渲染全圆角翠绿胶囊与装饰性状态圆点', () => {
    render(<StatusBadge tone="success">在库</StatusBadge>)
    const badge = screen.getByText('在库')
    expect(badge).toHaveClass('rounded-full', 'bg-success-soft', 'text-success', 'border-success/30')
    expect(badge.querySelector('[aria-hidden="true"]')).not.toBeNull()
  })

  it('区分未入库草稿虚线态、在途冷蓝态与已报废删除线消亡态', () => {
    render(
      <div>
        <StatusBadge tone="draft">已登记</StatusBadge>
        <StatusBadge tone="info">调拨中</StatusBadge>
        <StatusBadge tone="outbound">已寄出</StatusBadge>
        <StatusBadge tone="scrapped">已报废</StatusBadge>
      </div>,
    )

    expect(screen.getByText('已登记')).toHaveClass('border-dashed', 'text-muted-foreground')
    expect(screen.getByText('调拨中')).toHaveClass('bg-info-soft', 'text-info')
    expect(screen.getByText('已寄出')).toHaveClass('bg-background', 'text-info')
    expect(screen.getByText('已报废')).toHaveClass('line-through')
  })

  it('支持关闭状态圆点且 Badge 支持 info 与 destructive-soft 语义变体', () => {
    render(
      <div>
        <StatusBadge tone="warning" dot={false}>领用中</StatusBadge>
        <Badge variant="info">进行中</Badge>
        <Badge variant="destructive-soft">逾期 3 天</Badge>
      </div>,
    )

    expect(screen.getByText('领用中').querySelector('[aria-hidden="true"]')).toBeNull()
    expect(screen.getByText('进行中')).toHaveClass('bg-info-soft', 'text-info', 'border-info/25')
    expect(screen.getByText('逾期 3 天')).toHaveClass('bg-destructive-soft', 'text-destructive', 'border-destructive/25')
  })
})
