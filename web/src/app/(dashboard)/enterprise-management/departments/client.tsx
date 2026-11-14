'use client'

import { useCallback, useMemo, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { type ColumnDef, type RowSelectionState } from '@tanstack/react-table'
import { Ban, Power, Trash2 } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, booleanBadgeColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Combobox } from '@/components/ui/combobox'
import { EmployeeCombobox } from '@/components/common/employee-combobox'
import { toast } from '@/components/ui/use-toast'
import { useHasPermission } from '@/components/providers/permissions-provider'
import { Permissions } from '@/lib/permissions'
import { type PagedResult } from '@/lib/api-client'
import type { CompanyDepartmentDto, EnterpriseDto } from '@/lib/api/types'
import { useLazyRef } from '@/lib/use-lazy-ref'
import { submitBatchIds } from '@/lib/crud/batch-chunk'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useMutateAndRefresh } from '@/lib/crud/use-mutate-and-refresh'
import { missingLabels } from '@/lib/crud/missing-fields'

// 类型来自 OpenAPI 生成产物（pnpm gen:api）的别名桶，与后端 DTO 永久同步。
export type CompanyDepartment = CompanyDepartmentDto
export type EnterpriseOption = EnterpriseDto

interface FormState {
  enterpriseId: string; parentId: string; code: string; name: string
  order: string; isEnabled: boolean
  leaderEmployeeId: string; leaderEmployeeName: string
}
const defaultForm: FormState = {
  enterpriseId: '', parentId: '', code: '', name: '',
  order: '0', isEnabled: true,
  leaderEmployeeId: '', leaderEmployeeName: '',
}

