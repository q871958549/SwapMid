'use client'

import { useEffect, useRef, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Ban, KeyRound, Search, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { DropdownButton } from '@/components/data-table/dropdown-button'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { submitBatchIds, submitInChunks, type BatchChunkProgress } from '@/lib/crud/batch-chunk'
import { useLazyRef } from '@/lib/use-lazy-ref'
import type { AccountRoleDto } from '@/lib/api/types'
import { ACCOUNT_PROVISION_CHUNK_SIZE, EMPLOYEE_LIST_MAX_PAGE_SIZE } from './constants'

type AccountProvisionPart = {
  created?: number
  alreadyBound?: number
  skipped?: number
  requested?: number
  messages?: string[]
}

type AccountProvisionProgress = {
  phase: 'listing' | 'provisioning'
  done: number
  total: number
  created: number
  alreadyBound: number
  skipped: number
}

/**
 * toast 里最多列几条逐项失败原因。取 5 是因为 toast 4s 自动消失——再多也读不完，
 * 反而把「新建 N / 跳过 M」这行汇总挤出视野。超出部分只报条数。
 */
const BATCH_MESSAGE_PREVIEW = 5

/**
 * 员工批量操作（一键开通 / 批量生成账号 / 批量状态 / 批量删除）。
 * 从 employees/client.tsx 拆出，让主文件聚焦「列表 + 表单 + 单删」。
 * 批量操作的 payload 各异（非标准 CRUD），故不走 useCrudForm，但确认弹窗复用 <ConfirmDialog>。
 *
 * 批量按 id 列表下发（后端 batch-* 接口收 `{ ids }`），前端传 DataTable 的 `selectedIds`
 * ——即「跨页累积的全部选中 id」，故服务端分页下也能对全部选中批量，而非仅当前页。
 *
 * @param reload 写成功后重取列表（传 useCrudResource 的 reload）
 */
export function useEmployeeBulkActions(reload: () => void) {
  const t = useTranslations('Enterprise')
  const tc = useTranslations('Common')
  const [batchBusy, setBatchBusy] = useState(false)
  const [bulkOpen, setBulkOpen] = useState(false)
  const [bulkRoleNames, setBulkRoleNames] = useState<string[]>([])
  const [batchGenerateTarget, setBatchGenerateTarget] = useState<{ ids: string[]; clear: () => void } | null>(null)
  const [batchGenerateRoleNames, setBatchGenerateRoleNames] = useState<string[]>([])
  const [provisionProgress, setProvisionProgress] = useState<AccountProvisionProgress | null>(null)
  const [batchDeleteTargets, setBatchDeleteTargets] = useState<{ ids: string[]; clear: () => void } | null>(null)
  // P2-L1：批量开通指定角色——候选角色列表懒加载，首次打开任一开通弹窗时才拉取一次
  // （与 employees/client.tsx 的 enterprises/departments/positions 同款 useLazyRef 用法）。
  const { items: roles, ensure: ensureRoles } = useLazyRef<AccountRoleDto>('account-users/assignable-roles')
  // 开通要跨多片 await。离开页面后组件已卸掉，再 setState 没有可提交的树；
  // 测试环境拆除后 react-dom 还会去读已经不存在的 window。
  const mountedRef = useRef(true)
  useEffect(() => {
    mountedRef.current = true
    return () => { mountedRef.current = false }
  }, [])

  function openBulkDialog() {
    void ensureRoles()
    setBulkRoleNames([])
    setBulkOpen(true)
  }

  function openBatchGenerateDialog(ids: string[], clear: () => void) {
    if (ids.length === 0) return
    void ensureRoles()
    setBatchGenerateRoleNames([])
    setBatchGenerateTarget({ ids, clear })
  }

  async function listPendingEmployeeIds() {
    const ids: string[] = []
    let skip = 0
    let total = Number.POSITIVE_INFINITY
    while (skip < total) {
      const qs = new URLSearchParams({
        status: '0',
        hasAccount: 'false',
        maxResultCount: String(EMPLOYEE_LIST_MAX_PAGE_SIZE),
        skipCount: String(skip),
      })
      const res = await fetch(`/api/proxy/employees?${qs}`)
      if (!res.ok) throw new Error(await res.text())
      const data = await res.json() as { items?: { id: string }[]; totalCount?: number }
      const items = data.items ?? []
      ids.push(...items.map(item => item.id))
      total = data.totalCount ?? ids.length
      if (items.length === 0) break
      skip += items.length
    }
    return ids
  }

  async function provisionAccountIds(ids: string[], roleNames: string[]) {
    const tallies = { created: 0, alreadyBound: 0, skipped: 0 }
    const parts = await submitInChunks(
      ids,
      async (chunk) => {
        const res = await fetch('/api/proxy/employees/batch-generate-accounts', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ids: chunk, roleNames }),
        })
        if (!res.ok) throw new Error(await res.text())
        const part = await res.json() as AccountProvisionPart
        tallies.created += part.created ?? 0
        tallies.alreadyBound += part.alreadyBound ?? 0
        tallies.skipped += part.skipped ?? 0
        return part
      },
      {
        chunkSize: ACCOUNT_PROVISION_CHUNK_SIZE,
        onProgress: (progress: BatchChunkProgress) => {
          if (!mountedRef.current) return
          setProvisionProgress({
            phase: 'provisioning',
            done: progress.done,
            total: progress.total,
            ...tallies,
          })
        },
      },
    )
    return {
      created: parts.reduce((sum, part) => sum + (part.created ?? 0), 0),
      alreadyBound: parts.reduce((sum, part) => sum + (part.alreadyBound ?? 0), 0),
      skipped: parts.reduce((sum, part) => sum + (part.skipped ?? 0), 0),
      requested: ids.length,
      messages: parts.flatMap(part => part.messages ?? []),
    }
  }

  // 先分页拉「在职且未开通」，再走与勾选批量同一套小分片，弹窗才能看到进度。
  // roleNames：弹窗内指定的追加角色（P2-L1），追加于默认角色之上；不选则空数组，后端按现行为仅授默认角色。
  async function bulkGenerateAll() {
    setBatchBusy(true)
    setProvisionProgress({ phase: 'listing', done: 0, total: 0, created: 0, alreadyBound: 0, skipped: 0 })
    try {
      const ids = await listPendingEmployeeIds()
      const data = await provisionAccountIds(ids, bulkRoleNames)
      void reload()
      toast({
        variant: 'success',
        title: t('employee.bulkGenerateAllDoneTitle'),
        description: t('employee.bulkGenerateAllDoneDesc', { created: data.created, alreadyBound: data.alreadyBound, skipped: data.skipped, requested: data.requested }),
      })
      if (mountedRef.current) {
        setBulkOpen(false)
        setProvisionProgress(null)
      }
    } catch (error) {
      toast({ variant: 'destructive', title: t('employee.bulkGenerateAllFailedTitle'), description: getApiErrorMessage(error, t('common.batchActionFailedDesc')) })
    } finally { if (mountedRef.current) setBatchBusy(false) }
  }

  // 勾选行「批量生成账号」：原直接触发改为先弹窗选角色再确认（P2-L1 需要角色复选清单，
  // ConfirmDialog 的 AlertDialogDescription 底层是 <p> 塞不下，故本操作也升级为 Dialog）。
  // 失败时对话框保留（与 confirmBatchDelete 一致，便于查看错误后重试）；成功才清空 target 关闭对话框。
  async function confirmBatchGenerate() {
    if (!batchGenerateTarget) return
    setBatchBusy(true)
    setProvisionProgress({
      phase: 'provisioning',
      done: 0,
      total: batchGenerateTarget.ids.length,
      created: 0,
      alreadyBound: 0,
      skipped: 0,
    })
    try {
      const data = await provisionAccountIds(batchGenerateTarget.ids, batchGenerateRoleNames)
      batchGenerateTarget.clear()
      if (mountedRef.current) {
        setBatchGenerateTarget(null)
        setProvisionProgress(null)
      }
      void reload()
      toast({
        variant: 'success',
        title: t('employee.batchGenerateDoneTitle'),
        description: (
          <div className="space-y-1">
            <p>{t('employee.batchGenerateDoneDesc', { created: data.created, alreadyBound: data.alreadyBound, skipped: data.skipped })}</p>
            {data.messages.length > 0 && (
              <ul className="list-disc space-y-0.5 pl-4 text-xs">
                {data.messages.slice(0, BATCH_MESSAGE_PREVIEW).map((m, i) => <li key={i}>{m}</li>)}
                {data.messages.length > BATCH_MESSAGE_PREVIEW && (
                  <li>{t('employee.batchGenerateMoreMessages', { count: data.messages.length - BATCH_MESSAGE_PREVIEW })}</li>
                )}
              </ul>
            )}
          </div>
        ),
      })
    } catch (error) {
      toast({ variant: 'destructive', title: t('employee.batchGenerateFailedTitle'), description: getApiErrorMessage(error, t('common.batchActionFailedDesc')) })
    } finally { if (mountedRef.current) setBatchBusy(false) }
  }

  // status: 0=在职 1=停职 2=离职（对应后端 EmployeeStatus）
  async function batchSetStatus(ids: string[], statusValue: number, label: string, clear: () => void) {
    if (ids.length === 0) return
    setBatchBusy(true)
    try {
      // 分片提交：本页是跨页勾选（selectedIds 累积全部选中），而后端批量契约上限 2000
      // ——把每页调到 5000 再全选就能一次超限，见 lib/crud/batch-chunk。
      const result = await submitBatchIds('employees/batch-set-status', ids, {
        extraBody: { status: statusValue },
      })
      clear(); void reload()
      toast({
        variant: 'success',
        title: t('common.batchActionDoneTitle', { action: label }),
        description: t('employee.succeededCountPeople', { count: result.succeeded }) + (result.skipped ? t('employee.skippedCountPeopleSuffix', { count: result.skipped }) : ''),
      })
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('common.batchActionFailedTitle', { action: label }), description: t('common.batchActionFailedDesc') })
    } finally { if (mountedRef.current) setBatchBusy(false) }
  }

  async function confirmBatchDelete() {
    if (!batchDeleteTargets) return
    setBatchBusy(true)
    try {
      // 分片提交（同上：跨页勾选可超后端 2000 上限）
      const result = await submitBatchIds('employees/batch-delete', batchDeleteTargets.ids)
      batchDeleteTargets.clear()
      if (mountedRef.current) setBatchDeleteTargets(null)
      void reload()
      toast({
        variant: 'success',
        title: t('common.batchDeleteDoneTitle'),
        description: t('employee.batchDeleteSucceededDesc', { count: result.succeeded }) + (result.skipped ? t('employee.skippedCountNameSuffix', { count: result.skipped }) : ''),
      })
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('common.batchDeleteFailedTitle'), description: t('common.batchActionFailedDesc') })
    } finally { if (mountedRef.current) setBatchBusy(false) }
  }

  return {
    batchBusy, bulkOpen, setBulkOpen, openBulkDialog, bulkRoleNames, setBulkRoleNames,
    batchGenerateTarget, setBatchGenerateTarget, openBatchGenerateDialog, batchGenerateRoleNames, setBatchGenerateRoleNames,
    provisionProgress, setProvisionProgress,
    batchDeleteTargets, setBatchDeleteTargets,
    roles,
    bulkGenerateAll, confirmBatchGenerate, batchSetStatus, confirmBatchDelete,
    t, tc,
  }
}

