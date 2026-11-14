'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { absorbPlatformUpdating } from '@/lib/platform-updating'

/**
 * CRUD 表单对话框状态机（数据层）。配 `<CrudFormDialog>`（展示层）使用。
 *
 * 抽掉的是每个 CRUD 页逐字重写、且最易出错的那段：新建/编辑共用一个对话框、
 * POST/PUT 分支、成功 toast、失败把后端中文错误就近内联、刷新列表、以及删除确认。
 *
 * ⚠️「写操作反馈 5 点」（见 AGENTS.md）在本 hook + `<CrudFormDialog>` 里**已内建**，
 * 页面无需也不应再手写 formError 的声明/清空/渲染：
 *   ① formError state —— 本 hook 持有
 *   ② openCreate/openEdit 清空 —— 本 hook 负责
 *   ③ 对话框关闭分支清空 —— `close()` 负责（`<CrudFormDialog>` 的 onOpenChange 调它）
 *   ④ submit try 首行清空 —— 本 hook 负责
 *   ⑤ catch setFormError + Footer 前渲染 —— 本 hook setState + `<CrudFormDialog>` 渲染
 *
 * 约定（覆盖本仓绝大多数 AppService 的 RESTful 形状）：
 *   - 新建：POST `/api/proxy/{endpoint}`
 *   - 编辑：PUT  `/api/proxy/{endpoint}/{id}`
 *   - 删除：DELETE `/api/proxy/{endpoint}/{id}`
 * 不规则路径用 `buildUrl` 覆盖。
 */

export type CrudMode = 'create' | 'edit'

/** `<CrudFormDialog>` 受控所需的最小切面；`useCrudForm` 的返回值是它的超集。 */
export interface CrudFormController {
  mode: CrudMode | null
  isOpen: boolean
  submitting: boolean
  formError: string | null
  close: () => void
  submit: () => Promise<void>
  /**
   * 表单相对「打开时的初始值」是否有改动。驱动 `<CrudFormDialog>` 的防误关拦截
   * （点遮罩 / Esc 时有改动才拦）。
   *
   * ⚠️ **是函数不是派生值**：只在事件处理器里调用。做成 render 期计算的布尔量会导致每次击键
   * 都 `JSON.stringify` 整个表单，且拿不到「最新一次 set 之后」的值而要读 ref——本仓 eslint 的
   * `react-hooks/refs` 禁止 render 期读写 ref。
   */
  isDirty: () => boolean
}

export interface UseCrudFormOptions<TForm, TRow> {
  /** 实体中文名，用于默认 toast 文案，如「岗位」「企业」。 */
  entityName: string
  /** 后端资源相对路径（不含 `/api/proxy/`），如 `positions`、`enterprises`。 */
  endpoint: string
  /** 新建时的空白表单。 */
  defaultForm: TForm
  /** 编辑时把整行映射成表单值。 */
  toForm: (row: TRow) => TForm
  /**
   * 表单映射成提交 body。是普通闭包，可直接读组件作用域里的参照数据
   * （如把选中企业/部门的 code 一并塞进 body）。
   */
  toBody: (form: TForm, mode: CrudMode, row: TRow | null) => unknown
  /**
   * 成功后的刷新动作。客户端分页页面省略即可（默认 `router.refresh()`）；
   * 服务端分页页面传 `useCrudResource` 的 `reload`。
   */
  onSuccess?: () => void
  /** 取实体显示名（toast 用），默认读 `.name`。无 name 字段的实体（如编码规则用 code）覆盖它。 */
  getName?: (item: TForm | TRow) => string
  /** 取行主键（PUT/DELETE 路径），默认 `.id`。 */
  getId?: (row: TRow) => string
  /** 覆盖默认 URL 拼接（不规则后端路由时用）。 */
  buildUrl?: (mode: CrudMode, row: TRow | null) => string
  /**
   * 编辑态提交方法，默认 'PUT'。少数后端把"编辑类"动作建成专属 POST 端点
   * （如需求单「修改重提」`POST {id}/resubmit`），此时配合 `buildUrl` 一起覆盖为 'POST'。
   * create 态恒为 POST，不受此项影响。
   */
  editHttpMethod?: 'PUT' | 'POST'
  /**
   * 打开「新建」前的钩子。可 await（如懒加载参照数据），返回的 patch 合并进表单
   * （如回填默认主体企业）。
   */
  beforeCreate?: () => void | Promise<Partial<TForm> | void>
  /** 打开「编辑」前的钩子（如懒加载参照数据，通常 `void ensureRefs()`）。 */
  beforeEdit?: (row: TRow) => void
  /**
   * 提交失败时先调用。返回字符串则作为内联错误；返回 null 表示调用方自行展示
   * （例如带链接的错误）。不返回则仍走 `getApiErrorMessage`。
   */
  onSubmitError?: (rawBody: string) => string | null | undefined
}

