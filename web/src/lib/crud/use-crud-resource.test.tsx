import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { ReactNode } from 'react'
import { renderHook as renderHookRTL, act, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import { type PagedResult } from '@/lib/api-client'
import { DEFAULT_EXPORT_MAX_ROWS, useCrudResource } from './use-crud-resource'
import { INTERACTIVE_LIST_MAX_PAGE_SIZE } from './list-pagination'
import { useUrlListState } from './use-url-list-state'
import { patchListUrl } from './list-url'
import zhHansMessages from '../../../messages/zh-Hans.json'

// useCrudResource 内取数失败 toast 的默认文案经 useTranslations('Common') 求值（i18n 批次4b前置），
// 组件树需要 NextIntlClientProvider 才能渲染；真实 zh-Hans messages 直供，与生产一致。
// 用同名遮蔽包一层 wrapper，断言零改动。
function renderHook<TProps, TResult>(
  callback: (props: TProps) => TResult,
  options?: Parameters<typeof renderHookRTL<TResult, TProps>>[1],
) {
  return renderHookRTL(callback, {
    wrapper: ({ children }) => (
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        {children}
      </NextIntlClientProvider>
    ),
    ...options,
  })
}

function AuthenticatedWrapper({ children }: { children: ReactNode }) {
  return (
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <SessionIdentityProvider identityVersion="identity-test" storagePrincipalId="principal-test">
        {children}
      </SessionIdentityProvider>
    </NextIntlClientProvider>
  )
}

vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))
// useCrudResource 用 useRouter().refresh 组合 reloadAndRefresh——固定 mock 以便断言。
const { refreshMock } = vi.hoisted(() => ({ refreshMock: vi.fn() }))
vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: refreshMock }) }))
// urlSync 会调 patchListUrl 写 URL——mock 之，只断言调用参数（写 URL 行为由 list-url.test.ts 覆盖）。
vi.mock('./list-url', () => ({ patchListUrl: vi.fn() }))
const patchMock = vi.mocked(patchListUrl)
// reload 失败路径会 console.error（预期）——静默以免污染输出。
vi.spyOn(console, 'error').mockImplementation(() => {})
// 原生下载必须留在同步点击链中；行为测试直接钉住 window.open 的调用时序与安全参数。
const { windowOpenMock } = vi.hoisted(() => ({ windowOpenMock: vi.fn() }))
vi.spyOn(window, 'open').mockImplementation(windowOpenMock)

const toastMock = vi.mocked(toast)

interface Emp {
  id: number
}

const emptyInitial: PagedResult<Emp> = { items: [], totalCount: 0 }

/** 桩 fetch：所有调用都返回同一份分页 JSON。 */
function stubJson(items: Emp[], totalCount: number) {
  const fetchMock = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ items, totalCount }) as PagedResult<Emp>,
  } as Response)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

function pagedResponse(items: Emp[], totalCount: number): Response {
  return {
    ok: true,
    json: async () => ({ items, totalCount }) as PagedResult<Emp>,
  } as Response
}

/** 第 n 次 fetch 的请求 URL。 */
function urlOf(fetchMock: ReturnType<typeof vi.fn>, n = 0): string {
  return fetchMock.mock.calls[n][0] as string
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('useCrudResource · 首屏', () => {
  it('首屏用 initial 渲染、不发请求（didMount 跳过首帧）', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: { items: [{ id: 1 }], totalCount: 1 } }),
    )

    expect(result.current.rows).toEqual([{ id: 1 }])
    expect(result.current.total).toBe(1)
    expect(result.current.page).toBe(0)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('pageSize 默认 15、可覆盖；tableProps 暴露 manual 模式字段', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: { items: [], totalCount: 42 } }),
    )
    expect(result.current.pageSize).toBe(15)
    expect(result.current.tableProps.manualPagination).toBe(true)
    expect(result.current.tableProps.rowCount).toBe(42)
    expect(result.current.tableProps.pageIndex).toBe(0)
    expect(result.current.tableProps.manualPageSize).toBe(15)

    const { result: r2 } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, pageSize: 20 }),
    )
    expect(r2.current.pageSize).toBe(20)
    expect(result.current.tableProps.maxPageSize).toBe(INTERACTIVE_LIST_MAX_PAGE_SIZE)
  })

  it('setPageSize 与初始 pageSize 默认钳到交互上限', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, pageSize: 1000 }),
    )
    expect(result.current.pageSize).toBe(INTERACTIVE_LIST_MAX_PAGE_SIZE)
    act(() => result.current.setPageSize(1000))
    expect(result.current.pageSize).toBe(INTERACTIVE_LIST_MAX_PAGE_SIZE)
  })

  it('pageSizeMax 覆盖默认上限并写入 tableProps', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        pageSize: 15,
        pageSizeMax: 500,
      }),
    )
    expect(result.current.tableProps.maxPageSize).toBe(500)
    act(() => result.current.setPageSize(1000))
    expect(result.current.pageSize).toBe(500)
  })
})