export type EmployeeBulk = ReturnType<typeof useEmployeeBulkActions>

/**
 * 批量开通弹窗内的角色复选清单（P2-L1）：候选角色量级几十~一二百（见 account-roles 蓝图注记），
 * 懒加载后本地过滤即可，无需 RemoteCombobox 那套远程搜索。仅本文件内用，不导出——
 * `RoleMultiSelect`（account-management/users/role-controls.tsx）语义等价但受 eslint
 * swapmid/no-cross-module-import 硬约束不可跨域引用，故按仓内「roles 页 AssignAccountsDialog」
 * 同款「Checkbox + 搜索框 + 滚动清单」范式在本域内自建一份，样式与交互对齐、无需共享组件。
 */
function EmployeeRoleChecklist({
  roles, selected, onChange, disabled, t,
}: {
  roles: AccountRoleDto[]
  selected: string[]
  onChange: (next: string[]) => void
  disabled?: boolean
  t: ReturnType<typeof useTranslations>
}) {
  const [search, setSearch] = useState('')
  const q = search.trim().toLowerCase()
  // 系统内置角色(isStatic,如 admin)后端已硬拒(批量开通不可授予),前端直接不出现在清单里(体验层过滤)。
  const assignable = roles.filter(r => !r.isStatic)
  const filtered = q ? assignable.filter(r => r.name.toLowerCase().includes(q)) : assignable

  function toggle(name: string) {
    onChange(selected.includes(name) ? selected.filter(n => n !== name) : [...selected, name])
  }

  return (
    <div className="space-y-2">
      <div className="space-y-1">
        <Label>{t('employee.assignRolesLabel')}</Label>
        <p className="text-xs text-muted-foreground">{t('employee.assignRolesHint')}</p>
      </div>
      <div className="relative">
        <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder={t('employee.searchRolePlaceholder')}
          disabled={disabled}
          className="h-8 pl-8 text-sm"
        />
      </div>
      <div className="max-h-48 divide-y overflow-y-auto rounded-md border">
        {filtered.map(role => (
          <label key={role.id} className="hover:bg-muted/40 flex cursor-pointer items-center gap-2.5 px-3 py-2">
            <Checkbox checked={selected.includes(role.name)} onCheckedChange={() => toggle(role.name)} disabled={disabled} />
            <span className="text-sm">{role.name}</span>
          </label>
        ))}
        {filtered.length === 0 && (
          <p className="text-muted-foreground py-6 text-center text-sm">{t('employee.noAssignableRoles')}</p>
        )}
      </div>
      {selected.length > 0 && (
        <p className="text-muted-foreground text-xs">{t('employee.selectedRolesCount', { count: selected.length })}</p>
      )}
    </div>
  )
}

