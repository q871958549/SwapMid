import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { createElement, type ComponentType, type ReactNode } from 'react'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import {
  createAuthenticatedBrowserStorage,
  createIdentityDraftRawHandle,
} from '@/lib/browser-storage-policy'
import { useFormDraft } from './use-form-draft'

interface Form {
  name: string
  idCard: string
}

const FORM_DRAFT_SCHEMA_VERSION = 1

function isFormDraft(value: unknown): boolean {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const record = value as Record<string, unknown>
  const keys = Object.keys(record)
  return (keys.length === 1 || keys.length === 2)
    && keys.every((key) => key === 'name' || key === 'idCard')
    && typeof record.name === 'string'
    && (!Object.hasOwn(record, 'idCard') || typeof record.idCard === 'string')
}

const FORM_DRAFT_SCHEMA = {
  schemaVersion: FORM_DRAFT_SCHEMA_VERSION,
  validator: isFormDraft,
} as const

function anonymousDraftKey(formKey: string, schemaVersion = FORM_DRAFT_SCHEMA_VERSION) {
  return `swapmid-draft:${formKey}:v${schemaVersion}`
}

const TestIdentityProvider = SessionIdentityProvider as ComponentType<{
  identityVersion: string
  storagePrincipalId: string
  children?: ReactNode
}>

beforeEach(() => {
  window.localStorage.clear()
  window.sessionStorage.clear()
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

function identityWrapper(identityVersion: string) {
  return function IdentityWrapper({ children }: { children: ReactNode }) {
    return createElement(
      TestIdentityProvider,
      { identityVersion, storagePrincipalId: 'principal-a' },
      children,
    )
  }
}

function identityDraftHandle(
  identityVersion: string,
  formKey: string,
  schemaVersion = FORM_DRAFT_SCHEMA_VERSION,
) {
  const storage = createAuthenticatedBrowserStorage({
    identityVersion,
    storagePrincipalId: 'principal-a',
  })
  return createIdentityDraftRawHandle(storage, `${formKey}:v${schemaVersion}`, (raw) => {
    try {
      const parsed = JSON.parse(raw) as unknown
      return isFormDraft(parsed)
    } catch {
      return false
    }
  })
}

describe('useFormDraft · 检测', () => {
  it('storage 无草稿时 hasDraft=false、draftValue=null', () => {
    const { result } = renderHook(() => useFormDraft<Form>({
      scope: 'anonymous',
      formKey: 'f1',
      ...FORM_DRAFT_SCHEMA,
      value: { name: '', idCard: '' },
    }))
    expect(result.current.hasDraft).toBe(false)
    expect(result.current.draftValue).toBeNull()
  })

  it('storage 已有草稿时挂载即检测到，返回其值', () => {
    window.sessionStorage.setItem(anonymousDraftKey('f1'), JSON.stringify({ name: '张三', idCard: '' }))
    const { result } = renderHook(() => useFormDraft<Form>({
      scope: 'anonymous',
      formKey: 'f1',
      ...FORM_DRAFT_SCHEMA,
      value: { name: '', idCard: '' },
    }))
    expect(result.current.hasDraft).toBe(true)
    expect(result.current.draftValue).toEqual({ name: '张三', idCard: '' })
  })

  it('formKey 变化时重新检测（不同条目互不串稿）', () => {
    window.sessionStorage.setItem(anonymousDraftKey('f-a'), JSON.stringify({ name: 'A草稿', idCard: '' }))
    window.sessionStorage.setItem(anonymousDraftKey('f-b'), JSON.stringify({ name: 'B草稿', idCard: '' }))
    const { result, rerender } = renderHook(
      ({ formKey }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey,
        ...FORM_DRAFT_SCHEMA,
        value: { name: '', idCard: '' },
      }),
      { initialProps: { formKey: 'f-a' } },
    )
    expect(result.current.draftValue).toEqual({ name: 'A草稿', idCard: '' })

    rerender({ formKey: 'f-b' })
    expect(result.current.draftValue).toEqual({ name: 'B草稿', idCard: '' })
  })

  it('业务形状非法时不恢复并立即清理损坏草稿', () => {
    const key = anonymousDraftKey('invalid-shape')
    window.sessionStorage.setItem(key, JSON.stringify({ name: '残缺草稿', idCard: [] }))

    const { result } = renderHook(() => useFormDraft<Form>({
      scope: 'anonymous',
      formKey: 'invalid-shape',
      ...FORM_DRAFT_SCHEMA,
      value: { name: '', idCard: '' },
    }))

    expect(result.current.hasDraft).toBe(false)
    expect(result.current.draftValue).toBeNull()
    expect(window.sessionStorage.getItem(key)).toBeNull()
  })

  it('schemaVersion 进入实际键，升级后不自动读取旧版本草稿', () => {
    const oldKey = anonymousDraftKey('versioned', 1)
    window.sessionStorage.setItem(oldKey, JSON.stringify({ name: '旧结构', idCard: '' }))

    const { result, rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'versioned',
        schemaVersion: 2,
        validator: isFormDraft,
        value,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )

    expect(result.current.draftValue).toBeNull()
    expect(window.sessionStorage.getItem(oldKey)).not.toBeNull()

    act(() => { rerender({ value: { name: '新结构', idCard: '' } }) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('versioned', 2))!))
      .toEqual({ name: '新结构', idCard: '' })
  })
})

