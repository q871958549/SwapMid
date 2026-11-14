'use client'

import { useCallback, useRef, type ReactNode } from 'react'

/**
 * DataTable 数据单元格的截断包装：**保持单行截断，但让用户有途径看到全文**。
 *
 * 背景：Grid 层把非结构列的内容统一包成 `max-w-[400px] truncate`，这是虚拟滚动
 * 固定行高策略的刻意设计（行高恒定，滚动条才能 1:1 跟手），不是漏写。副作用是
 * 全站长文本（描述 / 摘要 / 错误信息）被单行截断且**没有任何查看全文的途径**。
 *
 * 设计取舍：
 * - **用原生 `title` 而不是 Radix Tooltip**。虚拟滚动下同屏可有几百个单元格，
 *   每个包一层 Tooltip 会带来可观的常驻实例开销；原生 title 零运行时成本。
 *   体验上需要富文本或延迟控制的重点列，另行单独升级，不在共享层统一加。
 * - **`mouseenter` 时才惰性检测**，不在渲染期测量：渲染期读 `scrollWidth` 会触发
 *   强制同步布局（layout thrashing），几百个单元格逐个读会明显掉帧。
 * - **检测要查后代**：嵌套块级子元素（如「显示名 + 描述 + 任务键」三行结构）的溢出
 *   发生在子 div 上，外层 `text-ellipsis` 对嵌套块不产生省略号，文本会戛然而止、
 *   连「被截断了」的暗示都没有。所以自身未溢出时还要遍历后代。
 * - **只在真溢出时设 title**：没溢出却挂 title 会让鼠标停留时冒出多余提示，反成噪声。
 *
 * jsdom 无布局引擎（`scrollWidth` 恒 0），单测只能覆盖「未溢出不设 title」分支；
 * 溢出分支需真实浏览器验证。
 */
export function TruncatedCell({ children, maxWidth }: {
  children: ReactNode
  /** 用户拖过列宽时按列宽截断，替代默认的 400px 上限：调窄才真的变窄，调宽也能多显示。 */
  maxWidth?: number
}) {
  const ref = useRef<HTMLDivElement>(null)

  const handleMouseEnter = useCallback(() => {
    const el = ref.current
    if (!el) return

    // 已经算过就不重复测量：内容变化时 React 会重建节点，title 随之丢弃并重新检测。
    if (el.dataset.overflowChecked === '1') return
    el.dataset.overflowChecked = '1'

    const overflowed = el.scrollWidth > el.clientWidth
      || Array.from(el.querySelectorAll<HTMLElement>('*')).some(child => child.scrollWidth > child.clientWidth)

    if (overflowed) {
      const text = el.textContent?.trim()
      if (text) el.title = text
    }
  }, [])

  return (
    <div ref={ref} className="max-w-[400px] truncate" style={maxWidth ? { maxWidth } : undefined} onMouseEnter={handleMouseEnter}>
      {children}
    </div>
  )
}