function AccountProvisionProgressView({
  progress,
  t,
}: {
  progress: AccountProvisionProgress | null
  t: ReturnType<typeof useTranslations>
}) {
  if (!progress) return null
  if (progress.phase === 'listing') {
    return <p className="text-muted-foreground text-sm" role="status" aria-live="polite">{t('employee.provisionLoadingIds')}</p>
  }

  const percent = progress.total === 0 ? 0 : Math.round((progress.done / progress.total) * 100)
  return (
    <div className="space-y-2" role="status" aria-live="polite">
      <div className="text-muted-foreground flex items-center justify-between text-xs">
        <span>{t('employee.provisionProgressLabel', { done: progress.done, total: progress.total })}</span>
        <span>{percent}%</span>
      </div>
      <div className="bg-muted h-2 overflow-hidden rounded-full">
        <div className="bg-primary h-full transition-[width]" style={{ width: `${percent}%` }} />
      </div>
      <p className="text-muted-foreground text-xs">
        {t('employee.provisionProgressTallies', {
          created: progress.created,
          alreadyBound: progress.alreadyBound,
          skipped: progress.skipped,
        })}
      </p>
    </div>
  )
}

/** DataTable 选中行浮层里的批量按钮组（作为 bulkActions render prop 的内容）。`ids` = 全部跨页选中 id。 */
export function EmployeeBulkBar({
  ids, clear, bulk, canUpdate, canDelete,
}: {
  ids: string[]
  clear: () => void
  bulk: EmployeeBulk
  canUpdate: boolean
  canDelete: boolean
}) {
  const { t } = bulk
  return (
    <>
      {canUpdate && (
        <Button size="sm" variant="outline" className="h-7" disabled={bulk.batchBusy} onClick={() => bulk.openBatchGenerateDialog(ids, clear)}>
          <KeyRound className="mr-1 size-3.5" />{t('employee.batchGenerateAccountsButton')}
        </Button>
      )}
      {canUpdate && (
        <DropdownButton
          className="h-7"
          disabled={bulk.batchBusy}
          icon={<Ban className="size-3.5" />}
          label={t('employee.batchStatusMenuLabel')}
          items={<>
            <DropdownMenuItem onClick={() => void bulk.batchSetStatus(ids, 0, t('employee.restoreActiveMenuItem'), clear)}>{t('employee.restoreActiveMenuItem')}</DropdownMenuItem>
            <DropdownMenuItem onClick={() => void bulk.batchSetStatus(ids, 1, t('employee.statusSuspended'), clear)}>{t('employee.statusSuspended')}</DropdownMenuItem>
            <DropdownMenuItem onClick={() => void bulk.batchSetStatus(ids, 2, t('employee.statusResigned'), clear)}>{t('employee.statusResigned')}</DropdownMenuItem>
          </>}
        />
      )}
      {canDelete && (
        <Button size="sm" variant="destructive-outline" className="h-7" disabled={bulk.batchBusy} onClick={() => bulk.setBatchDeleteTargets({ ids, clear })}>
          <Trash2 className="mr-1 size-3.5" />{t('common.batchDelete')}
        </Button>
      )}
    </>
  )
}

