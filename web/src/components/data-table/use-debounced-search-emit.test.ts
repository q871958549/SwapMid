import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useDebouncedSearchEmit } from './use-debounced-search-emit'

beforeEach(() => {
  vi.useFakeTimers()
})
afterEach(() => {
  vi.useRealTimers()
})

describe('useDebouncedSearchEmit', () => {
  it('manualPagination 关闭时，搜索词变化不回调（客户端分页由前端切片）', () => {
    const onSearchChange = vi.fn()
    const { rerender } = renderHook(
      ({ q }: { q: string }) => useDebouncedSearchEmit(q, false, onSearchChange),
      { initialProps: { q: '' } },
    )

    rerender({ q: '张三' })
    act(() => vi.advanceTimersByTime(500))

    expect(onSearchChange).not.toHaveBeenCalled()
  })

  it('挂载首帧不触发（避免无谓重取）', () => {
    const onSearchChange = vi.fn()
    renderHook(() => useDebouncedSearchEmit('init', true, onSearchChange))

    act(() => vi.advanceTimersByTime(500))

    expect(onSearchChange).not.toHaveBeenCalled()
  })

  it('搜索词变化经 300ms 防抖后回调最新值', () => {
    const onSearchChange = vi.fn()
    const { rerender } = renderHook(
      ({ q }: { q: string }) => useDebouncedSearchEmit(q, true, onSearchChange),
      { initialProps: { q: '' } },
    )

    rerender({ q: '张' })
    act(() => vi.advanceTimersByTime(299))
    expect(onSearchChange).not.toHaveBeenCalled()

    act(() => vi.advanceTimersByTime(1))
    expect(onSearchChange).toHaveBeenCalledTimes(1)
    expect(onSearchChange).toHaveBeenCalledWith('张')
  })

  it('防抖窗口内连续输入只触发最后一次', () => {
    const onSearchChange = vi.fn()
    const { rerender } = renderHook(
      ({ q }: { q: string }) => useDebouncedSearchEmit(q, true, onSearchChange),
      { initialProps: { q: '' } },
    )

    rerender({ q: '张' })
    act(() => vi.advanceTimersByTime(200))
    rerender({ q: '张三' }) // 清掉上一个 timer、重开防抖
    act(() => vi.advanceTimersByTime(200))
    expect(onSearchChange).not.toHaveBeenCalled()

    act(() => vi.advanceTimersByTime(100))
    expect(onSearchChange).toHaveBeenCalledTimes(1)
    expect(onSearchChange).toHaveBeenCalledWith('张三')
  })

  it('onSearchChange 引用变化时用最新回调、且不重启防抖', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { rerender } = renderHook(
      ({ q, cb }: { q: string; cb: (v: string) => void }) => useDebouncedSearchEmit(q, true, cb),
      { initialProps: { q: '', cb: first } },
    )

    rerender({ q: 'x', cb: first }) // 启动防抖
    act(() => vi.advanceTimersByTime(200))
    rerender({ q: 'x', cb: second }) // 仅换回调，q 不变 → 防抖 effect 依赖未变、timer 不重启
    act(() => vi.advanceTimersByTime(100)) // 累计 300ms 触发

    expect(first).not.toHaveBeenCalled()
    expect(second).toHaveBeenCalledTimes(1)
    expect(second).toHaveBeenCalledWith('x')
  })
})
