'use client'

import { useRef } from 'react'

/**
 * 菜单项要打开另一个浮层（Popover、Dialog）或把焦点挪到别处时用。
 *
 * Radix 关菜单时，会在关闭动画放完、内容卸载的那一刻把焦点还给触发按钮。浮层要是先开了，
 * 这一下焦点就落到浮层外面，非模态浮层会当成「点到外面」立刻关掉——菜单项里写
 * `setTimeout(() => setOpen(true), 0)` 赶不上动画，而 jsdom 没有动画，单测也测不出来。
 * 所以选中时只登记要做的事，到菜单内容的 `onCloseAutoFocus` 拦下焦点归还后再做。
 *
 * ```tsx
 * const afterClose = useAfterMenuClose()
 * <DropdownMenuContent onCloseAutoFocus={afterClose.onCloseAutoFocus}>
 *   <DropdownMenuItem onSelect={() => afterClose.schedule(() => setPanelOpen(true))}>…</DropdownMenuItem>
 * </DropdownMenuContent>
 * ```
 */
export function useAfterMenuClose() {
  const pending = useRef<(() => void) | null>(null)
  return {
    /** 登记菜单关好后要做的事；它取代这一次的焦点归还。 */
    schedule: (action: () => void) => { pending.current = action },
    onCloseAutoFocus: (event: Event) => {
      const action = pending.current
      if (!action) return
      pending.current = null
      event.preventDefault()
      action()
    },
  }
}
