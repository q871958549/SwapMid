'use client'

import { useEffect, useRef, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { Search, UsersRound } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { toast } from '@/components/ui/use-toast'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import type { AccountRoleDto } from '@/lib/api/types'

// 分配账号对话框用的账号行（account-users 列表字段子集）
interface AccountRow {
  id: string
  userName: string
  name: string | null
  roleNames: string[] | null
}

interface CopyForm {
  name: string
}

/**
 * 角色复制对话框（P0-11 前端接线）：以 `useCrudForm` 的 create 模式 + `buildUrl` 覆盖驱动
 * （AGENTS.md「不规则路径用 buildUrl 覆盖」，与「修改重提」POST {id}/resubmit 同型）。
 * 复制语义上更贴近「以源角色权限集为模板建一个新角色」而非「编辑源角色」——用 create 模式，
 * 成功后内建 toast 才会说「已创建」而非「已更新」，措辞准确；写操作反馈 5 点（inline formError
 * 等）全交给 useCrudForm + CrudFormDialog 内建，不再手写（呼应 eslint swapmid/prefer-crud-form）。
 *
 * 源角色不经 useCrudForm 的 row 通道传递（openCreate() 不接收行参数），改由 `source` prop
 * 闭包供 buildUrl 读取——闭包每次渲染重建，useCrudForm 内部 latest-ref 机制保证 submit() 时
 * 读到最新值（同 use-crud-form.ts 顶部注释的 optsRef 机制）。
 */
export function CopyRoleDialog({
  source,
  onClose,
  onSaved,
}: {
  /** 被复制的源角色；非空即打开。 */
  source: AccountRoleDto | null
  onClose: () => void
  /** 复制成功后立刻重取角色列表（router.refresh 往往刷不到当前页签）。 */
  onSaved?: () => void
}) {
  const t = useTranslations('Account')

  const crud = useCrudForm<CopyForm, AccountRoleDto>({
    entityName: t('common.roleEntity'),
    endpoint: 'account-roles',
    defaultForm: { name: '' },
    toForm: () => ({ name: '' }), // 复制不预填源角色名，留空待用户填新角色名
    toBody: (form) => ({ name: form.name }),
    buildUrl: () => `/api/proxy/account-roles/${source?.id}/copy`,
    onSuccess: () => onSaved?.(),
  })

  // source 变为新的非空值时打开（effect 而非渲染期调整——openCreate 内部多步 setState + await，
  // 放渲染期同步调用风险大，交给 effect 是标准时序）。
  useEffect(() => {
    if (source) void crud.openCreate()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source])

  // crud.isOpen 从 true 变 false（提交成功 或 取消/Esc/背景点击，均经 CrudFormDialog 内部 crud.close()）
  // 时通知父组件清空 copyTarget——否则再次点击同一行「复制」时 source 引用不变，上面的 effect 不会重新触发。
  const wasOpenRef = useRef(false)
  useEffect(() => {
    if (wasOpenRef.current && !crud.isOpen) onClose()
    wasOpenRef.current = crud.isOpen
  }, [crud.isOpen, onClose])

  return (
    <CrudFormDialog
      crud={crud}
      title={{
        create: t('role.copyDialogTitle', { name: source?.name ?? '' }),
        edit: t('role.copyDialogTitle', { name: source?.name ?? '' }),
      }}
      contentClassName="sm:max-w-[420px]"
      canSubmit={!!crud.form.name}
      submitLabel={t('role.copyConfirmButton')}
    >
      <p className="text-muted-foreground text-sm">{t('role.copyDialogDesc')}</p>
      <div className="space-y-1.5">
        <Label>{t('role.formNameLabel')}</Label>
        <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('role.formNamePlaceholder')} autoFocus />
      </div>
    </CrudFormDialog>
  )
}

