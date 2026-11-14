'use client'

import { useCallback, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { DataTable } from '@/components/data-table/data-table'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import type { PagedResult } from '@/lib/api-client'
import type { AccountRoleDto } from '@/lib/api/types'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { getRolesColumns } from './columns'
import { AssignAccountsDialog, CopyRoleDialog } from './role-dialogs'
import { RoleDetailSheet } from './role-detail-sheet'

interface FormState {
  name: string
  isDefault: boolean
  isPublic: boolean
  description: string
}

const defaultForm: FormState = { name: '', isDefault: false, isPublic: true, description: '' }
const rolesListUrl = '/api/proxy/account-roles?maxResultCount=1000&skipCount=0'

export function RolesClient({ data }: { data: PagedResult<AccountRoleDto> }) {
  const t = useTranslations('Account')
  const tc = useTranslations('Common')
  // 列表数据来自服务端首屏。只靠 router.refresh() 时，当前页签经常仍显示保存前的行，
  // 必须整页刷新才看得到。写成功后先走客户端重取；紧接着的一次服务端 props 若仍是旧快照，先挡住。
  const [list, setList] = useState(data)
  const [serverList, setServerList] = useState(data)
  const [holdClientList, setHoldClientList] = useState(false)
  if (data !== serverList) {
    setServerList(data)
    if (holdClientList) setHoldClientList(false)
    else setList(data)
  }
  const reload = useCallback(async () => {
    setHoldClientList(true)
    try {
      const res = await fetch(rolesListUrl, { cache: 'no-store' })
      if (!res.ok) {
        setHoldClientList(false)
        return
      }
      setList(await res.json() as PagedResult<AccountRoleDto>)
    } catch {
      setHoldClientList(false)
    }
  }, [])
  // 分配账号 / 复制 / 详情：各自独立的行级目标 state（非空即打开对应对话框/抽屉），
  // 分配账号与复制实现收在 role-dialogs.tsx（自包含状态机），详情抽屉见 role-detail-sheet.tsx（P1-1）。
  const [assignTarget, setAssignTarget] = useState<AccountRoleDto | null>(null)
  const [copyTarget, setCopyTarget] = useState<AccountRoleDto | null>(null)
  const [detailTarget, setDetailTarget] = useState<AccountRoleDto | null>(null)
  const detail = detailTarget
    ? list.items.find(item => item.id === detailTarget.id) ?? detailTarget
    : null

  // P0-10：角色 CRUD 从 ABP 原生路由（api/identity/roles）切到平台服务 account-roles
  // （AccountRoleAppService，支持 filter/分页 + Description/MemberCount/PermissionCount 扩展字段）。
  // PUT 的 concurrencyStamp：AccountRoleDto 已把它收作一等字段（不再需要从行上 as unknown 硬取），
  // 直接 row.concurrencyStamp 透传进 CreateUpdateDto 同名字段即可。
  const crud = useCrudForm<FormState, AccountRoleDto>({
    entityName: t('common.roleEntity'),
    endpoint: 'account-roles',
    defaultForm,
    toForm: (r) => ({ name: r.name, isDefault: r.isDefault, isPublic: r.isPublic, description: r.description ?? '' }),
    toBody: (form, _mode, row) => ({
      name: form.name,
      isDefault: form.isDefault,
      isPublic: form.isPublic,
      description: form.description.trim() || null,
      ...(row ? { concurrencyStamp: row.concurrencyStamp } : {}),
    }),
    onSuccess: () => { void reload() },
  })

  const columns = getRolesColumns({
    onViewDetail: setDetailTarget,
    onAssign: setAssignTarget,
    onCopy: setCopyTarget,
    onEdit: crud.openEdit,
    onDelete: crud.askDelete,
    t,
    tc,
  })

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <PageHeader
        title={t('role.pageTitle')}
        description={t('role.totalCountLabel', { count: list.totalCount })}
        action={<CreateButton label={t('role.createTitle')} onClick={crud.openCreate} />}
      />
      <DataTable columns={columns} data={list.items} searchPlaceholder={t('role.searchPlaceholder')} tableKey="roles" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('role.createTitle'), edit: t('role.editTitle') }}
        contentClassName="sm:max-w-[440px]"
        canSubmit={!!crud.form.name}
      >
        <div className="space-y-1.5">
          <Label>{t('role.formNameLabel')}</Label>
          <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('role.formNamePlaceholder')} />
        </div>
        <div className="space-y-1.5">
          <Label>{t('role.formDescriptionLabel')}</Label>
          <Textarea
            value={crud.form.description}
            onChange={e => crud.set({ description: e.target.value })}
            placeholder={t('role.formDescriptionPlaceholder')}
            rows={2}
          />
        </div>
        <div className="flex items-center gap-2">
          <Switch id="isDefault" checked={crud.form.isDefault} onCheckedChange={v => crud.set({ isDefault: v })} />
          <Label htmlFor="isDefault">{t('role.formIsDefaultLabel')}</Label>
        </div>
        <div className="flex items-center gap-2">
          <Switch id="isPublic" checked={crud.form.isPublic} onCheckedChange={v => crud.set({ isPublic: v })} />
          <Label htmlFor="isPublic">{t('role.isPublicColumnHeader')}</Label>
        </div>
      </CrudFormDialog>

      {/* P0-12：删除确认带成员数警示——N=0 用常规文案（不吓唬用户），N>0 明确告知这些账号将失去该角色全部权限。 */}
      <DeleteConfirm
        crud={crud}
        entityName={t('common.roleEntity')}
        description={(target) => target.memberCount > 0
          ? t.rich('role.deleteDescWithMembers', {
              count: target.memberCount,
              name: target.name,
              b: (chunks) => <strong>{chunks}</strong>,
            })
          : t.rich('role.deleteDesc', { name: target.name, b: (chunks) => <strong>{chunks}</strong> })}
      />

      <AssignAccountsDialog role={assignTarget} onClose={() => setAssignTarget(null)} onSaved={() => { void reload() }} />
      <CopyRoleDialog source={copyTarget} onClose={() => setCopyTarget(null)} onSaved={() => { void reload() }} />
      <RoleDetailSheet role={detail} onClose={() => setDetailTarget(null)} />
    </div>
  )
}