/**
 * 一键开通全员 + 勾选行批量生成 + 批量删除，三个确认弹窗。
 *
 * 前两个（P2-L1 新增角色复选清单）用普通 `<Dialog>` 手写，而非 `<ConfirmDialog>`
 * （= AlertDialog）——`AlertDialogDescription` 底层渲染 `<p>`，`<div>` 等块级元素不能作其子节点，
 * 塞不下复选清单的滚动容器（同 roles/role-dialogs.tsx `AssignAccountsDialog` 注释指出的同一约束）。
 * busy 时禁用关闭/取消/确认，行为对齐 `<ConfirmDialog>` 的 busy 语义。
 */
export function EmployeeBulkDialogs({ bulk, pendingAccountCount }: { bulk: EmployeeBulk; pendingAccountCount: number | null }) {
  const { t, tc } = bulk
  const batchGenerateCount = bulk.batchGenerateTarget?.ids.length ?? 0
  return (
    <>
      <Dialog open={bulk.bulkOpen} onOpenChange={(o) => { if (!bulk.batchBusy) { bulk.setBulkOpen(o); if (!o) bulk.setProvisionProgress(null) } }}>
        <DialogContent className="sm:max-w-[480px]" aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle>{t('employee.confirmBulkTitle')}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <p className="text-sm text-muted-foreground">
              {t.rich('employee.confirmBulkDesc', {
                b: (chunks) => <span className="text-foreground font-medium">{chunks}</span>,
                count: pendingAccountCount ?? '…',
              })}
            </p>
            <EmployeeRoleChecklist roles={bulk.roles} selected={bulk.bulkRoleNames} onChange={bulk.setBulkRoleNames} disabled={bulk.batchBusy} t={t} />
            <AccountProvisionProgressView progress={bulk.provisionProgress} t={t} />
          </div>
          <DialogFooter>
            <Button variant="outline" disabled={bulk.batchBusy} onClick={() => { bulk.setBulkOpen(false); bulk.setProvisionProgress(null) }}>{tc('cancel')}</Button>
            <Button disabled={bulk.batchBusy} onClick={() => void bulk.bulkGenerateAll()}>
              {bulk.batchBusy ? t('employee.provisioningBusyText') : t('employee.confirmProvisionText')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!bulk.batchGenerateTarget} onOpenChange={(o) => { if (!o && !bulk.batchBusy) { bulk.setBatchGenerateTarget(null); bulk.setProvisionProgress(null) } }}>
        <DialogContent className="sm:max-w-[480px]" aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle>{t('employee.confirmBatchGenerateTitle', { count: batchGenerateCount })}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <p className="text-sm text-muted-foreground">{t('employee.confirmBatchGenerateDesc')}</p>
            <EmployeeRoleChecklist roles={bulk.roles} selected={bulk.batchGenerateRoleNames} onChange={bulk.setBatchGenerateRoleNames} disabled={bulk.batchBusy} t={t} />
            <AccountProvisionProgressView progress={bulk.provisionProgress} t={t} />
          </div>
          <DialogFooter>
            <Button variant="outline" disabled={bulk.batchBusy} onClick={() => { bulk.setBatchGenerateTarget(null); bulk.setProvisionProgress(null) }}>{tc('cancel')}</Button>
            <Button disabled={bulk.batchBusy} onClick={() => void bulk.confirmBatchGenerate()}>
              {bulk.batchBusy ? t('employee.provisioningBusyText') : t('employee.batchGenerateAccountsButton')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={!!bulk.batchDeleteTargets}
        onOpenChange={(o) => !o && bulk.setBatchDeleteTargets(null)}
        title={t('common.confirmBatchDeleteTitle', { entityName: t('common.employeeEntity') })}
        description={t('employee.confirmBatchDeleteDesc', { count: bulk.batchDeleteTargets?.ids.length ?? 0 })}
        confirmText={tc('delete')}
        busyText={tc('deleting')}
        destructive
        busy={bulk.batchBusy}
        onConfirm={bulk.confirmBatchDelete}
      />
    </>
  )
}
