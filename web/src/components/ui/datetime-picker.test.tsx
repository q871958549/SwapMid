import { fireEvent, render as rtlRender, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { describe, expect, it, vi } from 'vitest'
import zhHansMessages from '../../../messages/zh-Hans.json'
import { DateTimePicker } from './datetime-picker'

function render(ui: React.ReactElement) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      {ui}
    </NextIntlClientProvider>,
  )
}

describe('DateTimePicker', () => {
  it('标识传给输入框，业务标签可聚焦对应时间字段', () => {
    render(<><label htmlFor="leave-start">开始时间</label><DateTimePicker id="leave-start" value="2026-11-06T09:00" onChange={vi.fn()} /></>)
    expect(screen.getByLabelText('开始时间')).toHaveValue('2026-11-06 09:00')
  })
  it('弹层右下角是确认而不是现在，点确认才提交', () => {
    const onChange = vi.fn()
    render(<DateTimePicker value="2026-09-03T09:00" onChange={onChange} />)

    fireEvent.click(screen.getByRole('button', { name: '打开日历' }))
    expect(screen.queryByRole('button', { name: '现在' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '确认' }))

    expect(onChange).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: '确认' })).not.toBeInTheDocument()
  }, 15_000)
})
