import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ListPageShell } from './list-page-shell'

describe('ListPageShell', () => {
  it('按 页头 → 筛选 → 提示 → 汇总 → 主体 固定顺序渲染，并保持 flex 高度链与汇总区 28vh 限高', () => {
    const { container } = render(
      <ListPageShell
        header={<h1>页面标题</h1>}
        filters={<div>筛选栏</div>}
        notice={<p role="status">单行提示</p>}
        summary={<section>汇总表格</section>}
        table={<div>主数据表</div>}
      >
        <div>挂载在底部的弹层</div>
      </ListPageShell>,
    )

    const shell = container.querySelector('[data-slot="list-page-shell"]')!
    expect(shell.className).toContain('flex')
    expect(shell.className).toContain('h-full')
    expect(shell.className).toContain('min-h-0')
    expect(shell.className).toContain('flex-col')

    const slots = [...shell.children].map((el) => el.getAttribute('data-slot'))
    expect(slots).toEqual([
      'list-page-header',
      'list-page-filters',
      'list-page-notice',
      'list-page-summary',
      'list-page-body',
    ])

    const summary = container.querySelector('[data-slot="list-page-summary"]')!
    expect(summary.className).toContain('max-h-[28vh]')
    expect(summary.className).toContain('overflow-y-auto')
    expect(summary.className).toContain('shrink-0')

    const body = container.querySelector('[data-slot="list-page-body"]')!
    expect(body.className).toContain('min-h-0')
    expect(body.className).toContain('flex-1')
    expect(screen.getByText('主数据表')).toBeInTheDocument()
    expect(screen.getByText('挂载在底部的弹层')).toBeInTheDocument()
  })

  it('未传可选插槽时不渲染多余空包装节点，向后兼容仅传 header + children', () => {
    const { container } = render(
      <ListPageShell header={<h1>极简页</h1>}>
        <div>表格内容</div>
      </ListPageShell>,
    )

    expect(container.querySelector('[data-slot="list-page-filters"]')).toBeNull()
    expect(container.querySelector('[data-slot="list-page-notice"]')).toBeNull()
    expect(container.querySelector('[data-slot="list-page-summary"]')).toBeNull()
    expect(screen.getByRole('heading', { name: '极简页' })).toBeInTheDocument()
    expect(screen.getByText('表格内容')).toBeInTheDocument()
  })
})
