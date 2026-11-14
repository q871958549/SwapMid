'use client'

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import { TriangleAlert } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, type DialogSize,
} from '@/components/ui/dialog'
import { MissingFieldsHint } from '@/components/data-table/missing-fields-hint'
import { type CrudFormController } from '@/lib/crud/use-crud-form'
import { cn } from '@/lib/utils'

/**
 * CRUD 新建/编辑对话框的展示层壳，受控于 `useCrudForm` 返回的 controller。
 *
 * 内建「写操作反馈 5 点」中的 ③（关闭清错）④⑤（提交清错 + 渲染错误），
 * 以及 Dialog 无障碍（固定 `aria-describedby={undefined}`，标题已自解释）。
 * 字段作为 children 传入，用 `crud.form` / `crud.set` 读写。
 *
 * 🔴 **防误关（脏检查拦截，全部调用方零改动生效）**：表单相对打开时有改动（`crud.isDirty()`）时，
 * 点遮罩与按 Esc 会被拦下，并短暂高亮边框 + 显示一行 `role="status"` 提示；无改动时照常关闭。
 * 「取消」按钮与右上角 X 不走这条路径——它们是精确点击目标、不存在误触，任何时候都能直接关。
 * 判据：**丢的是用户敲进去的东西才拦，丢的只是一次浏览不拦**。字段多的长表单最易在滚动或
 * 挪动鼠标时点出弹框外，此前一律直接关闭、已填内容全丢。
 *
 * 🔴 **三段式布局（header 固定 / 字段区内滚 / 提示+footer 固定）**：`ui/dialog.tsx` 的
 * `DialogContent` 基类是**单一滚动容器**（`max-h-[90vh] overflow-y-auto`），header、字段、提示条、
 * footer 全在流内一起滚。长表单下的后果是**拦截提示与「取消/保存」按钮双双被推出视口**——用户
 * 视线停在表单中上部点遮罩，弹窗纹丝不动又看不到任何解释，主观体验就是「有时候点外面关不掉」。
 * 故本壳把滚动下沉到中间字段区：`DialogContent` 用 `overflow-y-hidden` 关掉外层滚动（与基类
 * `overflow-y-auto` **同属 twMerge 的 `overflow-y` 组，必被覆盖**——勿改写成 `overflow-hidden`，
 * 那是 `overflow` 组、不同组不互相覆盖，最终由 Tailwind 产物里的属性顺序决定胜负、不可靠），
 * 字段区 `min-h-0 flex-1 overflow-y-auto` 吸收全部溢出。`guardFlash` 提示与 `crud.formError`
 * **必须留在滚动区之外**：footer 固定后用户不再需要滚到底才能点「保存」，错误若仍在滚动区末尾
 * 会复现同一个「看不见」。
 * ⚠️ 调用方的 `contentClassName` 不要再传 `overflow-*` / `max-h-*`（会在 twMerge 里反压回整体滚动）。
 *
 * @example
 * <CrudFormDialog
 *   crud={crud}
 *   title={{ create: '新建岗位', edit: '编辑岗位' }}
 *   canSubmit={!!crud.form.code && !!crud.form.name}
 *   contentClassName="sm:max-w-[520px]"
 * >
 *   <div className="space-y-1.5">
 *     <Label>岗位名称 *</Label>
 *     <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} />
 *   </div>
 * </CrudFormDialog>
 *
 * @example 表单 footer 需要额外按钮（如「测试连接」）时用 footerExtra 注入——
 * 收编原本只能手写 Dialog 的「半迁」页（如 data-sources）：
 * <CrudFormDialog crud={crud} title={...} footerExtra={
 *   <Button variant="outline" size="sm" onClick={testConnection}>测试连接</Button>
 * }>...</CrudFormDialog>
 *
 * @example 「保存」旁需要并排的次要动作按钮（如「保存并打印标签」）时用 submitExtra 注入——
 * 渲染在「取消」与「保存」之间，调用方传入完整 <Button> 自行控制 variant/onClick/disabled/文案：
 * <CrudFormDialog crud={crud} title={...} submitExtra={crud.mode === 'create' ? (
 *   <Button variant="secondary" onClick={handleSaveAndPrint} disabled={!canSubmit}>保存并打印标签</Button>
 * ) : undefined}>...</CrudFormDialog>
 */
