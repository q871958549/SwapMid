import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../messages/zh-Hans.json'
import { FilterBar } from './filter-bar'

function renderWithIntl(ui: React.ReactElement) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      {ui}
    </NextIntlClientProvider>,
  )
}

describe('FilterBar', () => {
  it('渲染行内筛选项、已生效计数徽标、Popover 更多筛选及条件胶囊清除', () => {
    const onClear = vi.fn()
    const onRemoveChip = vi.fn()

    renderWithIntl(
      <FilterBar
        activeCount={2}
        onClear={onClear}
        moreFilters={<div>低频筛选字段</div>}
        chips={[{ key: 'status', label: '状态：已审核', onRemove: onRemoveChip }]}
        actions={<button type="button">查询</button>}
      >
        <input aria-label="关键字" />
      </FilterBar>,
    )

    expect(screen.getByLabelText('关键字')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '查询' })).toBeInTheDocument()

    // 浮层默认未打开，不占页面文档流
    expect(screen.queryByText('低频筛选字段')).not.toBeInTheDocument()
    const trigger = screen.getByRole('button', { name: /筛选/ })
    expect(trigger).toHaveTextContent('2')

    // 点开更多筛选并点击重置
    fireEvent.click(trigger)
    expect(screen.getByText('低频筛选字段')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '重置' }))
    expect(onClear).toHaveBeenCalledOnce()

    // 点击条件胶囊移除单项
    fireEvent.click(screen.getByRole('button', { name: '清除 状态：已审核' }))
    expect(onRemoveChip).toHaveBeenCalledOnce()
  })

  it('无生效高级筛选时不显示数字徽标与重置按钮', () => {
    renderWithIntl(
      <FilterBar activeCount={0} onClear={() => {}} moreFilters={<div>高级条件</div>}>
        <span>行内项</span>
      </FilterBar>,
    )

    const trigger = screen.getByRole('button', { name: '筛选' })
    expect(trigger).toHaveTextContent(/^筛选$/)
    fireEvent.click(trigger)
    expect(screen.getByText('高级条件')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '重置' })).not.toBeInTheDocument()
  })
})
