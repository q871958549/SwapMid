import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render as rtlRender, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { ColorPicker, hexToHsv, hexToRgb, hsvToHex, normalizeHex, rgbToHex } from './color-picker'
import zhHansMessages from '../../../messages/zh-Hans.json'

function render(ui: React.ReactElement) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>,
  )
}

/** 打开取色弹层并返回触发按钮（可见文本即当前 hex）。 */
async function openPicker(hex: string) {
  const trigger = screen.getByRole('button', { name: `#${hex}` })
  fireEvent.click(trigger)
  await screen.findByLabelText('Hex')
  return trigger
}

describe('颜色纯函数', () => {
  it('normalizeHex：可带可不带 #，归一成大写无 #；非法输入返回 null', () => {
    expect(normalizeHex('ff0000')).toBe('FF0000')
    expect(normalizeHex('#a1B2c3')).toBe('A1B2C3')
    expect(normalizeHex(' #FF0000 ')).toBe('FF0000')
    expect(normalizeHex('FFF')).toBeNull()
    expect(normalizeHex('12345G')).toBeNull()
    expect(normalizeHex('')).toBeNull()
    expect(normalizeHex('#1234567')).toBeNull()
  })

  it('hex ↔ rgb 互转，rgbToHex 越界钳到 0–255', () => {
    expect(hexToRgb('00FF7F')).toEqual({ r: 0, g: 255, b: 127 })
    expect(rgbToHex(0, 255, 127)).toBe('00FF7F')
    expect(rgbToHex(-5, 300, 12.4)).toBe('00FF0C')
  })

  it('hex ↔ hsv 往返一致（含无彩色与边界色）', () => {
    for (const hex of ['FF0000', '00FF00', '0000FF', '336699', 'FFFFFF', '000000', 'ABCDEF']) {
      expect(hsvToHex(hexToHsv(hex))).toBe(hex)
    }
  })
})

describe('ColorPicker', () => {
  it('非法 hex 输入不上抛不崩溃，失焦后回退到上一次有效值', async () => {
    const onChange = vi.fn()
    render(<ColorPicker value="FF0000" onChange={onChange} />)
    await openPicker('FF0000')

    const hexInput = screen.getByLabelText('Hex')
    fireEvent.change(hexInput, { target: { value: 'zzz' } })
    expect(onChange).not.toHaveBeenCalled()
    fireEvent.blur(hexInput)
    expect(hexInput).toHaveValue('FF0000')
  })

  it('hex 输入合法值（小写带 #）即上抛大写无 # 的归一值', async () => {
    const onChange = vi.fn()
    render(<ColorPicker value="FF0000" onChange={onChange} />)
    await openPicker('FF0000')
    fireEvent.change(screen.getByLabelText('Hex'), { target: { value: '#00ff00' } })
    expect(onChange).toHaveBeenCalledWith('00FF00')
  })

  it('RGB 单通道修改上抛合成色；越界/非整数不上抛', async () => {
    const onChange = vi.fn()
    render(<ColorPicker value="FF0000" onChange={onChange} />)
    await openPicker('FF0000')

    fireEvent.change(screen.getByLabelText('R'), { target: { value: '300' } })
    fireEvent.change(screen.getByLabelText('R'), { target: { value: '12.5' } })
    expect(onChange).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('R'), { target: { value: '128' } })
    expect(onChange).toHaveBeenCalledWith('800000')
  })

  it('jsdom 无 EyeDropper 时不渲染拾取按钮', async () => {
    render(<ColorPicker value="FF0000" onChange={vi.fn()} />)
    await openPicker('FF0000')
    expect(screen.queryByLabelText('从屏幕拾取颜色')).not.toBeInTheDocument()
  })

  it('EyeDropper 可用时点击拾取按钮上抛所选色', async () => {
    vi.stubGlobal(
      'EyeDropper',
      class {
        open() {
          return Promise.resolve({ sRGBHex: '#123456' })
        }
      },
    )
    try {
      const onChange = vi.fn()
      render(<ColorPicker value="FF0000" onChange={onChange} />)
      await openPicker('FF0000')
      fireEvent.click(screen.getByLabelText('从屏幕拾取颜色'))
      await waitFor(() => expect(onChange).toHaveBeenCalledWith('123456'))
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('饱和度/明度面板：指针点击按位置换算 s/v', async () => {
    const onChange = vi.fn()
    render(<ColorPicker value="808080" onChange={onChange} />)
    await openPicker('808080')

    const panel = screen.getByLabelText('饱和度与明度')
    vi.spyOn(panel, 'getBoundingClientRect').mockReturnValue({
      x: 0, y: 0, left: 0, top: 0, right: 200, bottom: 100, width: 200, height: 100,
      toJSON: () => ({}),
    } as DOMRect)

    // 右上角 = 饱和度 100%、明度 100%；灰色的色相为 0 → 纯红
    fireEvent.pointerDown(panel, { clientX: 200, clientY: 0, buttons: 1, pointerId: 1 })
    expect(onChange).toHaveBeenCalledWith('FF0000')
  })

  it('饱和度/明度面板：键盘方向键微调明度', async () => {
    const onChange = vi.fn()
    render(<ColorPicker value="808080" onChange={onChange} />)
    await openPicker('808080')
    // ArrowUp 提升明度 2%（0x80=128/255≈50.2% → 52.2% ≈ 133 = 0x85）
    fireEvent.keyDown(screen.getByLabelText('饱和度与明度'), { key: 'ArrowUp' })
    expect(onChange).toHaveBeenCalledWith('858585')
  })

  it('色相滑条键盘拨动上抛新色相合成色', async () => {
    const onChange = vi.fn()
    render(<ColorPicker value="FF0000" onChange={onChange} />)
    await openPicker('FF0000')
    fireEvent.keyDown(screen.getByLabelText('色相'), { key: 'ArrowRight' })
    expect(onChange).toHaveBeenCalledWith('FF0400')
  })

  it('外部 value 变化时同步显示（render-adjust）', () => {
    const onChange = vi.fn()
    const { rerender } = render(<ColorPicker value="FF0000" onChange={onChange} />)
    expect(screen.getByRole('button', { name: '#FF0000' })).toBeInTheDocument()
    rerender(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <ColorPicker value="00ff00" onChange={onChange} />
      </NextIntlClientProvider>,
    )
    expect(screen.getByRole('button', { name: '#00FF00' })).toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
  })
})