interface CrudFormDialogProps {
  crud: CrudFormController
  /** 按 create / edit 模式切换的标题。 */
  title: { create: string; edit: string }
  children: ReactNode
  /** 必填校验：false 时禁用「保存」（不传视为可提交，仍受 submitting 约束）。 */
  canSubmit?: boolean
  /**
   * 还缺哪些必填项（**已翻译的字段显示名**）。非空时在 footer 上方显示一行
   * 「还需填写：X、Y」，让被禁用的「保存」把原因说出来——`canSubmit=false` 本身是无声的，
   * 用户只看到灰按钮、不知道差什么（2026-08-11 用户反馈）。
   *
   * 与 `canSubmit` **刻意保持两个独立入参**而不是从这里推导禁用：不少页面的可提交条件
   * 不止「非空」（如数值范围、两字段互斥、异步查重），把两者绑死会逼这些页面编造假字段名。
   * 常见页面直接 `canSubmit={missing.length === 0}` 即可，一处定义两处消费。
   */
  missingFields?: string[]
  /** 提交按钮文案，默认「保存」。 */
  submitLabel?: string
  /** 提交中按钮文案，默认「保存中...」。 */
  submittingLabel?: string
  /** 主提交按钮样式，默认 default。单据页把「保存草稿」降成 outline 时用。 */
  submitVariant?: 'default' | 'secondary' | 'outline'
  /** 对话框桌面端标准尺寸档位；传入时替代默认 520px 宽度，避免手写任意像素类。 */
  size?: DialogSize
  /** DialogContent 额外类名（控制宽度等），未传 `size` 时默认 `sm:max-w-[520px]`。 */
  contentClassName?: string
  /**
   * 注入到 footer 左侧的自定义内容（如「测试连接」按钮 + 连接结果）。
   * 传入时 footer 切为两端对齐（左 footerExtra / 右 取消+保存）；
   * 不传则维持默认（取消/保存右对齐），现有页零影响。
   * 让带额外 footer 按钮的表单也能用本壳，免去手写整个 Dialog。
   */
  footerExtra?: ReactNode
  /**
   * 渲染在 footer 提交区、「取消」与「保存」之间的并排次要动作按钮
   * （如样品登记的「保存并打印标签」）。调用方传入完整的 `<Button>`，
   * 自行控制 variant/onClick/disabled/文案；本壳只负责摆位。
   * 与 `footerExtra` 区分：`footerExtra` 注入 footer 最左（两端对齐，放「测试连接」类）；
   * `submitExtra` 紧邻主「保存」左侧、属于右侧提交动作区。
   */
  submitExtra?: ReactNode
  /** 固定在字段滚动区外的补充提示，例如带链接的业务错误。 */
  notice?: ReactNode
}