describe('useCrudResource · 翻页 / 搜索触发 reload', () => {
  it('setPage 触发 reload，skipCount=page*pageSize、maxResultCount=pageSize', async () => {
    const fetchMock = stubJson([{ id: 2 }], 30)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, pageSize: 15 }),
    )

    act(() => result.current.setPage(1))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const url = urlOf(fetchMock)
    expect(url).toContain('/api/proxy/employees?')
    expect(url).toContain('skipCount=15')
    expect(url).toContain('maxResultCount=15')
    await waitFor(() => expect(result.current.rows).toEqual([{ id: 2 }]))
    expect(result.current.total).toBe(30)
  })

  it('onPaginationChange 更新 page/pageSize 并按新值取数', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    act(() => result.current.tableProps.onPaginationChange({ pageIndex: 3, pageSize: 50 }))

    expect(result.current.page).toBe(3)
    expect(result.current.pageSize).toBe(50)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const url = urlOf(fetchMock)
    expect(url).toContain('skipCount=150') // 3 * 50
    expect(url).toContain('maxResultCount=50')
  })

  it('onSearchChange 注入 filter 且回到第 1 页（setPage(0) 语义）', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, pageSize: 15 }),
    )

    // 先翻到第 3 页
    act(() => result.current.setPage(2))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))

    // 搜索：应回第 1 页并带 filter
    act(() => result.current.tableProps.onSearchChange('张三'))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    expect(result.current.page).toBe(0)
    expect(result.current.search).toBe('张三')
    const url = urlOf(fetchMock, 1)
    expect(url).toContain('skipCount=0')
    expect(decodeURIComponent(url)).toContain('filter=张三')
  })

  it('搜索词全为空白时 trim 后不带 filter', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    act(() => result.current.setSearch('   '))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(urlOf(fetchMock)).not.toContain('filter=')
  })

  it('reload() 手动重取当前页（供写成功后回取，对应 onSuccess: list.reload）', async () => {
    const fetchMock = stubJson([{ id: 9 }], 1)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    await act(async () => {
      await result.current.reload()
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(result.current.rows).toEqual([{ id: 9 }])
    expect(result.current.total).toBe(1)
  })
})

