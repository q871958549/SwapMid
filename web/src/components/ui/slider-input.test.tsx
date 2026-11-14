import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { SliderInput, formatSliderValue, toSliderValue } from './slider-input'

describe('滑块数值纯函数', () => {
  it('formatSliderValue：按 step 精度收敛并去尾零', () => {
    expect(formatSliderValue(40.300000000000004, 0.1)).toBe('40.3')
    expect(formatSliderValue(2, 0.25)).toBe('2')
    expect(formatSliderValue(2.25, 0.25)).toBe('2.25')
    expect(formatSliderValue(14, 0.5)).toBe('14')
  })

  it('toSliderValue：非法/空输入退回 min，超界钳到边界', () => {
    expect(toSliderValue('', 5, 500)).toBe(5)
    expect(toSliderValue('abc', 5, 500)).toBe(5)
    expect(toSliderValue('600', 5, 500)).toBe(500)
    expect(toSliderValue('1', 5, 500)).toBe(5)
    expect(toSliderValue('40.3', 5, 500)).toBe(40.3)
  })
})

describe('SliderInput', () => {
  const baseProps = {
    id: 'test-width',
    label: '宽度（mm）',
    thumbLabel: '宽度（mm）（滑块）',
    min: 5,
    max: 500,
    step: 0.1,
    onChange: vi.fn(),
  }

  it('数字输入原样上抛（含清空的中间态），滑块显示钳制值', () => {
    const onChange = vi.fn()
    const { rerender } = render(<SliderInput {...baseProps} value="40" onChange={onChange} />)
    fireEvent.change(screen.getByLabelText('宽度（mm）'), { target: { value: '600' } })
    expect(onChange).toHaveBeenCalledWith('600')

    // 超界表单值：数字框保留原文，滑块钳到 max
    rerender(<SliderInput {...baseProps} value="600" onChange={onChange} />)
    expect(screen.getByLabelText('宽度（mm）')).toHaveValue(600)
    expect(screen.getByLabelText('宽度（mm）（滑块）')).toHaveAttribute('aria-valuenow', '500')

    // 清空的中间态不产生 NaN：滑块退回 min
    rerender(<SliderInput {...baseProps} value="" onChange={onChange} />)
    expect(screen.getByLabelText('宽度（mm）（滑块）')).toHaveAttribute('aria-valuenow', '5')
  })

  it('键盘拨动滑块按 step 上抛格式化字符串', () => {
    const onChange = vi.fn()
    render(<SliderInput {...baseProps} value="40" onChange={onChange} />)
    fireEvent.keyDown(screen.getByLabelText('宽度（mm）（滑块）'), { key: 'ArrowRight' })
    expect(onChange).toHaveBeenCalledWith('40.1')
  })

  it('disabled 透传到滑块与数字框', () => {
    render(<SliderInput {...baseProps} value="40" disabled />)
    expect(screen.getByLabelText('宽度（mm）')).toBeDisabled()
    expect(screen.getByLabelText('宽度（mm）（滑块）')).toHaveAttribute('data-disabled')
  })
})