/**
 * 分配账号对话框（P0-13 防误伤）：勾选集合 = 该角色的完整成员名单，保存前对比初始成员集算 diff，
 * diff 为空时保存按钮禁用；diff 非空先弹摘要确认（新增/移出人数，移出 > 0 时列出前 5 个姓名 + destructive
 * 样式），确认后才真正发 set-role-members 请求（全量同步语义不变，只是加了一道摘要确认）。
 *
 * 摘要确认没有用「同时挂载两个 Radix 模态」的写法（AlertDialog 嵌在 Dialog 内）——本仓对
 * DropdownMenu 嵌 Dialog 已有过一次生产事故（modal 叠加导致误判外部点击、丢表单，见 AGENTS.md
 * 「Dialog 内的 DropdownMenu 必须 modal={false}」铁律），Radix 官方也未把 Dialog/AlertDialog 嵌套
 * 列为免检项。稳妥起见改用「同一时刻只一个模态 open」的状态机：`confirming` 在两个 Dialog 的
 * open 条件间互斥切换，两步视觉上仍是「列表 → 确认摘要」，但底层任意时刻只有一个 Radix 模态挂载。
 */
export function AssignAccountsDialog({
  role,
  onClose,
  onSaved,
}: {
  /** 目标角色；非空即打开。 */
  role: AccountRoleDto | null
  onClose: () => void
  /** 成员变更成功后立刻重取角色列表，更新成员数。 */
  onSaved?: () => void
}) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  const router = useRouter()
  const [accounts, setAccounts] = useState<AccountRow[] | null>(null)
  const [checkedIds, setCheckedIds] = useState<Set<string>>(new Set())
  const [initialMemberIds, setInitialMemberIds] = useState<Set<string>>(new Set())
  const [memberSearch, setMemberSearch] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [saving, setSaving] = useState(false)

  // role 变为新的非空值：同步重置本地 UI 状态——渲染期按 prop 变化调整 state（react.dev 推荐范式，
  // 也是 eslint react-hooks/set-state-in-effect 的要求：纯同步 setState 不放 effect 顶层）。
  const [prevRole, setPrevRole] = useState(role)
  if (role !== prevRole) {
    setPrevRole(role)
    setConfirming(false)
    setMemberSearch('')
    if (role) setAccounts(null)
  }

  // 拉取成员列表：异步 I/O 必须放 effect（渲染期不能发请求）；effect 顶层本身不做任何同步 setState
  // （满足 react-hooks/set-state-in-effect），全部 setState 延后到 fetch resolve 后的回调里——
  // 该形态正是规则原文列举的允许写法（"calling setState in a callback function when external state
  // changes"）。含 cancelled 竞态防护（对话框快速关闭又重开时，晚到的旧响应不覆盖新状态）。
  useEffect(() => {
    if (!role) return
    let cancelled = false
    void (async () => {
      try {
        // 轻量接口：一次拉全部账号(id/userName/name/roleNames)，角色名批量映射，
        // 取代旧的 maxResultCount=1000 全量请求（逐账号 N+1 致弹窗加载慢的根因）。
        const res = await fetch('/api/proxy/account-users/for-assignment')
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as { items: AccountRow[] }
        if (cancelled) return
        const memberIds = new Set(data.items.filter(a => a.roleNames?.includes(role.name)).map(a => a.id))
        setAccounts(data.items)
        setInitialMemberIds(memberIds)
        setCheckedIds(new Set(memberIds))
      } catch (e) {
        if (cancelled) return
        console.error(e)
        setAccounts([])
        toast({ variant: 'destructive', title: t('common.accountListLoadFailedTitle'), description: tc('tryAgainLater') })
      }
    })()
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [role])

  function toggleMember(id: string) {
    setCheckedIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const added = (accounts ?? []).filter(a => checkedIds.has(a.id) && !initialMemberIds.has(a.id))
  const removed = (accounts ?? []).filter(a => !checkedIds.has(a.id) && initialMemberIds.has(a.id))
  const hasDiff = added.length > 0 || removed.length > 0

  async function confirmSave() {
    if (!role) return
    setSaving(true)
    try {
      const res = await fetch('/api/proxy/account-users/set-role-members', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ roleName: role.name, userIds: [...checkedIds] }),
      })
      if (!res.ok) throw new Error(await res.text())
      const r = (await res.json()) as { requested: number; succeeded: number; skipped: number }
      toast({
        variant: 'success',
        title: t('role.membersUpdatedTitle', { roleName: role.name }),
        description: r.requested === 0
          ? t('role.noMemberChange')
          : t('role.memberChangedDesc', { count: r.succeeded }) + (r.skipped ? t('common.skippedSuffix', { count: r.skipped }) : ''),
      })
      setConfirming(false)
      onClose()
      onSaved?.()
      router.refresh() // 失效 Router Cache；当前页签的成员数靠 onSaved 立刻重取
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('role.assignSaveFailedTitle'), description: t('common.retryOrCheckLogs') })
      setConfirming(false) // 回到成员列表，让用户可重试或调整勾选
    } finally {
      setSaving(false)
    }
  }

  const filteredAccounts = (accounts ?? []).filter(a => {
    const q = memberSearch.trim().toLowerCase()
    if (!q) return true
    return (a.name ?? '').toLowerCase().includes(q) || a.userName.toLowerCase().includes(q)
  })

  return (
    <>
      <Dialog open={!!role && !confirming} onOpenChange={(o) => { if (!o) onClose() }}>
        <DialogContent className="sm:max-w-[480px]" aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <UsersRound className="size-4" />
              {t('role.assignDialogTitle', { roleName: role?.name ?? '' })}
            </DialogTitle>
          </DialogHeader>
          {!accounts ? (
            <p className="text-muted-foreground py-8 text-center text-sm">{t('role.loadingAccounts')}</p>
          ) : (
            <div className="space-y-2.5">
              <div className="relative">
                <Search className="text-muted-foreground absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2" />
                <Input
                  value={memberSearch}
                  onChange={e => setMemberSearch(e.target.value)}
                  placeholder={t('role.searchMemberPlaceholder')}
                  className="h-8 pl-8 text-sm"
                />
              </div>
              <div className="max-h-80 divide-y overflow-y-auto rounded-md border">
                {filteredAccounts.map(a => (
                  <label key={a.id} className="hover:bg-muted/40 flex cursor-pointer items-center gap-2.5 px-3 py-2">
                    <Checkbox checked={checkedIds.has(a.id)} onCheckedChange={() => toggleMember(a.id)} />
                    <span className="text-sm font-medium">{a.name || a.userName}</span>
                    <span className="text-muted-foreground font-mono text-xs">@{a.userName}</span>
                  </label>
                ))}
                {accounts.length === 0 && (
                  <p className="text-muted-foreground py-6 text-center text-sm">{t('role.noAccounts')}</p>
                )}
              </div>
              <p className="text-muted-foreground text-xs">
                {t('role.selectedSummary', { checked: checkedIds.size, total: accounts.length })}
              </p>
            </div>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={onClose}>{tc('cancel')}</Button>
            <Button onClick={() => setConfirming(true)} disabled={!accounts || !hasDiff}>
              {tc('save')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={!!role && confirming}
        onOpenChange={(o) => { if (!o) setConfirming(false) }}
        title={t('role.assignConfirmTitle')}
        description={<AssignDiffSummary added={added} removed={removed} t={t} />}
        confirmText={t('role.assignConfirmButton')}
        busyText={tc('saving')}
        busy={saving}
        destructive={removed.length > 0}
        onConfirm={confirmSave}
      />
    </>
  )
}

const ASSIGN_DIFF_NAME_LIMIT = 5

function AssignDiffSummary({
  added,
  removed,
  t,
}: {
  added: AccountRow[]
  removed: AccountRow[]
  t: ReturnType<typeof useTranslations>
}) {
  const shownRemoved = removed.slice(0, ASSIGN_DIFF_NAME_LIMIT)
  const extraRemoved = removed.length - shownRemoved.length
  return (
    <>
      {t('role.assignDiffSummary', { added: added.length, removed: removed.length })}
      {removed.length > 0 && (
        // AlertDialogDescription 底层渲染 <p>，div/ul 等块级元素不能作为其子节点；
        // <span className="block"> 仍是合法的内联元素（phrasing content），css display 不影响 HTML 内容模型，
        // 借它实现另起一行，保持嵌套合法（同 role.deleteDesc 的 t.rich 惯例：只用行内元素）。
        <span className="text-destructive mt-1 block text-xs">
          {t('role.assignRemovedNamesPrefix', { count: removed.length })}
          {shownRemoved.map(a => a.name || a.userName).join(t('role.assignNameSeparator'))}
          {extraRemoved > 0 ? t('role.assignRemovedMoreSuffix', { count: extraRemoved }) : ''}
        </span>
      )}
    </>
  )
}