describe('useCrudResource · 最新列表请求所有权', () => {
  it('新 reload 取消旧请求、向 fetch 传播 signal，旧响应晚到也不能覆盖最新结果', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    let firstInit: RequestInit | undefined
    let secondInit: RequestInit | undefined
    const fetchMock = vi
      .fn()
      .mockImplementationOnce((_url: string, init?: RequestInit) => {
        firstInit = init
        return first.promise
      })
      .mockImplementationOnce((_url: string, init?: RequestInit) => {
        secondInit = init
        return second.promise
      })
    vi.stubGlobal('fetch', fetchMock)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let firstReload!: Promise<void>
    let secondReload!: Promise<void>
    act(() => { firstReload = result.current.reload() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    act(() => { secondReload = result.current.reload() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    expect(firstInit?.signal).toBeInstanceOf(AbortSignal)
    expect(firstInit?.signal?.aborted).toBe(true)
    expect(secondInit?.signal).toBeInstanceOf(AbortSignal)
    expect(secondInit?.signal?.aborted).toBe(false)

    await act(async () => {
      second.resolve(pagedResponse([{ id: 2 }], 20))
      await secondReload
    })
    expect(result.current.rows).toEqual([{ id: 2 }])
    expect(result.current.total).toBe(20)

    // 模拟不理会 AbortSignal、仍然返回数据的 fetch/polyfill：序号门禁必须继续挡住旧响应。
    await act(async () => {
      first.resolve(pagedResponse([{ id: 1 }], 10))
      await firstReload
    })
    expect(result.current.rows).toEqual([{ id: 2 }])
    expect(result.current.total).toBe(20)
  })

  it('旧请求先结束时不能提前清除仍在进行的新请求 loading', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    const fetchMock = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)
    vi.stubGlobal('fetch', fetchMock)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let firstReload!: Promise<void>
    let secondReload!: Promise<void>
    act(() => { firstReload = result.current.reload() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    act(() => { secondReload = result.current.reload() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    await act(async () => {
      first.resolve(pagedResponse([{ id: 1 }], 1))
      await firstReload
    })
    expect(result.current.loading).toBe(true)
    expect(result.current.rows).toEqual([])

    await act(async () => {
      second.resolve(pagedResponse([{ id: 2 }], 1))
      await secondReload
    })
    expect(result.current.loading).toBe(false)
    expect(result.current.rows).toEqual([{ id: 2 }])
  })

  it('被后续 reload 替代的 reloadAndRefresh 静默完成，不记录旧错误或弹 toast', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    const fetchMock = vi
      .fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)
    vi.stubGlobal('fetch', fetchMock)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let superseded!: Promise<void>
    let latest!: Promise<void>
    act(() => { superseded = result.current.reloadAndRefresh() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    act(() => { latest = result.current.reload() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    await act(async () => {
      first.reject(new TypeError('stale network failure'))
      await expect(superseded).resolves.toBeUndefined()
    })
    expect(console.error).not.toHaveBeenCalled()
    expect(toastMock).not.toHaveBeenCalled()
    expect(refreshMock).toHaveBeenCalledTimes(1)
    expect(result.current.loading).toBe(true)

    await act(async () => {
      second.resolve(pagedResponse([{ id: 2 }], 1))
      await latest
    })
  })

  it('卸载时使请求失效并 abort；随后失败不会 reject、记录错误或弹 toast', async () => {
    const pendingResponse = deferred<Response>()
    let requestInit: RequestInit | undefined
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => {
      requestInit = init
      return pendingResponse.promise
    })
    vi.stubGlobal('fetch', fetchMock)
    const { result, unmount } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let reloadPromise!: Promise<void>
    act(() => { reloadPromise = result.current.reload() })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const reloadAfterUnmount = result.current.reload

    unmount()
    expect(requestInit?.signal?.aborted).toBe(true)
    pendingResponse.reject(new TypeError('late failure after unmount'))
    await expect(reloadPromise).resolves.toBeUndefined()
    await expect(reloadAfterUnmount()).resolves.toBeUndefined()
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(console.error).not.toHaveBeenCalled()
    expect(toastMock).not.toHaveBeenCalled()
  })

  it.each(['resolve', 'reject'] as const)(
    'reloadAndRefresh 进行中卸载后，即使请求随后 %s 也静默完成且不刷新新路由',
    async (settlement) => {
      const pendingResponse = deferred<Response>()
      let requestInit: RequestInit | undefined
      const fetchMock = vi.fn((_url: string, init?: RequestInit) => {
        requestInit = init
        return pendingResponse.promise
      })
      vi.stubGlobal('fetch', fetchMock)
      const { result, unmount } = renderHook(() =>
        useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
      )

      let reloadPromise!: Promise<void>
      act(() => { reloadPromise = result.current.reloadAndRefresh() })
      await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))

      unmount()
      expect(requestInit?.signal?.aborted).toBe(true)
      if (settlement === 'resolve') {
        pendingResponse.resolve(pagedResponse([{ id: 1 }], 1))
      } else {
        pendingResponse.reject(new TypeError('late failure after unmount'))
      }

      await expect(reloadPromise).resolves.toBeUndefined()
      expect(refreshMock).not.toHaveBeenCalled()
      expect(console.error).not.toHaveBeenCalled()
      expect(toastMock).not.toHaveBeenCalled()
    },
  )

  it('卸载后持有的 reloadAndRefresh 直接调用时不发请求也不刷新路由', async () => {
    const fetchMock = stubJson([{ id: 1 }], 1)
    const { result, unmount } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )
    const reloadAfterUnmount = result.current.reloadAndRefresh

    unmount()
    await expect(reloadAfterUnmount()).resolves.toBeUndefined()

    expect(fetchMock).not.toHaveBeenCalled()
    expect(refreshMock).not.toHaveBeenCalled()
  })
})

describe('useCrudResource · 服务端排序下推', () => {
  it('onSortingChange 序列化成 ABP Sorting 串并回第 1 页', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    // 先翻到第 3 页
    act(() => result.current.setPage(2))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))

    // 排序 name 降序 → 回第 1 页 + 带 Sorting=name desc
    act(() => result.current.tableProps.onSortingChange([{ id: 'name', desc: true }]))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    expect(result.current.page).toBe(0)
    const params = new URLSearchParams(urlOf(fetchMock, 1).split('?')[1])
    expect(params.get('skipCount')).toBe('0')
    expect(params.get('Sorting')).toBe('name desc')
  })

  it('多列排序逗号拼接；清空排序后请求不带 Sorting', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    act(() =>
      result.current.tableProps.onSortingChange([
        { id: 'status', desc: false },
        { id: 'employeeNo', desc: true },
      ]),
    )
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(new URLSearchParams(urlOf(fetchMock, 0).split('?')[1]).get('Sorting')).toBe('status asc,employeeNo desc')

    act(() => result.current.tableProps.onSortingChange([]))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(urlOf(fetchMock, 1)).not.toContain('Sorting')
  })
})

