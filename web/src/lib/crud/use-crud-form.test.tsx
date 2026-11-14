import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook as renderHookRTL, act, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import { useCrudForm, type UseCrudFormOptions } from './use-crud-form'
import zhHansMessages from '../../../messages/zh-Hans.json'

// useCrudForm 内 toast 文案模板经 useTranslations('Common') 求值（i18n 批次4b前置），
// 组件树需要 NextIntlClientProvider 才能渲染；真实 zh-Hans messages 直供，与生产一致。
// 用同名遮蔽包一层 wrapper，断言零改动。
function renderHook<TProps, TResult>(
  callback: (props: TProps) => TResult,
  options?: Parameters<typeof renderHookRTL<TResult, TProps>>[1],
) {
  return renderHookRTL(callback, {
    wrapper: ({ children }) => (
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{children}</NextIntlClientProvider>
    ),
    ...options,
  })
}

// useRouter().refresh 在每次写成功后都会调用（失效 Next Router Cache，防切页签返回读旧 RSC），
// 无论是否传 onSuccess。固定一个 mock 以便断言。
const { refreshMock } = vi.hoisted(() => ({ refreshMock: vi.fn() }))
vi.mock('next/navigation', () => ({
  useRouter: () => ({ refresh: refreshMock }),
}))
vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))

// 共享件在写操作失败的 catch 里会 console.error（预期行为）——静默以免污染测试输出。
// 模块级 spy 一次：clearMocks 只清调用记录、保留这里的空实现。
vi.spyOn(console, 'error').mockImplementation(() => {})

const toastMock = vi.mocked(toast)

interface Row {
  id: string
  name: string
  code: string
}
interface Form {
  name: string
  code: string
}

const defaultForm: Form = { name: '', code: '' }
const row: Row = { id: 'r-1', name: '工程师', code: 'P1' }

function baseOptions(
  overrides: Partial<UseCrudFormOptions<Form, Row>> = {},
): UseCrudFormOptions<Form, Row> {
  return {
    entityName: '岗位',
    endpoint: 'positions',
    defaultForm,
    toForm: (r) => ({ name: r.name, code: r.code }),
    toBody: (f) => ({ name: f.name, code: f.code }),
    ...overrides,
  }
}

/** 按序桩 fetch 的多次返回（只覆盖共享件用到的 `ok` / `text()`）。 */
function stubFetch(responses: Array<{ ok: boolean; body?: string }>) {
  const fetchMock = vi.fn()
  for (const r of responses) {
    fetchMock.mockResolvedValueOnce({ ok: r.ok, text: async () => r.body ?? '' } as Response)
  }
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** 取第 n 次 fetch 调用的 [url, init]，带上类型。 */
function fetchCall(fetchMock: ReturnType<typeof vi.fn>, n = 0): [string, RequestInit] {
  return fetchMock.mock.calls[n] as [string, RequestInit]
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('useCrudForm · openCreate', () => {
  it('进入 create 模式、重置为 defaultForm、清空 target', async () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))
    // 先进编辑态制造 target，验证 openCreate 会清掉它
    act(() => result.current.openEdit(row))
    expect(result.current.target).toEqual(row)

    await act(async () => {
      await result.current.openCreate()
    })

    expect(result.current.mode).toBe('create')
    expect(result.current.isOpen).toBe(true)
    expect(result.current.target).toBeNull()
    expect(result.current.form).toEqual(defaultForm)
  })

  it('beforeCreate 返回的 patch 合并进表单，其余字段保留默认', async () => {
    const beforeCreate = vi.fn(async () => ({ name: '默认名' }))
    const { result } = renderHook(() => useCrudForm(baseOptions({ beforeCreate })))

    await act(async () => {
      await result.current.openCreate()
    })

    expect(beforeCreate).toHaveBeenCalledTimes(1)
    expect(result.current.form).toEqual({ name: '默认名', code: '' })
  })

  it('beforeCreate 无返回值（void）时表单保持默认', async () => {
    const beforeCreate = vi.fn(async () => {})
    const { result } = renderHook(() => useCrudForm(baseOptions({ beforeCreate })))

    await act(async () => {
      await result.current.openCreate()
    })

    expect(result.current.form).toEqual(defaultForm)
  })
})

