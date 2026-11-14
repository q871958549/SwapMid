import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { PageHeader } from './page-header'
import zhHansMessages from '../../../messages/zh-Hans.json'

describe('PageHeader', () => {
  it('窄屏隐藏说明、保留标题与主操作', () => {
    render(
      <PageHeader
        title="我的用车"
        description="共 1 张与我相关的单据"
        action={<button type="button">发起申请</button>}
      />,
    )

    expect(screen.getByRole('heading', { name: '我的用车' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '发起申请' })).toBeInTheDocument()
    expect(screen.getByText('共 1 张与我相关的单据')).toHaveClass('hidden')
    const root = screen.getByRole('heading', { name: '我的用车' }).parentElement?.parentElement
    expect(root?.className).toContain('flex-col')
    expect(root?.className).toContain('md:flex-row')
  })

  it('操作区允许换行，窄屏按钮加高', () => {
    render(
      <PageHeader
        title="候选人"
        action={(
          <div className="flex gap-2">
            <button type="button">登记</button>
            <a href="/import">导入</a>
          </div>
        )}
      />,
    )

    const actions = screen.getByRole('button', { name: '登记' }).parentElement?.parentElement
    expect(actions?.className).toContain('flex-wrap')
    expect(actions?.className).toContain('max-md:[&>div]:flex-wrap')
    expect(actions?.className).toContain('max-md:[&_button]:h-11')
    expect(actions?.className).toContain('max-md:[&_a]:h-11')
  })
})

describe('PageHeader 结构化插槽', () => {
  function renderIntl(ui: React.ReactElement) {
    return render(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
  }

  it('按固定顺序排：说明 → 次按钮 → 更多 → 主按钮（最右）', () => {
    renderIntl(
      <PageHeader
        title="样品"
        help={{ label: '口径说明', content: '只统计在库样品' }}
        secondaryActions={[<button key="a" type="button">导出</button>]}
        moreActions={<DropdownMenuItem>标签模板</DropdownMenuItem>}
        primaryAction={<button type="button">登记</button>}
      />,
    )
    const names = screen.getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? button.textContent)
    expect(names).toEqual(['口径说明', '导出', '更多', '登记'])
  })

  it('说明图标点开浮层，正文不占文档流', () => {
    renderIntl(<PageHeader title="出货统计" help={{ label: '口径说明', content: '按出库日期统计' }} />)
    expect(screen.queryByText('按出库日期统计')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '口径说明' }))
    expect(screen.getByText('按出库日期统计')).toBeInTheDocument()
  })

  it('开发态提醒：次按钮超过 2 个、描述超过 40 字', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderIntl(
      <PageHeader
        title="候选人"
        description={'一'.repeat(41)}
        secondaryActions={['甲', '乙', '丙'].map((label) => <button key={label} type="button">{label}</button>)}
      />,
    )
    expect(warn.mock.calls.map((call) => String(call[0]))).toEqual([
      expect.stringContaining('3 个次按钮'),
      expect.stringContaining('超过 40 字'),
    ])
    warn.mockRestore()
  })

  it('旧的 action / extra 仍按原样渲染', () => {
    render(<PageHeader title="旧页" extra={<button type="button">筛选</button>} action={<button type="button">新建</button>} />)
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['筛选', '新建'])
  })
})
