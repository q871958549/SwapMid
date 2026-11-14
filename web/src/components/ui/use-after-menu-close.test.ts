import { describe, expect, it, vi } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useAfterMenuClose } from './use-after-menu-close'

function closeEvent() {
  return new Event('focusScope.autoFocusOnUnmount', { cancelable: true })
}

describe('useAfterMenuClose', () => {
  it('登记的事在菜单关好时执行一次，并拦下这次焦点归还', () => {
    const { result } = renderHook(() => useAfterMenuClose())
    const action = vi.fn()
    result.current.schedule(action)
    expect(action).not.toHaveBeenCalled()

    const first = closeEvent()
    result.current.onCloseAutoFocus(first)
    expect(action).toHaveBeenCalledTimes(1)
    expect(first.defaultPrevented).toBe(true)

    // 下一次关菜单照常归还焦点。
    const second = closeEvent()
    result.current.onCloseAutoFocus(second)
    expect(action).toHaveBeenCalledTimes(1)
    expect(second.defaultPrevented).toBe(false)
  })
})