describe('useCrudForm · openEdit', () => {
  it('进入 edit 模式、回填 toForm(row)、记录 target、调用 beforeEdit(row)', () => {
    const beforeEdit = vi.fn()
    const { result } = renderHook(() => useCrudForm(baseOptions({ beforeEdit })))

    act(() => result.current.openEdit(row))

    expect(result.current.mode).toBe('edit')
    expect(result.current.isOpen).toBe(true)
    expect(result.current.target).toEqual(row)
    expect(result.current.form).toEqual({ name: '工程师', code: 'P1' })
    expect(beforeEdit).toHaveBeenCalledWith(row)
  })
})

describe('useCrudForm · set', () => {
  it('函数式 set 基于最新 form 合并，不会被随后的补丁盖掉刚写入的字段', async () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))
    await act(async () => {
      await result.current.openCreate()
    })
    act(() => {
      result.current.set({ name: '工程师' })
      result.current.set(prev => ({ code: prev.name === '工程师' ? 'P1' : '' }))
    })
    expect(result.current.form).toEqual({ name: '工程师', code: 'P1' })
  })
})

describe('useCrudForm · submit（create 分支）', () => {
  it('POST 到 /api/proxy/{endpoint}，body 为 toBody 结果', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const onSuccess = vi.fn()
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess })))

    await act(async () => {
      await result.current.openCreate()
    })
    act(() => result.current.set({ name: '工程师', code: 'P1' }))
    await act(async () => {
      await result.current.submit()
    })

    const [url, init] = fetchCall(fetchMock)
    expect(url).toBe('/api/proxy/positions')
    expect(init.method).toBe('POST')
    expect(init.headers).toMatchObject({ 'Content-Type': 'application/json' })
    expect(JSON.parse(init.body as string)).toEqual({ name: '工程师', code: 'P1' })
  })

  it('成功后 toast「已创建」、关闭对话框、调用 onSuccess，且始终 router.refresh 失效路由缓存', async () => {
    stubFetch([{ ok: true }])
    const onSuccess = vi.fn()
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess })))

    await act(async () => {
      await result.current.openCreate()
    })
    act(() => result.current.set({ name: '工程师', code: 'P1' }))
    await act(async () => {
      await result.current.submit()
    })

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: 'success',
        title: '岗位已创建',
        description: '岗位「工程师」已保存',
      }),
    )
    expect(result.current.isOpen).toBe(false)
    expect(onSuccess).toHaveBeenCalledTimes(1)
    // 即便传了 onSuccess（服务端分页 reload），也必须 router.refresh() 失效 Router Cache，
    // 否则切走再切回会读到 staleTimes 窗口内的旧 SSR 快照（「新建行消失」bug）。
    expect(refreshMock).toHaveBeenCalledTimes(1)
  })
})

describe('useCrudForm · submit（edit 分支）', () => {
  it('PUT 到 /api/proxy/{endpoint}/{id}（默认 getId 读 .id）', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess: vi.fn() })))

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    const [url, init] = fetchCall(fetchMock)
    expect(url).toBe('/api/proxy/positions/r-1')
    expect(init.method).toBe('PUT')
    expect(toastMock).toHaveBeenCalledWith(expect.objectContaining({ title: '岗位已更新' }))
  })

  it('未传 onSuccess 时默认调用 router.refresh', async () => {
    stubFetch([{ ok: true }])
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    expect(refreshMock).toHaveBeenCalledTimes(1)
  })

  it('buildUrl 覆盖默认 URL 拼接', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const buildUrl = vi.fn(() => '/api/proxy/custom/path')
    const { result } = renderHook(() =>
      useCrudForm(baseOptions({ buildUrl, onSuccess: vi.fn() })),
    )

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    expect(buildUrl).toHaveBeenCalledWith('edit', row)
    expect(fetchCall(fetchMock)[0]).toBe('/api/proxy/custom/path')
  })

  it('getId 自定义影响 PUT 路径', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const { result } = renderHook(() =>
      useCrudForm(baseOptions({ getId: (r) => r.code, onSuccess: vi.fn() })),
    )

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    expect(fetchCall(fetchMock)[0]).toBe('/api/proxy/positions/P1')
  })

  it('editHttpMethod 覆盖 edit 态提交方法（配合 buildUrl 指向专属端点，如"修改重提"）', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const buildUrl = vi.fn(() => '/api/proxy/requisitions/r-1/resubmit')
    const { result } = renderHook(() =>
      useCrudForm(baseOptions({ buildUrl, editHttpMethod: 'POST', onSuccess: vi.fn() })),
    )

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    const [url, init] = fetchCall(fetchMock)
    expect(url).toBe('/api/proxy/requisitions/r-1/resubmit')
    expect(init.method).toBe('POST')
  })

  it('未传 editHttpMethod 时 edit 态默认仍是 PUT（向后兼容）', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess: vi.fn() })))

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    expect(fetchCall(fetchMock)[1].method).toBe('PUT')
  })

  it('create 态恒为 POST，不受 editHttpMethod 影响', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const { result } = renderHook(() =>
      useCrudForm(baseOptions({ editHttpMethod: 'POST', onSuccess: vi.fn() })),
    )

    await act(async () => {
      await result.current.openCreate()
    })
    await act(async () => {
      await result.current.submit()
    })

    expect(fetchCall(fetchMock)[1].method).toBe('POST')
  })

  it('getName 自定义影响成功 toast 文案', async () => {
    stubFetch([{ ok: true }])
    const { result } = renderHook(() =>
      useCrudForm(baseOptions({ getName: () => '自定义名', onSuccess: vi.fn() })),
    )

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ description: '岗位「自定义名」已保存' }),
    )
  })

  it('toBody 在 create 收到 (form,"create",null)、edit 收到 (form,"edit",row)', async () => {
    const toBody = vi.fn((f: Form) => ({ ...f }))
    stubFetch([{ ok: true }, { ok: true }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ toBody, onSuccess: vi.fn() })))

    await act(async () => {
      await result.current.openCreate()
    })
    act(() => result.current.set({ name: 'a', code: 'b' }))
    await act(async () => {
      await result.current.submit()
    })
    expect(toBody).toHaveBeenLastCalledWith({ name: 'a', code: 'b' }, 'create', null)

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })
    expect(toBody).toHaveBeenLastCalledWith({ name: '工程师', code: 'P1' }, 'edit', row)
  })
})

