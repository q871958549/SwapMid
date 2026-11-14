import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { TruncatedCell } from './truncated-cell'

/**
 * ⚠️ jsdom 没有布局引擎，`scrollWidth` / `clientWidth` 恒为 0，所以「真溢出 → 设 title」
 * 那条分支在此层测不了（0 > 0 为 false），只能靠真实浏览器验证。
 * 这里覆盖的是可测的部分：不溢出时不留多余 title、检测只跑一次、内容正常渲染。
 * 溢出分支通过给节点打桩 scrollWidth 来验证，见最后一例。
 */
describe('TruncatedCell', () => {
  it('渲染子内容并保持单行截断类名', () => {
    const { container } = render(<TruncatedCell>一段文本</TruncatedCell>)
    const cell = container.firstElementChild as HTMLElement
    expect(cell).toHaveClass('truncate')
    expect(screen.getByText('一段文本')).toBeInTheDocument()
  })

  it('未溢出时不设 title（避免无谓的悬停提示）', () => {
    const { container } = render(<TruncatedCell>短文本</TruncatedCell>)
    const cell = container.firstElementChild as HTMLElement
    fireEvent.mouseEnter(cell)
    expect(cell.title).toBe('')
  })

  it('检测只跑一次：二次悬停不再重复测量', () => {
    const { container } = render(<TruncatedCell>短文本</TruncatedCell>)
    const cell = container.firstElementChild as HTMLElement
    fireEvent.mouseEnter(cell)
    expect(cell.dataset.overflowChecked).toBe('1')
    fireEvent.mouseEnter(cell)
    expect(cell.dataset.overflowChecked).toBe('1')
  })

  it('溢出时把纯文本内容写进 title', () => {
    const { container } = render(<TruncatedCell>很长很长的一段描述文本</TruncatedCell>)
    const cell = container.firstElementChild as HTMLElement
    // jsdom 无布局，手动打桩模拟「内容宽度超过可视宽度」
    Object.defineProperty(cell, 'scrollWidth', { value: 500, configurable: true })
    Object.defineProperty(cell, 'clientWidth', { value: 200, configurable: true })
    fireEvent.mouseEnter(cell)
    expect(cell.title).toBe('很长很长的一段描述文本')
  })

  it('自身未溢出但后代溢出时也补 title（嵌套块级结构不产生省略号）', () => {
    const { container } = render(
      <TruncatedCell><div><span>主标题</span><span>副标题</span></div></TruncatedCell>,
    )
    const cell = container.firstElementChild as HTMLElement
    const inner = cell.querySelector('div') as HTMLElement
    Object.defineProperty(inner, 'scrollWidth', { value: 500, configurable: true })
    Object.defineProperty(inner, 'clientWidth', { value: 200, configurable: true })
    fireEvent.mouseEnter(cell)
    expect(cell.title).toBe('主标题副标题')
  })
})
