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
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Combobox } from '@/components/ui/combobox'
import { toast } from '@/components/ui/use-toast'
import { useHasPermission } from '@/components/providers/permissions-provider'
import { Permissions } from '@/lib/permissions'
import { DepartmentCell } from '@/components/common/department-cell'
import { type PagedResult } from '@/lib/api-client'
import type { CompanyDepartmentDto, EnterpriseDto, PositionDto } from '@/lib/api/types'
import { useLazyRef } from '@/lib/use-lazy-ref'
import { submitBatchIds } from '@/lib/crud/batch-chunk'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useMutateAndRefresh } from '@/lib/crud/use-mutate-and-refresh'
import { missingLabels } from '@/lib/crud/missing-fields'

// 类型来自 OpenAPI 生成产物（pnpm gen:api）的别名桶，与后端 DTO 永久同步。
export type Position = PositionDto
export type EnterpriseOption = EnterpriseDto
export type DepartmentOption = CompanyDepartmentDto

interface FormState {
  enterpriseId: string; departmentId: string
  code: string; name: string; level: string
  description: string; order: string; isEnabled: boolean
}

const defaultForm: FormState = {
  enterpriseId: '', departmentId: '',
  code: '', name: '', level: '',
  description: '', order: '0', isEnabled: true,
}