describe('useCrudResource · extraDeps / buildParams', () => {
  it('extraDeps 变化触发 reload，buildParams 注入页面级筛选', async () => {
    const fetchMock = stubJson([], 0)
    const { rerender } = renderHook(
      ({ status }: { status: string }) =>
        useCrudResource<Emp>({
          endpoint: 'employees',
          initial: emptyInitial,
          extraDeps: [status],
          buildParams: (qs) => {
            if (status) qs.set('status', status)
          },
        }),
      { initialProps: { status: '' } },
    )

    expect(fetchMock).not.toHaveBeenCalled() // 首屏不发

    rerender({ status: 'active' })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(urlOf(fetchMock)).toContain('status=active')
  })

  it('extraDeps 变化时若不在第 1 页，回第 1 页且只请求 skipCount=0 一次', async () => {
    const fetchMock = stubJson([], 0)
    const { result, rerender } = renderHook(
      ({ status }: { status: string }) =>
        useCrudResource<Emp>({
          endpoint: 'employees',
          initial: emptyInitial,
          extraDeps: [status],
          buildParams: (qs) => {
            if (status) qs.set('status', status)
          },
        }),
      { initialProps: { status: '' } },
    )

    act(() => result.current.setPage(2))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(urlOf(fetchMock)).toContain('skipCount=30')
    fetchMock.mockClear()

    rerender({ status: 'active' })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(result.current.page).toBe(0)
    expect(urlOf(fetchMock)).toContain('skipCount=0')
    expect(urlOf(fetchMock)).toContain('status=active')
  })

  it('首屏 initialPage 不因 extraDeps 初值被打回第 1 页', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        extraDeps: ['active'],
        initialPage: 2,
      }),
    )
    expect(result.current.page).toBe(2)
  })

  it('urlSync 开启时 extraDeps 回第 1 页会清 URL page 键', async () => {
    const fetchMock = stubJson([], 0)
    const { result, rerender } = renderHook(
      ({ status }: { status: string }) =>
        useCrudResource<Emp>({
          endpoint: 'employees',
          initial: emptyInitial,
          urlSync: true,
          extraDeps: [status],
        }),
      { initialProps: { status: '' } },
    )

    act(() => result.current.setPage(2))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    patchMock.mockClear()

    rerender({ status: 'active' })
    await waitFor(() => expect(result.current.page).toBe(0))
    expect(patchMock).toHaveBeenCalledWith({ page: '' })
  })
})

