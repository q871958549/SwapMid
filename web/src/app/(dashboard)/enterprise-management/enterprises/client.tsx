'use client'

import { useTranslations } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { Crown } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { type PagedResult } from '@/lib/api-client'
import type { EnterpriseDto } from '@/lib/api/types'
import { useHasPermission } from '@/components/providers/permissions-provider'
import { Permissions } from '@/lib/permissions'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useMutateAndRefresh } from '@/lib/crud/use-mutate-and-refresh'

// 类型来自 OpenAPI 生成产物（pnpm gen:api）的别名桶，与后端 DTO 永久同步。
export type Enterprise = EnterpriseDto

interface FormState { code: string; name: string; shortName: string; contactPerson: string; contactPhone: string; email: string; address: string; status: string; isEnabled: boolean }
const defaultForm: FormState = { code: '', name: '', shortName: '', contactPerson: '', contactPhone: '', email: '', address: '', status: '0', isEnabled: true }

export function EnterprisesClient({ data }: { data: PagedResult<Enterprise> }) {
  const t = useTranslations('Enterprise')
  const tc = useTranslations('Common')
  const entityName = t('common.enterpriseEntity')
  // 0=Active 1=Suspended 2=Archived
  const ENT_STATUS = [
    { value: 0, label: t('enterprise.statusActive'), variant: 'success' as const },
    { value: 1, label: t('enterprise.statusSuspended'), variant: 'secondary' as const },
    { value: 2, label: t('enterprise.statusArchived'), variant: 'outline' as const },
  ]
  const canCreate = useHasPermission(Permissions.EnterpriseManagement.EnterprisesCreate)
  const canUpdate = useHasPermission(Permissions.EnterpriseManagement.EnterprisesUpdate)
  const canDelete = useHasPermission(Permissions.EnterpriseManagement.EnterprisesDelete)
  const mutate = useMutateAndRefresh()
  const crud = useCrudForm<FormState, Enterprise>({
    entityName,
    endpoint: 'enterprises',
    defaultForm,
    toForm: (e) => ({ code: e.code, name: e.name, shortName: e.shortName ?? '', contactPerson: e.contactPerson ?? '', contactPhone: e.contactPhone ?? '', email: e.email ?? '', address: e.address ?? '', status: String(e.status), isEnabled: e.isEnabled }),
    toBody: (form) => ({ code: form.code, name: form.name, shortName: form.shortName || null, contactPerson: form.contactPerson || null, contactPhone: form.contactPhone || null, email: form.email || null, address: form.address || null, status: Number(form.status), isEnabled: form.isEnabled }),
  })

  async function handleSetAsMain(id: string) {
    await mutate(`/api/proxy/enterprises/${id}/set-as-main`, {
      method: 'PUT',
      okTitle: t('enterprise.setAsMainOkTitle'),
      okDescription: t('enterprise.setAsMainOkDesc'),
      errorTitle: t('enterprise.setAsMainErrorTitle'),
      errorFallback: t('common.batchActionFailedDesc'),
    })
  }

  const columns: ColumnDef<Enterprise, unknown>[] = [
    primaryColumn<Enterprise>('code', t('enterprise.colCode'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 120,
      className: 'font-mono text-sm',
    }),
    primaryColumn<Enterprise>('name', t('enterprise.colName'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 220,
      format: (e) => (
        <span className="flex min-w-0 items-center gap-2">
          <span className="min-w-0 truncate">{e.name}</span>
          {e.isMain && <Badge variant="default" className="shrink-0 gap-1 text-xs"><Crown className="size-3" />{t('enterprise.mainBadge')}</Badge>}
        </span>
      ),
    }),
    {
      accessorKey: 'contactPerson',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('enterprise.colContactPerson')} />,
      size: 120,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.contactPerson ?? '—'}</span>,
    },
    {
      accessorKey: 'contactPhone',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('enterprise.colContactPhone')} />,
      size: 140,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.contactPhone ?? '—'}</span>,
    },
    {
      accessorKey: 'status',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.statusColumnHeader')} />,
      size: 80,
      meta: { filterVariant: 'select', filterOptions: ENT_STATUS.map(s => ({ value: s.value, label: s.label })) },
      cell: ({ row }) => {
        const s = ENT_STATUS.find(x => x.value === row.original.status)
        return <Badge variant={s?.variant ?? 'secondary'}>{s?.label ?? t('enterprise.statusUnknown')}</Badge>
      },
    },
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('creationTime')} />,
      size: 160,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{new Date(row.original.creationTime).toLocaleDateString('zh-CN')}</span>,
    },
    ...((canUpdate || canDelete)
      ? [actionsColumn<Enterprise>({
          onEdit: canUpdate ? crud.openEdit : undefined,
          onDelete: canDelete ? crud.askDelete : undefined,
          extraItems: canUpdate
            ? (row) => !row.isMain && (
              <DropdownMenuItem onClick={() => handleSetAsMain(row.id)}><Crown className="mr-2 size-3.5" />{t('enterprise.setAsMainMenuItem')}</DropdownMenuItem>
            )
            : undefined,
        })]
      : []),
  ]

  return (
    <>
      <PageHeader
        title={t('enterprise.pageTitle')}
        description={t('enterprise.countDescription', { count: data.totalCount })}
        action={canCreate
          ? <CreateButton label={t('common.createEntityTitle', { entityName })} onClick={crud.openCreate} />
          : undefined}
      />
      <DataTable columns={columns} data={data.items} searchPlaceholder={t('enterprise.searchPlaceholder')} tableKey="enterprises" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('common.createEntityTitle', { entityName }), edit: t('common.editEntityTitle', { entityName }) }}
        contentClassName="sm:max-w-[540px]"
        canSubmit={!!crud.form.code && !!crud.form.name}
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('enterprise.formCodeLabel')}</Label>
            <Input value={crud.form.code} onChange={e => crud.set({ code: e.target.value })} placeholder="ENT001" className="font-mono" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('enterprise.formStatusLabel')}</Label>
            <Select value={crud.form.status} onValueChange={v => crud.set({ status: v })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>{ENT_STATUS.map(s => <SelectItem key={s.value} value={String(s.value)}>{s.label}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('enterprise.formNameLabel')}</Label>
            <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('enterprise.formNamePlaceholder')} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('enterprise.formShortNameLabel')}</Label>
            <Input value={crud.form.shortName} onChange={e => crud.set({ shortName: e.target.value })} placeholder={t('enterprise.formShortNamePlaceholder')} />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('enterprise.formContactPersonLabel')}</Label>
            <Input value={crud.form.contactPerson} onChange={e => crud.set({ contactPerson: e.target.value })} placeholder={t('enterprise.formContactPersonPlaceholder')} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('enterprise.formContactPhoneLabel')}</Label>
            <Input value={crud.form.contactPhone} onChange={e => crud.set({ contactPhone: e.target.value })} placeholder={t('enterprise.formContactPhonePlaceholder')} />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{tc('email')}</Label>
            <Input type="email" value={crud.form.email} onChange={e => crud.set({ email: e.target.value })} placeholder="email@example.com" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('enterprise.formAddressLabel')}</Label>
            <Input value={crud.form.address} onChange={e => crud.set({ address: e.target.value })} placeholder={t('enterprise.formAddressPlaceholder')} />
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Switch id="ent-isEnabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
          <Label htmlFor="ent-isEnabled">{t('enterprise.enableSwitchLabel')}</Label>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={entityName}
        description={(target) => <>{t('common.deleteEntityDesc', { entityName, name: target.name })}</>}
      />
    </>
  )
}