export function DepartmentsClient({ data }: { data: PagedResult<CompanyDepartment> }) {
  const router = useRouter()
  const t = useTranslations('Enterprise')
  const tc = useTranslations('Common')
  const entityName = t('common.departmentEntity')
  const canCreate = useHasPermission(Permissions.EnterpriseManagement.DepartmentsCreate)
  const canUpdate = useHasPermission(Permissions.EnterpriseManagement.DepartmentsUpdate)
  const canDelete = useHasPermission(Permissions.EnterpriseManagement.DepartmentsDelete)
  const canMutate = canUpdate || canDelete
  const { items: enterprises, ensure: ensureEnterprises } = useLazyRef<EnterpriseOption>('enterprises?maxResultCount=1000')

  const crud = useCrudForm<FormState, CompanyDepartment>({
    entityName,
    endpoint: 'departments',
    defaultForm,
    toForm: (d) => ({
      enterpriseId: d.enterpriseId, parentId: d.parentId ?? '',
      code: d.code, name: d.name, order: String(d.order), isEnabled: d.isEnabled,
      leaderEmployeeId: d.leaderEmployeeId ?? '', leaderEmployeeName: d.leaderEmployeeName ?? '',
    }),
    toBody: (form) => {
      const enterprise = enterprises.find(e => e.id === form.enterpriseId)
      const parent = data.items.find(d => d.id === form.parentId)
      return {
        enterpriseId: form.enterpriseId, enterpriseCode: enterprise?.code ?? null,
        parentId: form.parentId || null, parentCode: parent?.code ?? null,
        code: form.code, name: form.name,
        order: Number(form.order) || 0, isEnabled: form.isEnabled,
        leaderEmployeeId: form.leaderEmployeeId || null,
      }
    },
    // 打开新建时懒加载企业列表并回填默认主体企业
    beforeCreate: async () => {
      const ents = await ensureEnterprises()
      return { enterpriseId: ents.find(e => e.isMain)?.id ?? '' }
    },
    beforeEdit: () => { void ensureEnterprises() },
  })

  const mutate = useMutateAndRefresh()
  const [statusBusyId, setStatusBusyId] = useState<string | null>(null)
  const [selectedRowIds, setSelectedRowIds] = useState<RowSelectionState>({})
  const [batchDeleteTargets, setBatchDeleteTargets] = useState<{ rows: CompanyDepartment[]; clear: () => void } | null>(null)
  const [batchBusy, setBatchBusy] = useState(false)

  const toggleEnabled = useCallback(async (row: CompanyDepartment, next: boolean) => {
    setStatusBusyId(row.id)
    try {
      await mutate('/api/proxy/departments/batch-set-enabled', {
        method: 'POST',
        body: { ids: [row.id], isEnabled: next },
        okTitle: next ? tc('enabledDone') : tc('disabledDone'),
      })
    } finally {
      setStatusBusyId(null)
    }
  }, [mutate, tc])

  async function batchSetEnabled(rows: CompanyDepartment[], isEnabled: boolean, clear: () => void) {
    if (rows.length === 0) return
    const label = isEnabled ? t('common.enableLabel') : t('common.disableLabel')
    setBatchBusy(true)
    try {
      const res = await fetch('/api/proxy/departments/batch-set-enabled', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids: rows.map(r => r.id), isEnabled }),
      })
      if (!res.ok) throw new Error(await res.text())
      const result = await res.json()
      clear(); router.refresh()
      toast({
        variant: 'success',
        title: t('common.batchActionDoneTitle', { action: label }),
        description: t('common.succeededCount', { count: result.succeeded }) + (result.skipped ? t('common.skippedCountSuffix', { count: result.skipped }) : ''),
      })
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('common.batchActionFailedTitle', { action: label }), description: t('common.batchActionFailedDesc') })
    } finally { setBatchBusy(false) }
  }

  async function confirmBatchDelete() {
    if (!batchDeleteTargets) return
    setBatchBusy(true)
    try {
      // 分片提交：后端批量契约上限 2000，而跨页勾选 + 大页档位可选出更多（见 lib/crud/batch-chunk）
      const result = await submitBatchIds('departments/batch-delete', batchDeleteTargets.rows.map(r => r.id))
      batchDeleteTargets.clear(); setBatchDeleteTargets(null); router.refresh()
      toast({
        variant: 'success',
        title: t('common.batchDeleteDoneTitle'),
        description: t('common.batchDeleteSucceededDesc', { count: result.succeeded, entityName }) + (result.skipped ? t('common.skippedCountSuffix', { count: result.skipped }) : ''),
      })
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('common.batchDeleteFailedTitle'), description: t('common.batchActionFailedDesc') })
    } finally { setBatchBusy(false) }
  }

  // Departments of selected enterprise for parent select（排除自身，避免选自己当父）
  const siblingDepts = data.items.filter(d => d.enterpriseId === crud.form.enterpriseId && d.id !== crud.target?.id)

  const columns: ColumnDef<CompanyDepartment, unknown>[] = useMemo(() => [
    primaryColumn<CompanyDepartment>('code', t('department.colCode'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 120,
      className: 'font-mono text-sm',
    }),
    // 编码与名称都是同一行身份，打开同一个编辑框。
    primaryColumn<CompanyDepartment>('name', t('department.colName'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 240,
      format: (d) => (
        <span className="flex min-w-0 flex-col">
          <span className="truncate">{d.name}</span>
          <span className="truncate text-xs font-normal text-muted-foreground">
            {d.fullPath ?? d.parentName ?? t('department.topLevel')}
          </span>
        </span>
      ),
    }),
    {
      accessorKey: 'level',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('department.colLevel')} />,
      size: 80,
      cell: ({ row }) => <Badge variant="secondary">L{row.original.level || 1}</Badge>,
    },
    {
      accessorKey: 'enterpriseName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.belongEnterpriseColumnHeader')} />,
      size: 160,
      cell: ({ row }) => <Badge variant="outline">{row.original.enterpriseName ?? '—'}</Badge>,
    },
    {
      accessorKey: 'parentName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('department.colParent')} />,
      size: 140,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.parentName ?? t('department.topLevel')}</span>,
    },
    {
      accessorKey: 'leaderEmployeeName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('department.colLeader')} />,
      size: 140,
      cell: ({ row }) => (
        <span className="text-sm">
          {row.original.leaderEmployeeName
            ? row.original.leaderEmployeeName
            : <span className="text-muted-foreground">—</span>}
        </span>
      ),
    },
    {
      accessorKey: 'order',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.sortColumnHeader')} />,
      size: 80,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.order}</span>,
    },
    booleanBadgeColumn<CompanyDepartment>('isEnabled', t('common.statusColumnHeader'), {
      trueLabel: t('common.enableLabel'),
      falseLabel: t('common.disableLabel'),
      onToggle: canUpdate ? toggleEnabled : undefined,
      isToggleDisabled: () => !!statusBusyId,
    }),
    ...((canUpdate || canDelete)
      ? [actionsColumn<CompanyDepartment>({
          onEdit: canUpdate ? crud.openEdit : undefined,
          onDelete: canDelete ? crud.askDelete : undefined,
        })]
      : []),
  ], [t, crud.openEdit, crud.askDelete, toggleEnabled, statusBusyId, canUpdate, canDelete])

  // 必填项一处声明、`canSubmit` 与缺失项提示两处消费
  const missingFields = missingLabels({
    [t('common.belongEnterpriseLabel')]: !!crud.form.enterpriseId,
    [t('department.codeLabel')]: !!crud.form.code,
    [t('department.nameLabel')]: !!crud.form.name,
  })

  return (
    <>
      <PageHeader
        title={t('department.pageTitle')}
        description={t('department.countDescription', { count: data.totalCount })}
        action={canCreate
          ? <CreateButton label={t('common.createEntityTitle', { entityName })} onClick={crud.openCreate} />
          : undefined}
      />
      <DataTable
        columns={columns}
        data={data.items}
        searchPlaceholder={t('department.searchPlaceholder')}
        tableKey="departments"
        enableRowSelection={canMutate}
        selectedRowIds={selectedRowIds}
        onSelectedRowIdsChange={setSelectedRowIds}
        getRowId={(row) => row.id}
        bulkActions={canMutate
          ? (rows, clearSelection) => (
            <>
              {canUpdate && (
                <Button size="sm" variant="outline" className="h-7" disabled={batchBusy} onClick={() => void batchSetEnabled(rows, true, clearSelection)}>
                  <Power className="mr-1 size-3.5" />{t('common.batchEnable')}
                </Button>
              )}
              {canUpdate && (
                <Button size="sm" variant="outline" className="h-7" disabled={batchBusy} onClick={() => void batchSetEnabled(rows, false, clearSelection)}>
                  <Ban className="mr-1 size-3.5" />{t('common.batchDisable')}
                </Button>
              )}
              {canDelete && (
                <Button size="sm" variant="destructive-outline" className="h-7" disabled={batchBusy} onClick={() => setBatchDeleteTargets({ rows, clear: clearSelection })}>
                  <Trash2 className="mr-1 size-3.5" />{t('common.batchDelete')}
                </Button>
              )}
            </>
          )
          : undefined}
      />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('common.createEntityTitle', { entityName }), edit: t('common.editEntityTitle', { entityName }) }}
        contentClassName="sm:max-w-[520px]"
        canSubmit={missingFields.length === 0}
        missingFields={missingFields}
      >
        <div className="space-y-1.5">
          <Label>{t('common.belongEnterpriseLabel')}</Label>
          <Combobox
            options={enterprises.map(e => ({ value: e.id, label: e.name }))}
            value={crud.form.enterpriseId}
            onChange={v => crud.set({ enterpriseId: v, parentId: '' })}
            placeholder={t('common.selectEnterprise')}
            searchPlaceholder={t('common.searchEnterprise')}
            emptyText={t('common.enterpriseNotFound')}
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('department.colParent')}</Label>
          <Combobox
            options={[{ value: '', label: t('department.topLevelOption') }, ...siblingDepts.map(d => ({ value: d.id, label: d.fullPath ?? d.name, description: `L${d.level || 1} · ${d.code}` }))]}
            value={crud.form.parentId}
            onChange={v => crud.set({ parentId: v })}
            placeholder={t('department.topLevelOption')}
            searchPlaceholder={t('department.searchParentDept')}
            emptyText={t('common.selectEnterpriseFirst')}
            disabled={!crud.form.enterpriseId}
          />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('department.codeLabel')}</Label>
            <Input value={crud.form.code} onChange={e => crud.set({ code: e.target.value })} placeholder={t('department.codePlaceholder')} className="font-mono" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('department.nameLabel')}</Label>
            <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('department.namePlaceholder')} />
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>{t('department.leaderLabel')}</Label>
          <EmployeeCombobox
            value={crud.form.leaderEmployeeId}
            selectedName={crud.form.leaderEmployeeName}
            onChange={(id, name) => crud.set({ leaderEmployeeId: id, leaderEmployeeName: name })}
            placeholder={t('department.leaderPlaceholder')}
          />
          <p className="text-xs text-muted-foreground">{t('department.leaderHint')}</p>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('common.sortColumnHeader')}</Label>
            <Input type="number" value={crud.form.order} onChange={e => crud.set({ order: e.target.value })} min={0} />
          </div>
          <div className="flex items-center gap-2 pt-6">
            <Switch id="dept-isEnabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
            <Label htmlFor="dept-isEnabled">{t('department.enableSwitchLabel')}</Label>
          </div>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={entityName}
        description={(target) => <>{t('common.deleteEntityDesc', { entityName, name: target.name })}</>}
      />

      <ConfirmDialog
        open={!!batchDeleteTargets}
        onOpenChange={(o) => !o && setBatchDeleteTargets(null)}
        title={t('common.confirmBatchDeleteTitle', { entityName })}
        description={t('common.confirmBatchDeleteDesc', { count: batchDeleteTargets?.rows.length ?? 0, entityName })}
        confirmText={tc('delete')}
        busyText={tc('deleting')}
        destructive
        busy={batchBusy}
        onConfirm={confirmBatchDelete}
      />
    </>
  )
}