describe('useCrudResource · fetchAll（导出分块绕 ABP 1000 上限）', () => {
  it('1500 条分两块（1000 + 500）后再发一次空块请求', async () => {
    const page1 = Array.from({ length: 1000 }, (_, i) => ({ id: i }))
    const page2 = Array.from({ length: 500 }, (_, i) => ({ id: 1000 + i }))
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: page1, totalCount: 1500 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: page2, totalCount: 1500 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [], totalCount: 1500 }) } as Response)
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    expect(all).toHaveLength(1500)
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(urlOf(fetchMock, 0)).toContain('skipCount=0')
    expect(urlOf(fetchMock, 1)).toContain('skipCount=1000')
    expect(urlOf(fetchMock, 2)).toContain('skipCount=1500')
  })

  // 🔴 P0 回归（2026-08-03）：服务端把单页钳得比请求的更小时，导出必须仍然完整。
  // 这正是 B 批引入的真实缺陷——样品列表 ListMaxPageSize=500 < chunk=1000，于是**每一页都
  // 不足一 chunk**，旧判据 `items.length < chunk` 在第一页之后就停，「导出全部」永远只有 500 行、
  // 且无任何提示。本用例此前不存在：既有 mock 全都按请求页大小满额返回，钳制场景是测试盲区。
  it('服务端钳制单页（500 < chunk）时仍导出完整：skip 按实际返回递增', async () => {
    const page = (from: number, size: number) =>
      Array.from({ length: size }, (_, i) => ({ id: from + i }))
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: page(0, 500), totalCount: 1200 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: page(500, 500), totalCount: 1200 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: page(1000, 200), totalCount: 1200 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [], totalCount: 1200 }) } as Response)
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    expect(all).toHaveLength(1200)
    // skip 必须按**实际返回行数**递增；按固定 chunk 递增会跳过服务端没返回的那 500 行。
    expect(urlOf(fetchMock, 1)).toContain('skipCount=500')
    expect(urlOf(fetchMock, 2)).toContain('skipCount=1000')
    expect(urlOf(fetchMock, 3)).toContain('skipCount=1200')
  })

  // 终止条件只认「本块返回 0 行」（2026-08-03 改，见 use-crud-resource.ts 注释里的两次历史错误）。
  // 代价：末尾恒多一次返回空的请求（本用例断言的正是这个代价）。
  it('整除边界：2000 条分两满块后会再发一次空块请求', async () => {
    const fullChunk = () => Array.from({ length: 1000 }, (_, i) => ({ id: i }))
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: fullChunk(), totalCount: 2000 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: fullChunk(), totalCount: 2000 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [], totalCount: 2000 }) } as Response)
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    expect(all).toHaveLength(2000)
    expect(fetchMock).toHaveBeenCalledTimes(3) // 第 3 次返回空块才停
  })

  it('截断计数下仍导出完整：totalCount 被后端截断成 cap+1 也不提前停止', async () => {
    const fullChunk = () => Array.from({ length: 1000 }, (_, i) => ({ id: i }))
    // 模拟后端截断计数：实际 2500 行，totalCount 恒报 1001（cap=1000 时的 cap+1）。
    // 旧实现会在第 2 块后因 2000 >= 1001 停下，只导出 2000 行、丢掉 500 行且无任何提示。
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: fullChunk(), totalCount: 1001 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: fullChunk(), totalCount: 1001 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: fullChunk().slice(0, 500), totalCount: 1001 }) } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [], totalCount: 1001 }) } as Response)
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    expect(all).toHaveLength(2500)
    expect(fetchMock).toHaveBeenCalledTimes(4)
  })

  it('serverExportPath 设置时同步打开身份绑定的原生流：一行都不拉、只下发筛选参数', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'samples',
        initial: emptyInitial,
        serverExportPath: 'samples/export',
        initialSearch: '关键词',
        buildParams: (qs) => qs.set('projectId', 'P-1'),
      }),
      { wrapper: AuthenticatedWrapper },
    )

    const exportPromise = result.current.tableProps.onServerExport?.()

    // window.open 必须在第一个 await 之前同步发生，否则浏览器会丢失 user activation。
    expect(windowOpenMock).toHaveBeenCalledTimes(1)
    await act(async () => { await exportPromise })

    // 关键：fetch/blob 分页拉取一次都不发生——响应流直接进入浏览器下载器。
    expect(fetchMock).not.toHaveBeenCalled()
    const [url, target, features] = windowOpenMock.mock.calls[0] as [string, string, string]
    expect(url).toContain('/api/proxy/samples/export')
    expect(url).toContain('filter=')
    expect(url).toContain('projectId=P-1')
    const parsed = new URL(url, 'http://localhost')
    expect(parsed.searchParams.getAll('_swapmidIdentityVersion')).toEqual(['identity-test'])
    expect(target).toBe('_blank')
    expect(features).toBe('noopener,noreferrer')
    // 导出是全量且服务端固定排序：不得下发分页与排序参数。
    expect(url).not.toContain('skipCount')
    expect(url).not.toContain('maxResultCount')
    expect(url).not.toContain('Sorting')
  })

  it('未设 serverExportPath 时不下发 onServerExport（既有页面保持前端导出）', () => {
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    expect(result.current.tableProps.onServerExport).toBeUndefined()
  })

  it('serverExportPath 脱离身份 Provider 时只在导出动作 fail-close，不影响共享 CRUD 渲染', async () => {
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'samples', initial: emptyInitial, serverExportPath: 'samples/export' }),
    )

    await expect(result.current.tableProps.onServerExport?.()).rejects.toThrow(
      'SessionIdentityProvider is required for server export',
    )
    expect(windowOpenMock).not.toHaveBeenCalled()
  })

  it('exportMaxRows 达上限即停并截断到上限', async () => {
    const fullChunk = () => Array.from({ length: 1000 }, (_, i) => ({ id: i }))
    const fetchMock = vi
      .fn()
      .mockResolvedValue({ ok: true, json: async () => ({ items: fullChunk(), totalCount: 100000 }) } as Response)
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, exportMaxRows: 1500 }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    // 拉满两块（2000 行）后达上限 ⇒ 切到 1500 返回，不再继续请求。
    expect(all).toHaveLength(1500)
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  /**
   * 🔴 2026-08-04 补的全站兜底：此前不传 `exportMaxRows` 就是「不限」，而 64 个页面里只有 1 页
   * 真的传了——其余 59 页的「导出全部」都会一路拉到 200 万行熔断。这条用例守的是那个默认值。
   */
  it('未声明 exportMaxRows 时用保守默认上限兜底', async () => {
    const fullChunk = () => Array.from({ length: 1000 }, (_, i) => ({ id: i }))
    const fetchMock = vi
      .fn()
      .mockResolvedValue({ ok: true, json: async () => ({ items: fullChunk(), totalCount: 1_000_000 }) } as Response)
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    // 断言锚常量而非字面量：默认值调整时这条用例应该跟着走，而不是变成需要手改的绊脚石。
    expect(all).toHaveLength(DEFAULT_EXPORT_MAX_ROWS)
    expect(fetchMock).toHaveBeenCalledTimes(DEFAULT_EXPORT_MAX_ROWS / 1000)
  })

  /**
   * `exportMaxRows: null` = 显式声明「本页不需要上限」。
   *
   * 单独立一条的理由：注入处若写成 `options.exportMaxRows ?? DEFAULT`，`??` 会把 `null` 和
   * `undefined` 一视同仁地吃掉，opt-out 静默失效——而这在小数据量下与正确实现表现完全一致，
   * 只有拉过默认上限才显形。故这里刻意造 6 万行（> 默认 5 万）。
   */
  it('exportMaxRows 传 null 时不套默认上限，一路拉到取完', async () => {
    const CHUNKS = 60
    let served = 0
    const fetchMock = vi.fn().mockImplementation(async () => {
      const items = served++ < CHUNKS ? Array.from({ length: 1000 }, (_, i) => ({ id: i })) : []
      return { ok: true, json: async () => ({ items, totalCount: CHUNKS * 1000 }) } as Response
    })
    vi.stubGlobal('fetch', fetchMock)

    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, exportMaxRows: null }),
    )

    let all: Emp[] = []
    await act(async () => {
      all = await result.current.tableProps.onExportAll()
    })

    expect(all).toHaveLength(CHUNKS * 1000)
    expect(all.length).toBeGreaterThan(DEFAULT_EXPORT_MAX_ROWS)
  })

  it('导出沿用当前搜索过滤词', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    act(() => result.current.setSearch('王五'))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    fetchMock.mockClear()

    await act(async () => {
      await result.current.tableProps.onExportAll()
    })

    expect(decodeURIComponent(urlOf(fetchMock))).toContain('filter=王五')
  })
})

