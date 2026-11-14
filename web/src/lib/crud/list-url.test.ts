import { describe, it, expect, vi, beforeEach } from 'vitest'
import { patchListUrl } from './list-url'

// patchListUrl 会调 useTabStore.getState().updateActivePath 同步页签 path——mock 之，只断言被调用。
const { updateActivePath } = vi.hoisted(() => ({ updateActivePath: vi.fn() }))
vi.mock('@/stores/use-tab-store', () => ({
  useTabStore: { getState: () => ({ updateActivePath }) },
}))

beforeEach(() => {
  vi.clearAllMocks()
  window.history.replaceState(null, '', '/list')
})

describe('patchListUrl', () => {
  it('增量写入 query（shallow）并同步页签 path', () => {
    patchListUrl({ status: '0' })
    expect(window.location.pathname + window.location.search).toBe('/list?status=0')
    expect(updateActivePath).toHaveBeenCalledWith('/list?status=0')
  })

  it('保留已有 query，合并新键', () => {
    window.history.replaceState(null, '', '/list?a=1')
    patchListUrl({ b: '2' })
    expect(window.location.search).toBe('?a=1&b=2')
  })

  it('空串 / null / undefined 三种空值都从 URL 删除，未涉及的键保留', () => {
    window.history.replaceState(null, '', '/list?a=1&b=2&c=3&keep=9')
    patchListUrl({ a: '', b: null, c: undefined })
    expect(window.location.search).toBe('?keep=9')
  })

  it('清空全部键后地址栏无 `?`', () => {
    window.history.replaceState(null, '', '/list?a=1')
    patchListUrl({ a: '' })
    expect(window.location.pathname + window.location.search).toBe('/list')
    expect(updateActivePath).toHaveBeenCalledWith('/list')
  })

  it('数字值序列化为字符串', () => {
    patchListUrl({ page: 3, size: 20 })
    expect(window.location.search).toBe('?page=3&size=20')
  })
})