export function PositionsClient({ data }: { data: PagedResult<Position> }) {
  const router = useRouter()
  const t = useTranslations('Enterprise')
  const tc = useTranslations('Common')
  const entityName = t('common.positionEntity')
  const canCreate = useHasPermission(Permissions.EnterpriseManagement.PositionsCreate)
  const canUpdate = useHasPermission(Permissions.EnterpriseManagement.PositionsUpdate)
  const canDelete = useHasPermission(Permissions.EnterpriseManagement.PositionsDelete)
  const canMutate = canUpdate || canDelete
  const { items: enterprises, ensure: ensureEnterprises } = useLazyRef<EnterpriseOption>('enterprises?maxResultCount=1000')
  const { items: departments, ensure: ensureDepartments } = useLazyRef<DepartmentOption>('departments?maxResultCount=2000')
  function ensureRefs() { void ensureDepartments(); return ensureEnterprises() }

  const crud = useCrudForm<FormState, Position>({
    entityName,
    endpoint: 'positions',
    defaultForm,
    toForm: (p) => ({
      enterpriseId: p.enterpriseId, departmentId: p.departmentId,
      code: p.code, name: p.name, level: p.level ?? '',
      description: p.description ?? '', order: String(p.order), isEnabled: p.isEnabled,
    }),
    toBody: (form) => {
      const enterprise = enterprises.find(e => e.id === form.enterpriseId)
      const department = departments.find(d => d.id === form.departmentId)
      return {
        enterpriseId: form.enterpriseId, enterpriseCode: enterprise?.code ?? null,
        departmentId: form.departmentId, departmentCode: department?.code ?? null,
        code: form.code, name: form.name,
        level: form.level || null, description: form.description || null,
        order: Number(form.order) || 0, isEnabled: form.isEnabled,
      }
    },
    // 打开新建时懒加载参照数据并回填默认主体企业
    beforeCreate: async () => {
      const ents = await ensureRefs()
      return { enterpriseId: ents.find(e => e.isMain)?.id ?? '' }
    },
    beforeEdit: () => { void ensureRefs() },
  })

  const mutate = useMutateAndRefresh()
  const [statusBusyId, setStatusBusyId] = useState<string | null>(null)
  const [selectedRowIds, setSelectedRowIds] = useState<RowSelectionState>({})
  const [batchDeleteTargets, setBatchDeleteTargets] = useState<{ rows: Position[]; clear: () => void } | null>(null)
  const [batchBusy, setBatchBusy] = useState(false)

  const toggleEnabled = useCallback(async (row: Position, next: boolean) => {
    setStatusBusyId(row.id)
    try {
      await mutate('/api/proxy/positions/batch-set-enabled', {
        method: 'POST',
        body: { ids: [row.id], isEnabled: next },
        okTitle: next ? tc('enabledDone') : tc('disabledDone'),
      })
    } finally {
      setStatusBusyId(null)
    }
  }, [mutate, tc])

  async function batchSetEnabled(rows: Position[], isEnabled: boolean, clear: () => void) {
    if (rows.length === 0) return
    const label = isEnabled ? t('common.enableLabel') : t('common.disableLabel')
    setBatchBusy(true)
    try {
      const res = await fetch('/api/proxy/positions/batch-set-enabled', {
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
      const result = await submitBatchIds('positions/batch-delete', batchDeleteTargets.rows.map(r => r.id))
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

  const filteredDepts = departments.filter(d => d.enterpriseId === crud.form.enterpriseId)
  const enterpriseOptions = enterprises.map(e => ({ value: e.id, label: e.name }))
  const deptOptions = filteredDepts.map(d => ({ value: d.id, label: d.fullPath ?? d.name, description: d.level ? `L${d.level} · ${d.code}` : d.code }))

  const columns = useMemo<ColumnDef<Position, unknown>[]>(() => [
    primaryColumn<Position>('code', t('position.colCode'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 120,
      className: 'font-mono text-sm',
    }),
    // 编码与名称都是同一行身份，打开同一个编辑框。
    primaryColumn<Position>('name', t('position.colName'), { onOpen: canUpdate ? crud.openEdit : undefined, size: 180 }),
    {
      accessorKey: 'level',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('position.colLevel')} />,
      size: 100,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.level ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'departmentName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('position.colDepartment')} />,
      size: 160,
      cell: ({ row }) => <DepartmentCell departmentId={row.original.departmentId} departmentName={row.original.departmentName} />,
    },
    {
      accessorKey: 'enterpriseName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.belongEnterpriseColumnHeader')} />,
      size: 160,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.enterpriseName ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'order',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.sortColumnHeader')} />,
      size: 80,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.order}</span>,
    },
    booleanBadgeColumn<Position>('isEnabled', t('common.statusColumnHeader'), {
      trueLabel: t('common.enableLabel'),
      falseLabel: t('common.disableLabel'),
      onToggle: canUpdate ? toggleEnabled : undefined,
      isToggleDisabled: () => !!statusBusyId,
    }),
    ...((canUpdate || canDelete)
      ? [actionsColumn<Position>({
          onEdit: canUpdate ? crud.openEdit : undefined,
          onDelete: canDelete ? crud.askDelete : undefined,
        })]
      : []),
  ], [t, crud.openEdit, crud.askDelete, toggleEnabled, statusBusyId, canUpdate, canDelete])

  // 必填项一处声明、`canSubmit` 与缺失项提示两处消费——两份手写清单迟早走散
  const missingFields = missingLabels({
    [t('common.belongEnterpriseLabel')]: !!crud.form.enterpriseId,
    [t('position.belongDepartmentLabel')]: !!crud.form.departmentId,
    [t('position.codeLabel')]: !!crud.form.code,
    [t('position.nameLabel')]: !!crud.form.name,
  })

  return (
    <>
      <PageHeader
        title={t('position.pageTitle')}
        description={t('position.countDescription', { count: data.totalCount })}
        action={canCreate
          ? <CreateButton label={t('common.createEntityTitle', { entityName })} onClick={crud.openCreate} />
          : undefined}
      />
      <DataTable
        columns={columns}
        data={data.items}
        searchPlaceholder={t('position.searchPlaceholder')}
        tableKey="positions"
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
        canSubmit={missingFields.length === 0}
        missingFields={missingFields}
      >
        <div className="space-y-1.5">
          <Label>{t('common.belongEnterpriseLabel')}</Label>
          <Combobox
            options={enterpriseOptions}
            value={crud.form.enterpriseId}
            onChange={v => crud.set({ enterpriseId: v, departmentId: '' })}
            placeholder={t('common.selectEnterprise')}
            searchPlaceholder={t('common.searchEnterprise')}
            emptyText={t('common.enterpriseNotFound')}
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('position.belongDepartmentLabel')}</Label>
          <Combobox
            options={deptOptions}
            value={crud.form.departmentId}
            onChange={v => crud.set({ departmentId: v })}
            placeholder={t('common.selectDepartment')}
            searchPlaceholder={t('common.searchDepartment')}
            emptyText={t('common.selectEnterpriseFirst')}
            disabled={!crud.form.enterpriseId}
          />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('position.codeLabel')}</Label>
            <Input value={crud.form.code} onChange={e => crud.set({ code: e.target.value })} placeholder={t('position.codePlaceholder')} className="font-mono" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('position.nameLabel')}</Label>
            <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('position.namePlaceholder')} />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('position.levelLabel')}</Label>
            <Input value={crud.form.level} onChange={e => crud.set({ level: e.target.value })} placeholder={t('position.levelPlaceholder')} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('common.sortColumnHeader')}</Label>
            <Input type="number" value={crud.form.order} onChange={e => crud.set({ order: e.target.value })} min={0} />
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>{t('position.descriptionLabel')}</Label>
          <Input value={crud.form.description} onChange={e => crud.set({ description: e.target.value })} placeholder={t('position.descriptionPlaceholder')} />
        </div>
        <div className="flex items-center gap-2">
          <Switch id="pos-enabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
          <Label htmlFor="pos-enabled">{t('position.enableSwitchLabel')}</Label>
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