describe('useCrudResource · 失败反馈', () => {
  it('reload 失败时 toast destructive，使用 errorTitle', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, text: async () => 'boom' } as Response)
    vi.stubGlobal('fetch', fetchMock)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        errorTitle: '员工列表加载失败',
      }),
    )

    act(() => result.current.setPage(1))

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ variant: 'destructive', title: '员工列表加载失败' }),
      ),
    )
  })
})

describe('useCrudResource · reloadAndRefresh（写后专用）', () => {
  it('reload 重取当前页 + router.refresh 失效路由缓存', async () => {
    const fetchMock = stubJson([{ id: 1 }], 1)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    await act(async () => { await result.current.reloadAndRefresh() })

    expect(fetchMock).toHaveBeenCalledTimes(1) // 客户端重取
    expect(refreshMock).toHaveBeenCalledTimes(1) // 失效 Next Router Cache
    expect(result.current.rows).toEqual([{ id: 1 }])
  })

  it('tableProps.onReload = reloadAndRefresh（工具栏刷新按钮：重取当前页 + 失效缓存）', async () => {
    const fetchMock = stubJson([{ id: 5 }], 1)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    await act(async () => { await result.current.tableProps.onReload() })

    expect(fetchMock).toHaveBeenCalledTimes(1) // 客户端重取当前页
    expect(refreshMock).toHaveBeenCalledTimes(1) // 失效 Next Router Cache
    expect(result.current.rows).toEqual([{ id: 5 }])
  })

  it('纯 reload 不触发 router.refresh（分页/搜索反应不该刷缓存）', async () => {
    stubJson([{ id: 2 }], 1)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )

    act(() => result.current.setPage(1)) // 触发内部 reload
    await waitFor(() => expect(result.current.rows).toEqual([{ id: 2 }]))

    expect(refreshMock).not.toHaveBeenCalled()
  })
})