export function CrudFormDialog({
  crud,
  title,
  children,
  canSubmit,
  missingFields,
  submitLabel,
  submittingLabel,
  submitVariant = 'default',
  size,
  contentClassName,
  footerExtra,
  submitExtra,
  notice,
}: CrudFormDialogProps) {
  const t = useTranslations('Common')
  // 防误关的可见反馈：被拦截时短暂高亮边框 + 显示一行警示提示，让用户知道「不是卡了，是有未保存内容」。
  // 计数器而非布尔：连点遮罩时递增值作为提示条的 key，React 重建节点让 `.animate-shake` 重播——
  // 布尔量在提示已显示时再次置 true 不产生任何变化，第二次点击就没有反馈了。0 = 不显示。
  const [guardFlashSeq, setGuardFlashSeq] = useState(0)
  const flashTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => () => { if (flashTimerRef.current) clearTimeout(flashTimerRef.current) }, [])

  // 点遮罩 / 按 Esc 时的脏检查拦截：有改动才拦，没改动照常关（无输入可丢时拦截只会显得啰嗦）。
  // 「取消」按钮与右上角 X 走 crud.close，不经此路径——精确点击目标不存在误触，永远能关。
  const guardDismiss = useCallback((e: Event) => {
    if (!crud.isDirty()) return
    e.preventDefault()
    setGuardFlashSeq(n => n + 1)
    if (flashTimerRef.current) clearTimeout(flashTimerRef.current)
    flashTimerRef.current = setTimeout(() => setGuardFlashSeq(0), 2600)
  }, [crud])
  const guardFlash = guardFlashSeq > 0
  const hasMissing = (missingFields?.length ?? 0) > 0

  const resolvedSubmitLabel = submitLabel ?? t('save')
  const resolvedSubmittingLabel = submittingLabel ?? t('saving')
  const submitDisabled = crud.submitting || canSubmit === false
  const saveButton = (
    <Button
      variant={submitVariant}
      className="max-sm:h-11"
      onClick={() => void crud.submit()}
      disabled={submitDisabled}
    >
      {crud.submitting ? resolvedSubmittingLabel : resolvedSubmitLabel}
    </Button>
  )
  const actions = (
    <>
      <Button variant="outline" className="max-sm:h-11" onClick={crud.close}>{t('cancel')}</Button>
      {submitExtra}
      {saveButton}
    </>
  )
  return (
    <Dialog open={crud.isOpen} onOpenChange={(o) => { if (!o) crud.close() }}>
      <DialogContent
        size={size}
        className={cn(
          !size && 'sm:max-w-[520px]',
          'overflow-y-hidden transition-shadow duration-200',
          guardFlash && 'ring-2 ring-warning/70',
          contentClassName,
        )}
        aria-describedby={undefined}
        onPointerDownOutside={guardDismiss}
        onEscapeKeyDown={guardDismiss}
      >
        <DialogHeader className="shrink-0">
          <DialogTitle>{crud.mode === 'edit' ? title.edit : title.create}</DialogTitle>
        </DialogHeader>
        {/* 唯一滚动区。min-h-0 必须有：flex 子项默认 min-height:auto，不显式放开则永远撑到内容高度、
            flex-1 形同虚设，溢出又会顶回外层。-mx-1/px-1 给 input 的 focus ring 让出 4px，
            否则贴容器边缘的字段 ring 会被滚动容器裁掉一圈。 */}
        <div data-slot="crud-form-fields" className="scrollbar-fade -mx-1 grid min-h-0 flex-1 gap-3 overflow-y-auto px-1 py-2">
          {children}
        </div>
        {/* 提示区固定在滚动区外（见文件头三段式说明）：拦截提示与提交错误一旦随字段滚走就等于没有反馈。
            data-slot 供回归测试断言「提示不在 crud-form-fields 滚动区内」——这是本次改动的核心不变量。 */}
        {(guardFlash || crud.formError || hasMissing || notice) && (
          <div data-slot="crud-form-notices" className="shrink-0 space-y-2">
            {/* 提交中不再提示缺项：此刻表单必然已齐，留着只会和「保存中…」互相矛盾 */}
            {hasMissing && !crud.submitting && <MissingFieldsHint fields={missingFields ?? []} />}
            {guardFlash && (
              <p
                key={guardFlashSeq}
                role="status"
                className="animate-shake flex items-center gap-2 rounded-md border border-warning/40 bg-warning-soft px-3 py-2 text-sm text-warning"
              >
                <TriangleAlert className="size-4 shrink-0" />
                {t('unsavedChangesGuardHint')}
              </p>
            )}
            {notice}
            {crud.formError && (
              <p className="text-destructive bg-destructive/10 rounded-md px-3 py-2 text-sm">{crud.formError}</p>
            )}
          </div>
        )}
        {footerExtra ? (
          <DialogFooter className="shrink-0 sm:justify-between">
            <div className="flex min-w-0 items-center gap-2">{footerExtra}</div>
            <div className="flex shrink-0 gap-2">{actions}</div>
          </DialogFooter>
        ) : (
          <DialogFooter className="shrink-0">{actions}</DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  )
}