describe('useCrudForm · submit（失败与 formError）', () => {
  it('失败时把后端 ABP 中文 message 内联到 formError，且不关闭/不刷新/不 toast', async () => {
    const onSuccess = vi.fn()
    stubFetch([{ ok: false, body: JSON.stringify({ error: { message: '岗位编码已存在' } }) }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess })))

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })

    expect(result.current.formError).toBe('岗位编码已存在')
    expect(result.current.isOpen).toBe(true)
    expect(onSuccess).not.toHaveBeenCalled()
    expect(toastMock).not.toHaveBeenCalled()
  })

  it('再次提交成功会清空上一次的 formError', async () => {
    stubFetch([{ ok: false, body: '第一次失败' }, { ok: true }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess: vi.fn() })))

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })
    expect(result.current.formError).toBe('第一次失败')

    await act(async () => {
      await result.current.submit()
    })
    expect(result.current.formError).toBeNull()
  })

  it('submit 期间 submitting 为 true，结束后回 false', async () => {
    let resolveFetch!: (v: Response) => void
    const fetchMock = vi.fn().mockReturnValue(
      new Promise<Response>((r) => {
        resolveFetch = r
      }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess: vi.fn() })))

    act(() => result.current.openEdit(row))
    let submitPromise!: Promise<void>
    act(() => {
      submitPromise = result.current.submit()
    })
    await waitFor(() => expect(result.current.submitting).toBe(true))

    await act(async () => {
      resolveFetch({ ok: true, text: async () => '' } as Response)
      await submitPromise
    })
    expect(result.current.submitting).toBe(false)
  })
})