describe('useCrudResource · urlSync（URL 同步）', () => {
  it('urlSync 关闭（默认）时翻页/搜索不写 URL', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )
    act(() => result.current.setPage(2))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    act(() => result.current.setSearch('x'))
    expect(patchMock).not.toHaveBeenCalled()
  })

  it('urlSync 开启时 setPage 写 page（1-based，首页删除键）', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, urlSync: true }),
    )
    act(() => result.current.setPage(2))
    expect(patchMock).toHaveBeenCalledWith({ page: 3 })
    act(() => result.current.setPage(0))
    expect(patchMock).toHaveBeenCalledWith({ page: '' })
  })

  it('urlSync 开启时 setPageSize 写 size（等于默认值则删除键）', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, urlSync: true, pageSize: 15 }),
    )
    act(() => result.current.setPageSize(20))
    expect(patchMock).toHaveBeenCalledWith({ size: 20 })
    act(() => result.current.setPageSize(15))
    expect(patchMock).toHaveBeenCalledWith({ size: '' })
  })

  it('urlSync 开启时 setSearch 写 q', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, urlSync: true }),
    )
    act(() => result.current.setSearch('张三'))
    expect(patchMock).toHaveBeenCalledWith({ q: '张三' })
  })

  it('initialPage / initialSearch 作为分页与搜索的初值（切回/刷新恢复）', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: { items: [], totalCount: 100 },
        initialPage: 2,
        initialSearch: 'abc',
      }),
    )
    expect(result.current.page).toBe(2)
    expect(result.current.search).toBe('abc')
  })

  it('urlSync 开启时改排序写 sort 并回第 1 页（同步清 URL page 键）', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, urlSync: true }),
    )
    act(() => result.current.tableProps.onSortingChange([{ id: 'name', desc: true }]))
    expect(patchMock).toHaveBeenCalledWith({ sort: 'name.desc' })
    expect(patchMock).toHaveBeenCalledWith({ page: '' })
    // 清排序 → 从 URL 删除 sort 键
    act(() => result.current.tableProps.onSortingChange([]))
    expect(patchMock).toHaveBeenCalledWith({ sort: '' })
  })

  it('initialSorting 作为排序初值并进入取数参数（切回/刷新恢复）', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        initialSorting: [{ id: 'code', desc: false }],
      }),
    )
    expect(result.current.tableProps.sorting).toEqual([{ id: 'code', desc: false }])
    // 首帧用 initial 跳过请求；翻页后重取应带上初始排序
    act(() => result.current.setPage(1))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('Sorting=code+asc')
  })

  it('resetView：搜索/页码/排序回默认并清 URL 键（每页条数不动，归用户偏好，见蓝图 §7.2）', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        urlSync: true,
        pageSize: 15,
        initialPage: 3,
        initialSearch: 'x',
        initialSorting: [{ id: 'name', desc: true }],
      }),
    )
    act(() => result.current.setPageSize(50)) // 用户手动调过每页条数
    act(() => result.current.resetView())
    expect(result.current.page).toBe(0)
    expect(result.current.pageSize).toBe(50) // resetView 不触碰
    expect(result.current.search).toBe('')
    expect(result.current.tableProps.sorting).toEqual([])
    // synced 版本清 URL：q/page/sort 写入删除语义；size 键仅由用户显式调用 setPageSize 写入（见上一行 act）
    expect(patchMock).toHaveBeenCalledWith({ q: '' })
    expect(patchMock).toHaveBeenCalledWith({ page: '' })
    expect(patchMock).toHaveBeenCalledWith({ size: 50 })
    expect(patchMock).toHaveBeenCalledWith({ sort: '' })
  })
})

