import { Children, Fragment, isValidElement, type ReactNode } from 'react'

/** 工具栏可见文字按钮上限，含「更多」本身。搜索框和选中条不计入。 */
export const TOOLBAR_VISIBLE_LIMIT = 4

export interface ToolbarSlot {
  id: string
  /** 越大越优先留在外面。 */
  priority: number
}

/**
 * 把内置动作分成「留在外面」和「收进更多」。
 * `extraSlots` 是页面 `toolbarExtra` 已经占掉的按钮数，优先保留，不拆开。
 * 一旦需要收纳，「更多」自己占 1 个名额。
 * 页面额外按钮已经不少于上限时，内置动作全部收进「更多」（可见数可能仍超过上限，页面按钮不隐藏）。
 */
export function partitionToolbarSlots<T extends ToolbarSlot>(
  slots: T[],
  extraSlots: number,
): { visible: T[]; overflow: T[] } {
  const extras = Math.max(0, extraSlots)
  if (extras + slots.length <= TOOLBAR_VISIBLE_LIMIT) {
    return { visible: slots, overflow: [] }
  }
  const ranked = [...slots].sort((a, b) => b.priority - a.priority)
  const keep = Math.max(0, TOOLBAR_VISIBLE_LIMIT - extras - 1)
  const keepIds = new Set(ranked.slice(0, keep).map((slot) => slot.id))
  return {
    visible: slots.filter((slot) => keepIds.has(slot.id)),
    overflow: slots.filter((slot) => !keepIds.has(slot.id)),
  }
}

/** 顶层节点各算 1 个按钮；Fragment 展开。包在 div 里的一组算 1，调用方应直接并排传入。 */
export function countToolbarExtraSlots(node: ReactNode): number {
  let count = 0
  Children.forEach(node, (child) => {
    if (child == null || typeof child === 'boolean') return
    if (isValidElement(child) && child.type === Fragment) {
      count += countToolbarExtraSlots((child.props as { children?: ReactNode }).children)
      return
    }
    count += 1
  })
  return count
}