export interface UseCrudFormReturn<TForm, TRow> extends CrudFormController {
  /** 当前表单值。 */
  form: TForm
  /**
   * 局部更新表单：`set({ name: 'x' })`。
   * 异步回填（取价等）必须用函数式 `set(prev => …)`，按最新 form 合并，避免用打开时的旧行把刚选的物料盖掉。
   */
  set: (patch: Partial<TForm> | ((prev: TForm) => Partial<TForm>)) => void
  /** 编辑态的目标行（新建态为 null）。 */
  target: TRow | null
  openCreate: () => Promise<void>
  openEdit: (row: TRow) => void
  // —— 删除确认 ——
  deleteTarget: TRow | null
  askDelete: (row: TRow) => void
  closeDelete: () => void
  confirmDelete: () => Promise<void>
  deleting: boolean
}

const defaultName = (item: unknown) =>
  String((item as Record<string, unknown> | null)?.name ?? '')
const defaultId = (row: unknown) =>
  String((row as Record<string, unknown> | null)?.id ?? '')

export function useCrudForm<TForm, TRow>(
  options: UseCrudFormOptions<TForm, TRow>,
): UseCrudFormReturn<TForm, TRow> {
  const router = useRouter()
  // toast 文案模板走 i18n（Common namespace）；entityName 由调用方传入的已是翻译值，
  // 这里只翻译前后缀模板本身，避免「Department已更新」这类中英混排（蓝图批次 4a 发现的欠账）。
  const t = useTranslations('Common')
  // 选项每次 render 重建（toBody 等闭包要捕获最新参照数据），存进 ref 让稳定的回调读最新值。
  const optsRef = useRef(options)
  // React 19 禁止 render 期间写 ref；改用 effect 更新（latest-ref 模式）。事件/effect 中读到的总是最新 options。
  useEffect(() => { optsRef.current = options })

  const [form, setForm] = useState<TForm>(options.defaultForm)
  const [mode, setMode] = useState<CrudMode | null>(null)
  const [target, setTarget] = useState<TRow | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<TRow | null>(null)
  const [deleting, setDeleting] = useState(false)
  // 打开对话框那一刻的表单快照（JSON 串），供 isDirty 比对；关闭后置 null = 无基线 = 恒不 dirty。
  // create / edit 各自记各自的基线并只与自己比较，故 defaultForm 与 toForm 的键序无需一致；
  // 但**同一模式内**基线与后续 form 必须同序——`set` 走 `{ ...prev, ...patch }` 保持插入序，天然满足。
  const baselineRef = useRef<string | null>(null)

  const set = useCallback((patch: Partial<TForm> | ((prev: TForm) => Partial<TForm>)) => {
    setForm((prev) => {
      const resolved = typeof patch === 'function' ? patch(prev) : patch
      return { ...prev, ...resolved }
    })
  }, [])

  const close = useCallback(() => {
    setMode(null)
    setTarget(null)
    setFormError(null)
    baselineRef.current = null
  }, [])

  const openCreate = useCallback(async () => {
    const o = optsRef.current
    setFormError(null)
    setForm(o.defaultForm)
    setTarget(null)
    setMode('create')
    baselineRef.current = JSON.stringify(o.defaultForm)
    const patch = await o.beforeCreate?.()
    if (patch) {
      setForm((prev) => ({ ...prev, ...patch }))
      // beforeCreate 回填的默认值（如主体企业）属于「系统给的初值」而非用户输入，必须计进基线，
      // 否则一打开新建框就被判为 dirty、点遮罩永远关不掉。
      baselineRef.current = JSON.stringify({ ...o.defaultForm, ...patch })
    }
  }, [])

  const openEdit = useCallback((row: TRow) => {
    const o = optsRef.current
    setFormError(null)
    o.beforeEdit?.(row)
    const snapshot = o.toForm(row)
    setForm(snapshot)
    setTarget(row)
    setMode('edit')
    baselineRef.current = JSON.stringify(snapshot)
  }, [])

  const isDirty = useCallback(
    () => baselineRef.current !== null && JSON.stringify(form) !== baselineRef.current,
    [form],
  )

  const submit = useCallback(async () => {
    const o = optsRef.current
    const currentMode: CrudMode = mode === 'edit' ? 'edit' : 'create'
    const getId = o.getId ?? defaultId
    const getName = o.getName ?? defaultName
    const url =
      o.buildUrl?.(currentMode, target) ??
      (currentMode === 'edit'
        ? `/api/proxy/${o.endpoint}/${getId(target as TRow)}`
        : `/api/proxy/${o.endpoint}`)
    setSubmitting(true)
    setFormError(null)
    try {
      const res = await fetch(url, {
        method: currentMode === 'edit' ? (o.editHttpMethod ?? 'PUT') : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(o.toBody(form, currentMode, target)),
      })
      const responseText = await res.text()
      if (!res.ok) {
        if (absorbPlatformUpdating(responseText)) return
        const custom = o.onSubmitError?.(responseText)
        if (custom !== undefined) {
          setFormError(custom)
          return
        }
        throw new Error(responseText)
      }
      close()
      const name = getName(form)
      toast({
        variant: 'success',
        title:
          currentMode === 'edit'
            ? t('entityUpdatedTitle', { entityName: o.entityName })
            : t('entityCreatedTitle', { entityName: o.entityName }),
        description: name ? t('entitySavedDescription', { entityName: o.entityName, name }) : undefined,
      })
      // 即时刷新可见列表（onSuccess，服务端分页页的 list.reload——只改 React state，无整页闪烁）
      // 之后必须再 router.refresh()：客户端 reload 不失效 Next 的 Router Cache（staleTimes.dynamic 窗口内会
      // 缓存旧 RSC），「切走再切回」remount 时会读到旧 SSR 快照（如新建的行不见了）。router.refresh() 失效
      // 路由缓存，下次导航回本页重跑 SSR 拿最新数据。两者叠加=即时更新 + 切回不丢
      // （2026-07-08 修「项目列表切页签数据消失」；单靠 reload 或单靠 refresh 都不够）。
      o.onSuccess?.()
      router.refresh()
    } catch (e) {
      console.error(`[${o.endpoint}/submit]`, e)
      setFormError(getApiErrorMessage(e, t('saveFailedFallback')))
    } finally {
      setSubmitting(false)
    }
  }, [mode, target, form, close, router, t])

  const askDelete = useCallback((row: TRow) => setDeleteTarget(row), [])
  const closeDelete = useCallback(() => setDeleteTarget(null), [])

  const confirmDelete = useCallback(async () => {
    const o = optsRef.current
    const row = deleteTarget
    if (!row) return
    const getId = o.getId ?? defaultId
    const getName = o.getName ?? defaultName
    const name = getName(row)
    setDeleting(true)
    try {
      const res = await fetch(`/api/proxy/${o.endpoint}/${getId(row)}`, { method: 'DELETE' })
      const responseText = await res.text()
      if (!res.ok) {
        if (absorbPlatformUpdating(responseText)) return
        throw new Error(responseText)
      }
      setDeleteTarget(null)
      toast({
        variant: 'success',
        title: t('deletedTitle'),
        description: name
          ? t('deletedWithNameDescription', { entityName: o.entityName, name })
          : t('deletedNoNameDescription', { entityName: o.entityName }),
      })
      // 同 submit：onSuccess 即时刷新列表 + router.refresh() 失效 Router Cache，防切页签返回读到旧 RSC。
      o.onSuccess?.()
      router.refresh()
    } catch (e) {
      console.error(`[${o.endpoint}/delete]`, e)
      toast({
        variant: 'destructive',
        title: t('deleteFailedTitle'),
        description: getApiErrorMessage(e, t('deleteFailedFallback')),
      })
    } finally {
      setDeleting(false)
    }
  }, [deleteTarget, router, t])

  return {
    form,
    set,
    mode,
    isOpen: mode !== null,
    target,
    submitting,
    formError,
    openCreate,
    openEdit,
    close,
    submit,
    isDirty,
    deleteTarget,
    askDelete,
    closeDelete,
    confirmDelete,
    deleting,
  }
}