// 蓝图 §7.3 第 2 条 + §7.2 P1 决议：viewDirty = search 非空 || page 非 0 || sorting 非空
// || 页面级 useUrlListState 筛选活跃（经 useSyncExternalStore 订阅），驱动工具栏「重置」按钮显隐。
// pageSize 不参与（归用户偏好而非会话视图态，resetView 也不动它——按钮出现即点击必生效的契约）。
describe('useCrudResource · tableProps.viewDirty', () => {
  it('默认视图（无搜索/翻页/排序/筛选）viewDirty 为 false', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )
    expect(result.current.tableProps.viewDirty).toBe(false)
  })

  it('search 非空时 viewDirty 为 true', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )
    act(() => result.current.setSearch('abc'))
    expect(result.current.tableProps.viewDirty).toBe(true)
  })

  it('page !== 0 时 viewDirty 为 true', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )
    act(() => result.current.setPage(1))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(result.current.tableProps.viewDirty).toBe(true)
  })

  it('pageSize 偏离默认不影响 viewDirty（归用户偏好，重置按钮不该为此出现却点了没反应）', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial, pageSize: 15 }),
    )
    act(() => result.current.setPageSize(50))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(result.current.tableProps.viewDirty).toBe(false)
  })

  it('sorting 非空时 viewDirty 为 true', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial }),
    )
    act(() => result.current.tableProps.onSortingChange([{ id: 'name', desc: true }]))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(result.current.tableProps.viewDirty).toBe(true)
  })

  it('页面级 useUrlListState 筛选非默认时 viewDirty 同步随之为 true（经外部 store 订阅）', () => {
    stubJson([], 0)
    const { result } = renderHook(() => {
      const [, setFilters] = useUrlListState({ status: '' })
      const list = useCrudResource<Emp>({ endpoint: 'employees', initial: emptyInitial })
      return { setFilters, list }
    })
    expect(result.current.list.tableProps.viewDirty).toBe(false)

    act(() => result.current.setFilters({ status: 'active' }))
    expect(result.current.list.tableProps.viewDirty).toBe(true)

    act(() => result.current.setFilters({ status: '' }))
    expect(result.current.list.tableProps.viewDirty).toBe(false)
  })

  it('resetView 后 viewDirty 回 false', () => {
    stubJson([], 0)
    const { result } = renderHook(() =>
      useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        initialSearch: 'x',
        initialPage: 2,
      }),
    )
    expect(result.current.tableProps.viewDirty).toBe(true)

    act(() => result.current.resetView())
    expect(result.current.tableProps.viewDirty).toBe(false)
  })
})

// 蓝图 §7.2「重置=视图与数据一致」：query-button 式页面（audit-logs 等）extraDeps 只含
// queryVersion、不含页面级 filters，用户若只碰过页面级筛选（未碰 search/page/sorting），
// 这三者本就是默认值——resetView 若只靠它们变化触发重取会静默失效（reload 引用不变）。
// resetNonce 保证 resetView 无论如何都强制重取一次。
describe('useCrudResource · resetView 强制重取（治「查询按钮式页面重置不重查」）', () => {
  it('只碰页面级筛选、从未碰 search/page/sorting 时，resetView 仍强制重取一次且带上已清空的筛选', async () => {
    const fetchMock = stubJson([], 0)
    const { result } = renderHook(() => {
      const [filters, setFilters] = useUrlListState({ status: '' })
      const list = useCrudResource<Emp>({
        endpoint: 'employees',
        initial: emptyInitial,
        buildParams: (qs) => { if (filters.status) qs.set('status', filters.status) },
      })
      return { setFilters, list }
    })

    // 只设页面级筛选：不在 extraDeps 里，单独不触发 reload（模拟查询按钮式页面「输入即时进 URL，取数靠点查询」）
    act(() => result.current.setFilters({ status: 'active' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(result.current.list.tableProps.viewDirty).toBe(true)

    // resetView：page/search/sorting 全程都是默认值（本用例从未碰过），过去 reload 引用不变、不会重取
    act(() => result.current.list.resetView())
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(decodeURIComponent(urlOf(fetchMock))).not.toContain('status=active') // 重取时筛选已清空
    expect(result.current.list.tableProps.viewDirty).toBe(false)
  })
})
