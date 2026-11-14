import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useDictionary } from './use-dictionary'
import { notifyDictionaryChanged } from './dictionary-events'

afterEach(() => vi.unstubAllGlobals())
const response = (value: string) => ({ ok: true, json: async () => ({ items: [{ value, label: value, sortOrder: 1 }] }) }) as Response

describe('useDictionary实时选项', () => {
  it('维护后立即刷新且迟到的旧请求不能覆盖新选项', async () => {
    let resolveOld!: (value: Response) => void
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveOld = resolve }))
      .mockResolvedValueOnce(response('新增选项'))
    vi.stubGlobal('fetch', fetchMock)
    const { result, unmount } = renderHook(() => useDictionary('sales_currency'))
    act(() => notifyDictionaryChanged('sales_currency'))
    await waitFor(() => expect(result.current.items[0]?.value).toBe('新增选项'))
    await act(async () => { resolveOld(response('旧选项')); await Promise.resolve() })
    expect(result.current.items[0]?.value).toBe('新增选项')
    unmount()
    act(() => notifyDictionaryChanged('sales_currency'))
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('忽略其他字典变化且回到窗口后重新获取最新选项', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(response('美元')).mockResolvedValueOnce(response('港币'))
    vi.stubGlobal('fetch', fetchMock)
    const { result } = renderHook(() => useDictionary('sales_currency'))
    await waitFor(() => expect(result.current.items[0]?.label).toBe('美元'))
    act(() => notifyDictionaryChanged('sales_rec_condition'))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    act(() => window.dispatchEvent(new Event('focus')))
    await waitFor(() => expect(result.current.items[0]?.label).toBe('港币'))
  })
})
