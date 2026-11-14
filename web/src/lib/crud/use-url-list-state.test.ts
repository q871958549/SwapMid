import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import {
  useUrlListState, hasActiveRegisteredFilters, subscribeListFilters, resetRegisteredListFilters,
} from './use-url-list-state'
import { patchListUrl } from './list-url'

// 只验数据层：state 更新 + 是否把 patch 转交 patchListUrl（后者的 URL 写入逻辑由 list-url.test.ts 覆盖）。
vi.mock('./list-url', () => ({ patchListUrl: vi.fn() }))
const patchMock = vi.mocked(patchListUrl)

beforeEach(() => vi.clearAllMocks())

describe('useUrlListState', () => {
  it('初值来自 initial（page.tsx 下传）', () => {
    const { result } = renderHook(() => useUrlListState({ status: 'a', dept: 'x' }))
    expect(result.current[0]).toEqual({ status: 'a', dept: 'x' })
  })

  it('set 增量更新 state 并把该 patch 转交 patchListUrl', () => {
    const { result } = renderHook(() => useUrlListState({ status: 'a', dept: 'x' }))
    act(() => result.current[1]({ status: 'b' }))
    expect(result.current[0]).toEqual({ status: 'b', dept: 'x' })
    expect(patchMock).toHaveBeenCalledWith({ status: 'b' })
  })

  it('多次 set 各自转交对应 patch', () => {
    const { result } = renderHook(() => useUrlListState({ status: '', dept: '' }))
    act(() => result.current[1]({ dept: 'd1' }))
    act(() => result.current[1]({ status: 's1' }))
    expect(result.current[0]).toEqual({ status: 's1', dept: 'd1' })
    expect(patchMock).toHaveBeenNthCalledWith(1, { dept: 'd1' })
    expect(patchMock).toHaveBeenNthCalledWith(2, { status: 's1' })
  })
})

// 蓝图 §7.3 第 1 条：hasActiveRegisteredFilters 必须在 set() 内同步反映最新值（不能靠 effect
// 事后纠正），否则「set 之后、effect 之前」的那次读取会拿到旧快照且无再渲染来修正。
describe('useUrlListState · 注册表（hasActiveRegisteredFilters / subscribeListFilters）', () => {
  it('挂载即全为空串时 hasActiveRegisteredFilters 为 false', () => {
    const { unmount } = renderHook(() => useUrlListState({ status: '', dept: '' }))
    expect(hasActiveRegisteredFilters()).toBe(false)
    unmount()
  })

  it('set 非空值后同步（无需等待下一次渲染）反映为 true', () => {
    const { result, unmount } = renderHook(() => useUrlListState({ status: '', dept: '' }))
    act(() => result.current[1]({ status: 'active' }))
    expect(hasActiveRegisteredFilters()).toBe(true)
    unmount()
  })

  it('resetRegisteredListFilters（useCrudResource.resetView 调用它）后归 false', () => {
    const { result, unmount } = renderHook(() => useUrlListState({ status: '', dept: '' }))
    act(() => result.current[1]({ status: 'active' }))
    expect(hasActiveRegisteredFilters()).toBe(true)

    act(() => resetRegisteredListFilters())
    expect(hasActiveRegisteredFilters()).toBe(false)
    expect(result.current[0]).toEqual({ status: '', dept: '' })
    unmount()
  })

  it('卸载后自动注销：hasActiveRegisteredFilters 不再计入该实例', () => {
    const { result, unmount } = renderHook(() => useUrlListState({ status: '', dept: '' }))
    act(() => result.current[1]({ status: 'active' }))
    expect(hasActiveRegisteredFilters()).toBe(true)

    unmount()
    expect(hasActiveRegisteredFilters()).toBe(false)
  })

  it('subscribeListFilters：set 时通知订阅者，取消订阅后不再收到通知', () => {
    const listener = vi.fn()
    const { result, unmount } = renderHook(() => useUrlListState({ status: '' }))
    const unsubscribe = subscribeListFilters(listener)

    act(() => result.current[1]({ status: 'active' }))
    expect(listener).toHaveBeenCalled()

    listener.mockClear()
    unsubscribe()
    act(() => result.current[1]({ status: '' }))
    expect(listener).not.toHaveBeenCalled()

    unmount()
  })
})
