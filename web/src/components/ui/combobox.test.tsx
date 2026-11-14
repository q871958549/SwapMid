import { describe, it, expect, vi } from 'vitest'
import { render as rtlRender, screen, fireEvent } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { Combobox, type ComboboxOption } from './combobox'
import zhHansMessages from '../../../messages/zh-Hans.json'

/**
 * ⚠️ jsdom 局限：jsdom 不做 CSS hit-testing，shadcn Button 基类的
 * `[&_svg]:pointer-events-none` 在 jsdom 里不生效——fireEvent 对旧版死代码
 * （清空钮曾是嵌在 trigger Button 内、靠该 CSS 规则被"截胡"指针事件的裸 svg）
 * 照样能触发，旧 bug 在 jsdom 里复现不出来，这里不写「复现旧 bug」的假测试。
 * 本文件只守新结构（清空钮挪到 trigger 外的独立 <button>）的交互协议：
 * 清空钮存在性 / onChange 语义 / 不触发展开，pointer-events 这层真实
 * 截胡效果只能真浏览器验证（见施工单第 4 节）。
 *
 * Combobox 的默认占位符/空态文案（i18n 批次3c）经 useTranslations('Common') 求值，
 * 组件树需要 NextIntlClientProvider 才能渲染；真实 zh-Hans messages 直供，与生产一致。
 */
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

const options: ComboboxOption[] = [
  { value: 'a', label: '选项 A' },
  { value: 'b', label: '选项 B' },
]

describe('Combobox 清空按钮', () => {
  it('已选值 + clearable → 渲染「清空已选」按钮，点击后 onChange 传空串一次且不展开下拉', async () => {
    const onChange = vi.fn()
    render(<Combobox options={options} value="a" onChange={onChange} clearable />)

    const clearBtn = screen.getByRole('button', { name: '清空已选' })
    fireEvent.click(clearBtn)

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('')
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('选项 B')).not.toBeInTheDocument()
  })

  it('清空钮位于 trigger 之外(防塞回 Button 内被 [&_svg]:pointer-events-none 复活死代码)', () => {
    render(<Combobox options={options} value="a" onChange={vi.fn()} clearable />)
    const trigger = screen.getByRole('combobox')
    const clearBtn = screen.getByRole('button', { name: '清空已选' })
    expect(trigger.contains(clearBtn)).toBe(false)
  })

  it('点击清空后焦点归还触发器(清空钮随即卸载,防焦点丢到 body)', () => {
    render(<Combobox options={options} value="a" onChange={vi.fn()} clearable />)
    fireEvent.click(screen.getByRole('button', { name: '清空已选' }))
    expect(screen.getByRole('combobox')).toHaveFocus()
  })

  it('未选值时不渲染清空按钮', () => {
    render(<Combobox options={options} onChange={vi.fn()} clearable />)
    expect(screen.queryByRole('button', { name: '清空已选' })).not.toBeInTheDocument()
  })

  it('clearable=false 时即使已选值也不渲染清空按钮', () => {
    render(<Combobox options={options} value="a" onChange={vi.fn()} />)
    expect(screen.queryByRole('button', { name: '清空已选' })).not.toBeInTheDocument()
  })

  it('disabled 时即使已选值也不渲染清空按钮', () => {
    render(<Combobox options={options} value="a" onChange={vi.fn()} clearable disabled />)
    expect(screen.queryByRole('button', { name: '清空已选' })).not.toBeInTheDocument()
  })

  it('点击 trigger 本体正常展开下拉', async () => {
    render(<Combobox options={options} value="a" onChange={vi.fn()} clearable />)
    const trigger = screen.getByRole('combobox')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(trigger)

    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(await screen.findByText('选项 B')).toBeInTheDocument()
  })
})

describe('Combobox 搜索框', () => {
  it('searchThreshold=0 时即使选项很少也显示搜索', async () => {
    render(<Combobox options={options} searchThreshold={0} searchPlaceholder="搜索岗位..." />)
    fireEvent.click(screen.getByRole('combobox'))
    expect(await screen.findByPlaceholderText('搜索岗位...')).toBeInTheDocument()
  })

  it('默认阈值下少量选项不显示搜索', async () => {
    render(<Combobox options={options} />)
    fireEvent.click(screen.getByRole('combobox'))
    expect(await screen.findByText('选项 B')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: /搜索/ })).not.toBeInTheDocument()
    expect(screen.queryByPlaceholderText('搜索...')).not.toBeInTheDocument()
  })
})
