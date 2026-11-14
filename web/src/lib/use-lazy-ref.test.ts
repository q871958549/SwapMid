import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useLazyRef } from './use-lazy-ref'

function response(body: unknown, init: { ok?: boolean; status?: number } = {}): Response {
  return {
    ok: init.ok ?? true,
    status: init.status ?? 200,
    json: vi.fn().mockResolvedValue(body),
  } as unknown as Response
}

describe('useLazyRef', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('同一路径并发与后续 ensure 复用一次成功请求', async () => {
    const fetchMock = vi.mocked(fetch)
      .mockResolvedValue(response({ items: [{ id: 'enterprise-1' }] }))
    const { result } = renderHook(() => useLazyRef<{ id: string }>('enterprises'))

    let first!: Promise<{ id: string }[]>
    let second!: Promise<{ id: string }[]>
    await act(async () => {
      first = result.current.ensure()
      second = result.current.ensure()
      await first
    })

    expect(first).toBe(second)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(result.current.items).toEqual([{ id: 'enterprise-1' }])
    await expect(result.current.ensure()).resolves.toEqual([{ id: 'enterprise-1' }])
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('HTTP 非 2xx 不缓存为空成功，下次 ensure 会重试', async () => {
    const fetchMock = vi.mocked(fetch)
      .mockResolvedValueOnce(response({}, { ok: false, status: 503 }))
      .mockResolvedValueOnce(response({ items: [{ id: 'department-1' }] }))
    const { result } = renderHook(() => useLazyRef<{ id: string }>('departments'))

    await expect(result.current.ensure()).resolves.toEqual([])
    await act(async () => {
      await expect(result.current.ensure()).resolves.toEqual([{ id: 'department-1' }])
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(result.current.items).toEqual([{ id: 'department-1' }])
  })

  it('畸形成功响应不缓存，下次 ensure 会重试', async () => {
    const fetchMock = vi.mocked(fetch)
      .mockResolvedValueOnce(response({ data: [] }))
      .mockResolvedValueOnce(response({ items: [{ id: 'position-1' }] }))
    const { result } = renderHook(() => useLazyRef<{ id: string }>('positions'))

    await expect(result.current.ensure()).resolves.toEqual([])
    await act(async () => {
      await result.current.ensure()
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(result.current.items).toEqual([{ id: 'position-1' }])
  })

  it('path 变化后发起新请求，不复用旧路径的缓存', async () => {
    const fetchMock = vi.mocked(fetch)
      .mockResolvedValueOnce(response({ items: [{ id: 'room-1' }] }))
      .mockResolvedValueOnce(response({ items: [{ id: 'room-2' }] }))
    const { result, rerender } = renderHook(
      ({ path }) => useLazyRef<{ id: string }>(path),
      { initialProps: { path: 'rooms?building=A' } },
    )

    await act(async () => {
      await result.current.ensure()
    })
    rerender({ path: 'rooms?building=B' })
    await act(async () => {
      await result.current.ensure()
    })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/proxy/rooms?building=A')
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/proxy/rooms?building=B')
    expect(result.current.items).toEqual([{ id: 'room-2' }])
  })

  it('invalidate 保留 last-known-good，并让下一次 ensure 重取', async () => {
    const fetchMock = vi.mocked(fetch)
      .mockResolvedValueOnce(response({ items: [{ id: 'employee-old' }] }))
      .mockResolvedValueOnce(response({ items: [{ id: 'employee-new' }] }))
    const { result } = renderHook(() => useLazyRef<{ id: string }>('employees'))

    await act(async () => {
      await result.current.ensure()
    })
    act(() => result.current.invalidate())

    expect(result.current.items).toEqual([{ id: 'employee-old' }])
    await act(async () => {
      await result.current.ensure()
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(result.current.items).toEqual([{ id: 'employee-new' }])
  })

  it('invalidate 后旧的在途响应不得覆盖新快照', async () => {
    let resolveOld!: (value: Response) => void
    const oldRequest = new Promise<Response>((resolve) => { resolveOld = resolve })
    const fetchMock = vi.mocked(fetch)
      .mockReturnValueOnce(oldRequest)
      .mockResolvedValueOnce(response({ items: [{ id: 'room-new' }] }))
    const { result } = renderHook(() => useLazyRef<{ id: string }>('rooms'))

    const stale = result.current.ensure()
    act(() => result.current.invalidate())
    await act(async () => {
      await result.current.ensure()
    })
    await act(async () => {
      resolveOld(response({ items: [{ id: 'room-old' }] }))
      await stale
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(result.current.items).toEqual([{ id: 'room-new' }])
  })

  it('成功缓存超过 TTL 后惰性重取，刷新失败仍保留 last-known-good', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-12T00:00:00Z'))
    const fetchMock = vi.mocked(fetch)
      .mockResolvedValueOnce(response({ items: [{ id: 'enterprise-known' }] }))
      .mockResolvedValueOnce(response({}, { ok: false, status: 503 }))
    const { result } = renderHook(() => useLazyRef<{ id: string }>('enterprises'))

    await act(async () => {
      await result.current.ensure()
    })
    vi.advanceTimersByTime(5 * 60 * 1000 + 1)
    await act(async () => {
      await expect(result.current.ensure()).resolves.toEqual([])
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(result.current.items).toEqual([{ id: 'enterprise-known' }])
  })
})
