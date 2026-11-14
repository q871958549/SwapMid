'use client'

import { useCallback, useEffect, useMemo, useRef, useSyncExternalStore } from 'react'
import { useOptionalAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import {
  createAnonymousDraftRawHandle,
  createIdentityDraftRawHandle,
  type BoundRawStorageHandle,
} from '@/lib/browser-storage-policy'

/**
 * P2 表单草稿恢复（见 docs/page-switch-keepalive-solution.md §四 P2）共享 hook。
 *
 * 用途：填写成本高、易被打断（切走页面/关标签/网络波动）的表单，按显式 scope 节流保存草稿。
 * 匿名流程继续使用 `swapmid-draft:<formKey>`；已认证流程通过 browser-storage-policy 绑定当前
 * identity namespace，避免退出、换号或模拟登录后串稿。重进时若存在草稿则供调用方预填 + 渲染
 * 「已恢复未提交草稿」提示与「清除草稿」入口；提交成功后调用方调用 `clearDraft()`。
 *
 * 设计取舍：
 * - **检测走 `useSyncExternalStore`**（而非 `useState`+`useEffect` 读一次再 setState）：sessionStorage
 *   是纯客户端外部存储，SSR 首帧无 `window`；`getServerSnapshot` 恒返回 `null`，hydration 首帧与服务端
 *   一致、不产生 mismatch，客户端订阅后立即以真实内容重渲染——写法对齐 `lib/recent-visits.ts` /
 *   `dashboard/recent-visits.tsx`。这也顺带规避了 React 19 `react-hooks/set-state-in-effect`
 *   （effect 里直接同步 setState 现在是 lint error，可复用 `use-dictionary.ts` 等既有先例）。
 * - **写入**（`value` 变化节流落盘）走单独的 `useEffect`，只做 `sessionStorage.setItem` + 派发一个
 *   全局 `CustomEvent` 通知 `useSyncExternalStore` 的订阅者刷新快照，不触碰 React state，不受
 *   上述规则约束。
 * - **`formKey` 变化即视为「新一轮追踪」**：对话框型表单把 `formKey` 设计为随「新建/编辑目标 id/
 *   对话框开关」变化（如 `'article:new'` / `'article:idle'`(关闭态) / `'article:<id>'`），每次打开
 *   对话框都会让 `formKey` 变化，从而重新检测该目标是否有存量草稿。
 * - **恢复不由本 hook 自动应用**：hook 只负责"检测到什么"，是否/如何合并进表单 state 由调用方决定
 *   （通常在检测到 `hasDraft` 时用一次性 effect 合并，见下方用法示例），hook 不对表单结构做假设。
 * - **首次挂载/formKey 变化后的第一次写入会被跳过**：若不跳过，"检测到草稿→调用方尚未来得及把
 *   draftValue 合并回 `value`"这段窗口期，写入 effect 会用尚是默认值的 `value` 把刚检测到的草稿覆盖掉
 *   （清空），是本 hook 最容易踩的坑，务必保留这条防御逻辑。
 * - 🔴 **写入内容判等抑制**（2026-08 补）：写入 effect 的依赖含 `value`，触发条件是「**引用**变化」。
 *   调用方把 `value` 写成内联字面量（`value: { head, groups }`）时，**任何无关 state 变化都会调度一次
 *   写入**，把与库里完全一致的表单值当草稿落盘；下次打开便凭空亮起「已为您恢复未提交的草稿」，
 *   内容与库里一模一样、纯噪声（2026-08-06 计划模板编辑器实证）。故 `flush` 落盘前做两道内容判等：
 *   等于本轮基线（handle 就位那一刻的表单值）或等于 storage 现值即跳过。它与上一条 `skipNextWriteRef`、
 *   以及 `clearDraft` 之后的跳过是**三道独立防线**，方向一致但各治各的场景，勿因为"已经有判等了"
 *   而删掉另外两条。
 * - SSR 防护：读写全部包 `typeof window === 'undefined'` 早退 + try/catch 静默降级（sessionStorage
 *   在隐私模式/配额已满时可能抛错，不应因此打断表单填写），写法对齐 `components/layout/scroll-restorer.tsx`。
 *
 * @example
 * const draft = useFormDraft<FormState>({
 *   scope: 'anonymous',
 *   formKey: `eval:${token}`,
 *   schemaVersion: 1,
 *   validator: isEvalDraft,
 *   value: { answers, comment },
 *   enabled: !readOnly && !success,
 * })
 * const appliedRef = useRef(false)
 * useEffect(() => {
 *   if (appliedRef.current || !draft.hasDraft || !draft.draftValue) return
 *   appliedRef.current = true
 *   setAnswers(prev => ({ ...prev, ...draft.draftValue!.answers }))
 *   setComment(draft.draftValue!.comment)
 * }, [draft.hasDraft, draft.draftValue])
 * // 提交成功：draft.clearDraft()
 */

const CHANGE_EVENT = 'swapmid-form-draft-change'

function isValidDraftValue(
  value: unknown,
  validator: (value: unknown) => boolean,
): boolean {
  try {
    return validator(value)
  } catch {
    return false
  }
}

function isSerializedDraft(
  value: string,
  validator: (value: unknown) => boolean,
): boolean {
  try {
    return isValidDraftValue(JSON.parse(value) as unknown, validator)
  } catch {
    return false
  }
}

/**
 * 按 omitKeys 裁剪出真正会落盘的对象。
 * 🔴 写入与基线判等必须共用这一份裁剪逻辑——只裁写入不裁基线，带 `omitKeys` 的调用点
 * （如 interview-register 剔除 idCard）基线与写入值永远不相等，判等抑制会整个失效。
 */
function applyOmitKeys<T extends object>(value: T, omitKeys: (keyof T)[] | undefined): T {
  if (!omitKeys || omitKeys.length === 0) return value
  return Object.fromEntries(
    Object.entries(value).filter(([key]) => !omitKeys.includes(key as keyof T)),
  ) as T
}

/** JSON.stringify 的安全版：不可序列化时返回 null，调用方据此放弃判等、回退为照常写入。 */
function safeStringify(value: unknown): string | null {
  try {
    const raw = JSON.stringify(value)
    return raw === undefined ? null : raw
  } catch {
    return null
  }
}

function writeDraft(handle: BoundRawStorageHandle, value: unknown) {
  try {
    const raw = JSON.stringify(value)
    if (raw === undefined) return
    handle.setItem(raw)
    if (typeof window !== 'undefined') window.dispatchEvent(new Event(CHANGE_EVENT))
  } catch {
    // JSON 序列化或浏览器存储不可用时静默放弃，不影响表单本身。
  }
}

function removeDraft(handle: BoundRawStorageHandle) {
  handle.removeItem()
  if (typeof window !== 'undefined') window.dispatchEvent(new Event(CHANGE_EVENT))
}

function subscribe(callback: () => void): () => void {
  if (typeof window === 'undefined') return () => {}
  window.addEventListener(CHANGE_EVENT, callback)
  return () => window.removeEventListener(CHANGE_EVENT, callback)
}

/** SSR / hydration 首帧快照：恒为 null（客户端订阅后立即以真实 sessionStorage 内容重渲染）。 */
function getServerSnapshot(): string | null {
  return null
}

export interface UseFormDraftOptions<T extends object> {
  /**
   * 匿名 capability 表单用 anonymous；登录后业务表单必须用 identity。
   * 必须显式声明，禁止根据 Provider 是否存在隐式猜测。
   */
  scope: 'anonymous' | 'identity'
  /**
   * 草稿标识。须包含足以区分"这是哪份表单/哪个条目"的信息（如表单名 + 考评 token、表单名 +
   * 文章 id/'new'），否则不同表单/不同条目会互相串稿。
   */
  formKey: string
  /**
   * 业务草稿结构版本（正整数）。版本会进入实际存储键；结构变更时递增，旧版本不会被自动读取。
   */
  schemaVersion: number
  /**
   * 草稿业务形状校验器。恢复与写入都必须通过；应验证调用方后续会直接访问的全部字段/嵌套结构。
   * 建议定义为模块级稳定函数，避免每次渲染创建新引用。
   */
  validator: (value: unknown) => boolean
  /** 当前表单值，变化时节流写入。必须是可 JSON 序列化的纯数据。 */
  value: T
  /** 是否记录草稿，默认 true。对话框型表单建议传 `crud.isOpen`：关闭时不追踪写入。 */
  enabled?: boolean
  /** 敏感字段：写入前从 value 中剔除，永不落 storage（身份证号、薪酬金额等 PII/脱敏域）。 */
  omitKeys?: (keyof T)[]
  /** 节流间隔（ms），默认 500。 */
  throttleMs?: number
}

export interface UseFormDraftReturn<T> {
  /**
   * 当前 formKey 下是否存在尚未清除的草稿。
   * ⚠️ **不要直接拿它当「已恢复草稿」横幅的显示条件**——它的语义是「storage 里*现在*有一份草稿」，
   * 而横幅要表达的是「*本次打开时*恢复了之前的草稿」。用户本次编辑产生的写入同样会让它变 true，
   * 横幅便会在编辑到一半时凭空冒出来。正确姿势见各调用点：在一次性应用 effect 里判等（等值草稿
   * 直接 `clearDraft()` 并 return）、真应用时才置一个 `restoredBanner` 标记，横幅绑
   * `restoredBanner && hasDraft`（后者保证点「清除草稿」后横幅立刻消失）。
   */
  hasDraft: boolean
  /** 检测到的草稿值（已按 omitKeys 剔除敏感字段，即写入时的裁剪结果）；无草稿为 null。 */
  draftValue: T | null
  /** 清除当前 formKey 对应的草稿（提交成功 / 用户点击「清除草稿」时调用）。 */
  clearDraft: () => void
}

export function useFormDraft<T extends object>({
  scope,
  formKey,
  schemaVersion,
  validator,
  value,
  enabled = true,
  omitKeys,
  throttleMs = 500,
}: UseFormDraftOptions<T>): UseFormDraftReturn<T> {
  if (!Number.isInteger(schemaVersion) || schemaVersion <= 0) {
    throw new Error('useFormDraft schemaVersion must be a positive integer')
  }

  const authenticatedStorage = useOptionalAuthenticatedBrowserStorage()
  const versionedFormKey = `${formKey}:v${schemaVersion}`
  const handle = useMemo(() => {
    if (scope === 'anonymous') return createAnonymousDraftRawHandle(versionedFormKey)
    if (!authenticatedStorage) {
      throw new Error('SessionIdentityProvider is required for identity form drafts')
    }
    return createIdentityDraftRawHandle(
      authenticatedStorage,
      versionedFormKey,
      (raw) => isSerializedDraft(raw, validator),
    )
  }, [authenticatedStorage, scope, validator, versionedFormKey])

  const getSnapshot = useCallback(() => {
    const raw = handle.getItem()
    if (!raw) return null
    if (isSerializedDraft(raw, validator)) return raw
    // 匿名 handle 不带策略层 validator；已认证 handle 即使底层未来放宽，也在这里二次防御。
    handle.removeItem()
    return null
  }, [handle, validator])
  const rawDraft = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)

  // rawDraft（JSON 字符串）按内容 memo 出解析结果，避免 raw 未变时每次 render 都产出新对象引用
  // （draftValue 常被调用方放进 useEffect 依赖数组，新引用会导致该 effect 无谓重跑）。
  const draftValue = useMemo<T | null>(() => {
    if (!rawDraft) return null
    try {
      const parsed = JSON.parse(rawDraft) as unknown
      return isValidDraftValue(parsed, validator) ? parsed as T : null
    } catch {
      return null
    }
  }, [rawDraft, validator])
  const hasDraft = draftValue !== null

  // 每次 render 都更新，flush 时读最新值。放进 effect 而非 render 期间直接写 ref——React 19 起
  // render 期间只允许"惰性初始化"式的 ref 读写，其余一律要放进 effect（react-hooks/refs），
  // 写法对齐 use-crud-form.ts 的 optsRef "latest-ref" 模式。omitKeys 不作为下方写入 effect 的
  // 依赖项（避免调用方传内联数组字面量导致每次 render 都触发重新调度），故也经由本 ref 读取。
  interface WriteContext {
    value: T
    handle: BoundRawStorageHandle | null
    omitKeys: (keyof T)[] | undefined
    validator: (value: unknown) => boolean
  }
  const writeCtxRef = useRef<WriteContext>({ value, handle, omitKeys, validator })
  const currentHandleRef = useRef(handle)
  useEffect(() => {
    writeCtxRef.current = { value, handle, omitKeys, validator }
    currentHandleRef.current = handle
  })

  const timerRef = useRef<number | null>(null)
  const timerContextRef = useRef<WriteContext | null>(null)
  const lastFlushRef = useRef<{ handle: BoundRawStorageHandle | null; at: number }>({
    handle: null,
    at: 0,
  })
  // handle 变化后的第一次写入 effect 应跳过（见文件头设计取舍说明）。
  const skipNextWriteRef = useRef<{ handle: BoundRawStorageHandle | null } | null>(null)
  // 本轮追踪的基线：handle 就位那一刻的表单值（已裁剪 + 序列化）。见文件头「写入内容判等抑制」。
  const baselineRef = useRef<{ handle: BoundRawStorageHandle | null; raw: string | null }>({
    handle: null,
    raw: null,
  })

  const flush = useCallback((context: WriteContext) => {
    const { value: v, handle: targetHandle, omitKeys: omit, validator: targetValidator } = context
    if (!targetHandle) return
    // 迟到的旧 identity 定时器只更新旧 namespace，不能改变新 identity 的节流时钟。
    if (lastFlushRef.current.handle === targetHandle) {
      lastFlushRef.current.at = Date.now()
    }
    const toWrite = applyOmitKeys(v, omit)
    // 拒绝非法业务形状，但保留已存在的最后一份合法草稿。
    if (!isValidDraftValue(toWrite, targetValidator)) return
    // 🔴 内容判等抑制（见文件头）：等于本轮基线 → 用户还没改出任何差异，写了只会让下次打开凭空
    // 亮起「已为您恢复未提交的草稿」；等于 storage 现值 → 幂等写，省一次 setItem + 事件广播。
    // 判等只跳过写入、**不删除**已有草稿——「改回初值即撤销草稿」是另一层语义，交调用方恢复时判等处理。
    const nextRaw = safeStringify(toWrite)
    if (nextRaw !== null) {
      if (baselineRef.current.handle === targetHandle && baselineRef.current.raw === nextRaw) return
      if (targetHandle.getItem() === nextRaw) return
    }
    writeDraft(targetHandle, toWrite)
  }, [])

  // handle 变化（formKey / scope / identity 变化）即视为新一轮追踪：取消旧计时器并重置节流状态。
  useEffect(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }
    timerContextRef.current = null
    lastFlushRef.current = { handle, at: 0 }
    skipNextWriteRef.current = { handle }
    // 基线取当帧的 value：本 effect 声明在上方 writeCtxRef 更新 effect 之后，同一次提交内按声明顺序
    // 执行，故这里读到的必定是本帧值（勿把本 effect 挪到那个 effect 之前，基线会落后一帧）。
    const ctx = writeCtxRef.current
    baselineRef.current = { handle, raw: safeStringify(applyOmitKeys(ctx.value, ctx.omitKeys)) }
  }, [handle])

  // 写入：value 变化时节流落盘（真节流——窗口内多次变化只在窗口尾触发一次写入，用最新值，
  // 而非每次变化都重新计时/每次都写）。
  useEffect(() => {
    if (!enabled || !handle) {
      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
      }
      timerContextRef.current = null
      return
    }
    if (skipNextWriteRef.current?.handle === handle) {
      skipNextWriteRef.current = null
      return
    }
    const context = writeCtxRef.current
    const lastFlushAt =
      lastFlushRef.current.handle === handle ? lastFlushRef.current.at : 0
    const elapsed = Date.now() - lastFlushAt
    if (lastFlushAt === 0 || elapsed >= throttleMs) {
      flush({ ...context })
    } else if (timerRef.current === null) {
      const timerContext = { ...context }
      timerContextRef.current = timerContext
      timerRef.current = window.setTimeout(() => {
        timerRef.current = null
        if (timerContextRef.current === timerContext) timerContextRef.current = null
        flush(timerContext)
      }, throttleMs - elapsed)
    } else if (timerContextRef.current?.handle === handle) {
      // 同一 identity 的节流窗口保留同一目标 handle，只更新待写值。
      timerContextRef.current.value = context.value
      timerContextRef.current.omitKeys = context.omitKeys
      timerContextRef.current.validator = context.validator
    }
  }, [value, enabled, handle, throttleMs, flush])

  // 卸载：清掉尚未触发的计时器，避免组件卸载后仍执行副作用。
  useEffect(() => () => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current)
    timerContextRef.current = null
  }, [])

  const clearDraft = useCallback(() => {
    if (!handle) return
    // 旧 identity 留存的回调只清自己的 namespace，不得取消或重置新 identity 的写入状态。
    if (currentHandleRef.current === handle) {
      if (timerRef.current !== null && timerContextRef.current?.handle === handle) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
        timerContextRef.current = null
      }
      lastFlushRef.current = { handle, at: Date.now() }
      skipNextWriteRef.current = { handle }
    }
    // 🔴 调用方的典型用法是「clearDraft() 紧接着把表单值重置为默认值」（见 client.tsx 的
    // clearDraftAndReset/handleClearDraft）——那次重置会触发 value 变化，进而重新调度一次写入
    // effect。若不拦截，~throttleMs 后会把"刚重置的默认表单"当新草稿写回 storage，让 hasDraft
    // 复活（字段全空但提示条重新出现）。借用 skipNextWriteRef 跳过 clearDraft 之后的下一次写入，
    // 之后用户真正开始重新填写时（再一次 value 变化）正常恢复节流写入。
    removeDraft(handle)
  }, [handle])

  return { hasDraft, draftValue, clearDraft }
}