describe('useFormDraft · 节流写入', () => {
  it('value 变化后节流窗口内多次更新只落盘一次，且是最新值', () => {
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'f2',
        ...FORM_DRAFT_SCHEMA,
        value,
        throttleMs: 500,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )

    // 首次 value 变化：立即 flush（lastFlushAtRef 尚为 0）
    act(() => { rerender({ value: { name: '张', idCard: '' } }) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f2'))!)).toEqual({ name: '张', idCard: '' })

    // 窗口内快速多次变化：不应立即再次落盘
    act(() => { rerender({ value: { name: '张三', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(100) })
    act(() => { rerender({ value: { name: '张三丰', idCard: '' } }) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f2'))!)).toEqual({ name: '张', idCard: '' })

    // 窗口结束后，落盘为窗口内最新值
    act(() => { vi.advanceTimersByTime(500) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f2'))!)).toEqual({ name: '张三丰', idCard: '' })
  })

  it('formKey 变化后的第一次写入被跳过（防止覆盖刚检测到的草稿）', () => {
    window.sessionStorage.setItem(anonymousDraftKey('f3'), JSON.stringify({ name: '已存草稿', idCard: '' }))
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'f3', ...FORM_DRAFT_SCHEMA, value,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    // 挂载时 value 仍是默认空值，不应把刚检测到的草稿覆盖掉
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f3'))!)).toEqual({ name: '已存草稿', idCard: '' })

    // 真正的下一次 value 变化才正常参与节流写入
    act(() => { rerender({ value: { name: '新值', idCard: '' } }) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f3'))!)).toEqual({ name: '新值', idCard: '' })
  })

  it('enabled=false 时不写入', () => {
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'f4',
        ...FORM_DRAFT_SCHEMA,
        value,
        enabled: false,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    act(() => { rerender({ value: { name: '不应落盘', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(window.sessionStorage.getItem(anonymousDraftKey('f4'))).toBeNull()
  })

  it('value 未通过 validator 时拒绝覆盖最后一份合法草稿', () => {
    const key = anonymousDraftKey('invalid-write')
    window.sessionStorage.setItem(key, JSON.stringify({ name: '合法草稿', idCard: '' }))
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'invalid-write',
        ...FORM_DRAFT_SCHEMA,
        value,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )

    act(() => {
      rerender({ value: { name: 42, idCard: '' } as unknown as Form })
    })

    expect(JSON.parse(window.sessionStorage.getItem(key)!))
      .toEqual({ name: '合法草稿', idCard: '' })
  })
})

describe('useFormDraft · 写入内容判等抑制', () => {
  // 守的是「已为您恢复未提交的草稿」误报的根因：写入 effect 依赖 value 的**引用**，调用方传内联
  // 字面量时任何无关 render 都会调度一次写入，把与库里一致的表单值落成草稿。

  it('同值 re-render（新引用、内容不变）不落盘', () => {
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'noop-write', ...FORM_DRAFT_SCHEMA, value,
      }),
      { initialProps: { value: { name: '库里的值', idCard: '' } } },
    )
    // 每次都是新对象、内容一字未改——不该产生任何草稿，否则下次打开凭空亮起「已恢复草稿」
    act(() => { rerender({ value: { name: '库里的值', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    act(() => { rerender({ value: { name: '库里的值', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(window.sessionStorage.getItem(anonymousDraftKey('noop-write'))).toBeNull()
  })

  it('真实值变化仍正常写入（抑制不得过度）', () => {
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'real-change', ...FORM_DRAFT_SCHEMA, value,
      }),
      { initialProps: { value: { name: '原值', idCard: '' } } },
    )
    act(() => { rerender({ value: { name: '用户改的', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('real-change'))!))
      .toEqual({ name: '用户改的', idCard: '' })
  })

  it('改回基线只跳过写入、不删除已有草稿（撤销语义交调用方恢复时判等处理）', () => {
    const key = anonymousDraftKey('back-to-baseline')
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'back-to-baseline', ...FORM_DRAFT_SCHEMA, value,
      }),
      { initialProps: { value: { name: '原值', idCard: '' } } },
    )
    act(() => { rerender({ value: { name: '改了一下', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(JSON.parse(window.sessionStorage.getItem(key)!)).toEqual({ name: '改了一下', idCard: '' })

    act(() => { rerender({ value: { name: '原值', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(JSON.parse(window.sessionStorage.getItem(key)!)).toEqual({ name: '改了一下', idCard: '' })
  })

  it('带 omitKeys 时按裁剪后的内容判等（基线不裁剪的话这条会红）', () => {
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'omit-eq', ...FORM_DRAFT_SCHEMA, value, omitKeys: ['idCard'],
      }),
      { initialProps: { value: { name: '原值', idCard: '' } } },
    )
    // 只有被剔除的 idCard 变了 → 真正落盘的内容与基线一致 → 不写
    act(() => { rerender({ value: { name: '原值', idCard: '110101199001011234' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(window.sessionStorage.getItem(anonymousDraftKey('omit-eq'))).toBeNull()
  })

  it('内容与 storage 现值相同时不重复写（幂等省写）', () => {
    const key = anonymousDraftKey('idempotent')
    window.sessionStorage.setItem(key, JSON.stringify({ name: '存量草稿', idCard: '' }))
    const setItemSpy = vi.spyOn(Storage.prototype, 'setItem')
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'idempotent', ...FORM_DRAFT_SCHEMA, value,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    // 模拟调用方把检测到的草稿应用回表单：值与 storage 现存的完全一致，不必再写一次
    act(() => { rerender({ value: { name: '存量草稿', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(setItemSpy).not.toHaveBeenCalled()
    setItemSpy.mockRestore()
  })
})

describe('useFormDraft · omitKeys', () => {
  it('敏感字段不写入 storage', () => {
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'f5',
        ...FORM_DRAFT_SCHEMA,
        value,
        omitKeys: ['idCard'],
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    act(() => { rerender({ value: { name: '李四', idCard: '110101199001011234' } }) })
    const stored = JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f5'))!)
    expect(stored).toEqual({ name: '李四' })
    expect(stored.idCard).toBeUndefined()
  })

  it('检测到的草稿（写入时已裁剪）自然不含敏感字段', () => {
    window.sessionStorage.setItem(anonymousDraftKey('f6'), JSON.stringify({ name: '王五' }))
    const { result } = renderHook(() =>
      useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'f6',
        ...FORM_DRAFT_SCHEMA,
        value: { name: '', idCard: '' },
        omitKeys: ['idCard'],
      }),
    )
    expect(result.current.draftValue).toEqual({ name: '王五' })
  })
})

describe('useFormDraft · clearDraft', () => {
  it('清除 storage 中的草稿并把 hasDraft/draftValue 归位', () => {
    window.sessionStorage.setItem(anonymousDraftKey('f7'), JSON.stringify({ name: '待清除', idCard: '' }))
    const { result } = renderHook(() => useFormDraft<Form>({
      scope: 'anonymous',
      formKey: 'f7',
      ...FORM_DRAFT_SCHEMA,
      value: { name: '', idCard: '' },
    }))
    expect(result.current.hasDraft).toBe(true)

    act(() => { result.current.clearDraft() })

    expect(result.current.hasDraft).toBe(false)
    expect(result.current.draftValue).toBeNull()
    expect(window.sessionStorage.getItem(anonymousDraftKey('f7'))).toBeNull()
  })

  it('clearDraft 后取消尚未触发的节流写入，不会把旧值写回', () => {
    const { result, rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'f8',
        ...FORM_DRAFT_SCHEMA,
        value,
        throttleMs: 500,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    act(() => { rerender({ value: { name: '第一次', idCard: '' } }) }) // 立即 flush
    act(() => { rerender({ value: { name: '第二次（窗口内）', idCard: '' } }) }) // 排进节流定时器

    act(() => { result.current.clearDraft() })
    act(() => { vi.advanceTimersByTime(1000) })

    expect(window.sessionStorage.getItem(anonymousDraftKey('f8'))).toBeNull()
  })

  it('clearDraft 后调用方立即把表单重置为默认值不会让草稿复活（P2 实测发现的 bug 回归用例）', () => {
    // 典型调用方模式（interview-register/eval/knowledge-center manage 等 handleClearDraft）：
    // clearDraft() 后紧接着把 value 改回默认值——这本身是一次新的 value 变化，若不拦截会在
    // throttleMs 后把"刚重置的默认值"当新草稿写回 storage，hasDraft 复活。
    const { result, rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous',
        formKey: 'f11',
        ...FORM_DRAFT_SCHEMA,
        value,
        throttleMs: 500,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    // 挂载后的第一次真实变化：模拟用户填写
    act(() => { rerender({ value: { name: '张三', idCard: '' } }) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f11'))!)).toEqual({ name: '张三', idCard: '' })

    act(() => { result.current.clearDraft() })
    expect(window.sessionStorage.getItem(anonymousDraftKey('f11'))).toBeNull()

    // 调用方紧接着把表单重置为默认值（同一处理函数里常见的下一步）
    act(() => { rerender({ value: { name: '', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(window.sessionStorage.getItem(anonymousDraftKey('f11'))).toBeNull()

    // 之后用户真正重新填写时，写入应恢复正常（不应被永久跳过）
    act(() => { rerender({ value: { name: '新的一轮', idCard: '' } }) })
    act(() => { vi.advanceTimersByTime(1000) })
    expect(JSON.parse(window.sessionStorage.getItem(anonymousDraftKey('f11'))!)).toEqual({ name: '新的一轮', idCard: '' })
  })
})

describe('useFormDraft · identity 作用域', () => {
  it('identity 草稿缺少会话 Provider 时快速失败，禁止静默退化为匿名键', () => {
    expect(() => renderHook(() => useFormDraft<Form>({
      scope: 'identity',
      formKey: 'knowledge-article:42',
      ...FORM_DRAFT_SCHEMA,
      value: { name: '', idCard: '' },
    }))).toThrow('SessionIdentityProvider is required for identity form drafts')
  })

  it('已认证草稿只写 identity envelope，不再写旧匿名键', () => {
    const handle = identityDraftHandle('identity-a', 'knowledge-article:42')
    const { result, rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'identity',
        formKey: 'knowledge-article:42',
        ...FORM_DRAFT_SCHEMA,
        value,
      }),
      {
        wrapper: identityWrapper('identity-a'),
        initialProps: { value: { name: '', idCard: '' } },
      },
    )

    act(() => { rerender({ value: { name: '身份草稿', idCard: '' } }) })

    expect(result.current.draftValue).toEqual({ name: '身份草稿', idCard: '' })
    expect(handle.getItem()).toBe(JSON.stringify({ name: '身份草稿', idCard: '' }))
    expect(window.sessionStorage.getItem('swapmid-draft:knowledge-article:42')).toBeNull()
  })

  it('不同 identity 无法恢复旧身份草稿', () => {
    const oldHandle = identityDraftHandle('identity-old', 'plan-template:42')
    oldHandle.setItem(JSON.stringify({ name: '旧账号', idCard: '' }))

    const { result } = renderHook(
      () => useFormDraft<Form>({
        scope: 'identity',
        formKey: 'plan-template:42',
        ...FORM_DRAFT_SCHEMA,
        value: { name: '', idCard: '' },
      }),
      { wrapper: identityWrapper('identity-new') },
    )

    expect(result.current.hasDraft).toBe(false)
    expect(result.current.draftValue).toBeNull()
  })

  it('身份切换时迟到定时器捕获旧 handle，绝不把旧值写入新 namespace', () => {
    let activeIdentity = 'identity-old'
    function MutableIdentityWrapper({ children }: { children: ReactNode }) {
      return createElement(
        TestIdentityProvider,
        { identityVersion: activeIdentity, storagePrincipalId: 'principal-a' },
        children,
      )
    }

    const oldHandle = identityDraftHandle('identity-old', 'recruitment-requisition:42')
    const { rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'identity',
        formKey: 'recruitment-requisition:42',
        ...FORM_DRAFT_SCHEMA,
        value,
        throttleMs: 500,
      }),
      {
        wrapper: MutableIdentityWrapper,
        initialProps: { value: { name: '', idCard: '' } },
      },
    )

    act(() => { rerender({ value: { name: '第一次', idCard: '' } }) })
    act(() => { rerender({ value: { name: '旧身份待写', idCard: '' } }) })

    // 模拟 clearTimeout 已经来不及阻止回调开始执行的竞态，验证回调自身仍绑定旧 handle。
    const clearTimeoutSpy = vi.spyOn(window, 'clearTimeout').mockImplementation(() => undefined)
    activeIdentity = 'identity-new'
    act(() => { rerender({ value: { name: '新身份默认值', idCard: '' } }) })
    const newHandle = identityDraftHandle('identity-new', 'recruitment-requisition:42')

    act(() => { vi.advanceTimersByTime(500) })

    expect(oldHandle.getItem()).toBe(JSON.stringify({ name: '旧身份待写', idCard: '' }))
    expect(newHandle.getItem()).toBeNull()
    clearTimeoutSpy.mockRestore()
  })
})

describe('useFormDraft · 降级', () => {
  it('sessionStorage.setItem 抛错时静默降级，不影响 hasDraft/draftValue', () => {
    const spy = vi.spyOn(window.sessionStorage.__proto__, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError')
    })
    const { result, rerender } = renderHook(
      ({ value }) => useFormDraft<Form>({
        scope: 'anonymous', formKey: 'f9', ...FORM_DRAFT_SCHEMA, value,
      }),
      { initialProps: { value: { name: '', idCard: '' } } },
    )
    expect(() => {
      act(() => { rerender({ value: { name: '不应抛错', idCard: '' } }) })
    }).not.toThrow()
    expect(result.current.hasDraft).toBe(false)
    spy.mockRestore()
  })

  it('sessionStorage 中存在非法 JSON 时静默返回 null（不抛错、不影响挂载）', () => {
    const key = anonymousDraftKey('f10')
    window.sessionStorage.setItem(key, '{not-json')
    const { result } = renderHook(() => useFormDraft<Form>({
      scope: 'anonymous',
      formKey: 'f10',
      ...FORM_DRAFT_SCHEMA,
      value: { name: '', idCard: '' },
    }))
    expect(result.current.hasDraft).toBe(false)
    expect(result.current.draftValue).toBeNull()
    expect(window.sessionStorage.getItem(key)).toBeNull()
  })
})