describe('useCrudForm · confirmDelete', () => {
  it('无 deleteTarget 时直接返回、不发请求', async () => {
    const fetchMock = stubFetch([])
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    await act(async () => {
      await result.current.confirmDelete()
    })

    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('askDelete 后 DELETE /api/proxy/{endpoint}/{id}，成功 toast + onSuccess + 清空 deleteTarget', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const onSuccess = vi.fn()
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess })))

    act(() => result.current.askDelete(row))
    expect(result.current.deleteTarget).toEqual(row)

    await act(async () => {
      await result.current.confirmDelete()
    })

    const [url, init] = fetchCall(fetchMock)
    expect(url).toBe('/api/proxy/positions/r-1')
    expect(init.method).toBe('DELETE')
    expect(onSuccess).toHaveBeenCalledTimes(1)
    expect(refreshMock).toHaveBeenCalledTimes(1) // 删除后同样失效 Router Cache
    expect(result.current.deleteTarget).toBeNull()
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ variant: 'success', title: '已删除' }),
    )
  })

  it('删除失败时 toast destructive、保留 deleteTarget、不调 onSuccess', async () => {
    stubFetch([{ ok: false, body: 'boom' }])
    const onSuccess = vi.fn()
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess })))

    act(() => result.current.askDelete(row))
    await act(async () => {
      await result.current.confirmDelete()
    })

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ variant: 'destructive', title: '删除失败' }),
    )
    expect(onSuccess).not.toHaveBeenCalled()
    expect(result.current.deleteTarget).toEqual(row)
  })

  it('getId 自定义影响 DELETE 路径', async () => {
    const fetchMock = stubFetch([{ ok: true }])
    const { result } = renderHook(() =>
      useCrudForm(baseOptions({ getId: (r) => r.code, onSuccess: vi.fn() })),
    )

    act(() => result.current.askDelete(row))
    await act(async () => {
      await result.current.confirmDelete()
    })

    expect(fetchCall(fetchMock)[0]).toBe('/api/proxy/positions/P1')
  })

  it('closeDelete 清空 deleteTarget', () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    act(() => result.current.askDelete(row))
    act(() => result.current.closeDelete())

    expect(result.current.deleteTarget).toBeNull()
  })
})

describe('useCrudForm · set / close', () => {
  it('set 局部合并表单', () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    act(() => result.current.set({ name: 'x' }))

    expect(result.current.form).toEqual({ name: 'x', code: '' })
  })

  it('close 清空 mode/target/formError', async () => {
    stubFetch([{ ok: false, body: '错误' }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess: vi.fn() })))

    act(() => result.current.openEdit(row))
    await act(async () => {
      await result.current.submit()
    })
    expect(result.current.formError).toBe('错误')

    act(() => result.current.close())

    expect(result.current.mode).toBeNull()
    expect(result.current.isOpen).toBe(false)
    expect(result.current.target).toBeNull()
    expect(result.current.formError).toBeNull()
  })
})

// isDirty 驱动 <CrudFormDialog> 的防误关拦截（点遮罩/Esc 有改动才拦）。
// 基线在打开时记录、关闭时清空；create/edit 各记各的，互不比较。
describe('useCrudForm - isDirty（防误关脏检查）', () => {
  it('未打开时恒为 false（无基线）', () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))
    expect(result.current.isDirty()).toBe(false)
  })

  it('openCreate 后未改动 false，set 之后 true', async () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    await act(async () => { await result.current.openCreate() })
    expect(result.current.isDirty()).toBe(false)

    act(() => result.current.set({ name: '新岗位' }))
    expect(result.current.isDirty()).toBe(true)
  })

  it('openEdit 回填后未改动 false，改动后 true', () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    act(() => result.current.openEdit(row))
    expect(result.current.isDirty()).toBe(false)

    act(() => result.current.set({ name: '高级工程师' }))
    expect(result.current.isDirty()).toBe(true)
  })

  it('改回原值后回到 false（比的是值不是操作次数）', () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    act(() => result.current.openEdit(row))
    act(() => result.current.set({ name: '改了' }))
    expect(result.current.isDirty()).toBe(true)

    act(() => result.current.set({ name: row.name }))
    expect(result.current.isDirty()).toBe(false)
  })

  it('beforeCreate 回填的默认值计进基线（否则一打开就被判 dirty、永远关不掉）', async () => {
    const { result } = renderHook(() => useCrudForm(baseOptions({
      beforeCreate: async () => ({ code: 'AUTO-1' }),
    })))

    await act(async () => { await result.current.openCreate() })

    expect(result.current.form).toEqual({ name: '', code: 'AUTO-1' })
    expect(result.current.isDirty()).toBe(false)
  })

  it('close 清空基线 → 回到 false', () => {
    const { result } = renderHook(() => useCrudForm(baseOptions()))

    act(() => result.current.openEdit(row))
    act(() => result.current.set({ name: '改了' }))
    expect(result.current.isDirty()).toBe(true)

    act(() => result.current.close())
    expect(result.current.isDirty()).toBe(false)
  })

  it('提交成功后（内部走 close）回到 false', async () => {
    stubFetch([{ ok: true }])
    const { result } = renderHook(() => useCrudForm(baseOptions({ onSuccess: vi.fn() })))

    act(() => result.current.openEdit(row))
    act(() => result.current.set({ name: '改了' }))
    await act(async () => { await result.current.submit() })

    expect(result.current.isDirty()).toBe(false)
  })
})
