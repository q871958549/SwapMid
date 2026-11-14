'use client'

import { useCallback, useEffect, useState } from 'react'
import { useTranslations } from 'next-intl'
import { type ColumnDef, type RowSelectionState, type SortingState } from '@tanstack/react-table'
import { KeyRound } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Combobox } from '@/components/ui/combobox'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { useHasPermission } from '@/components/providers/permissions-provider'
import { Permissions } from '@/lib/permissions'
import { DepartmentCell } from '@/components/common/department-cell'
import { type PagedResult } from '@/lib/api-client'
import { getApiErrorMessage } from '@/lib/api-error'
import { useLazyRef } from '@/lib/use-lazy-ref'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { useUrlListState } from '@/lib/crud/use-url-list-state'
import { cn, displayEmail } from '@/lib/utils'
import { useEmployeeBulkActions, EmployeeBulkBar, EmployeeBulkDialogs } from './employees-bulk-actions'
import { EMPLOYEE_LIST_MAX_PAGE_SIZE } from './constants'
import {
  defaultEmployeeForm,
  EmployeeFormDialog,
  type DepartmentOption,
  type Employee,
  type EmployeeFormState,
  type EnterpriseOption,
  type PositionOption,
} from './employee-dialogs'

export type { Employee, EnterpriseOption, DepartmentOption, PositionOption }

export function EmployeesClient({
  data,
  initialDepartmentId = '', initialStatus = '', initialAccount = '',
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
  initialPendingCount = null,
}: {
  data: PagedResult<Employee>
  initialDepartmentId?: string; initialStatus?: string; initialAccount?: string
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
  initialPendingCount?: number | null
}) {
  const t = useTranslations('Enterprise')
  const tc = useTranslations('Common')
  const entityName = t('common.employeeEntity')

  const EMP_STATUS = [
    { value: 0, label: t('employee.statusActive'), variant: 'success' as const },
    { value: 1, label: t('employee.statusSuspended'), variant: 'secondary' as const },
    { value: 2, label: t('employee.statusResigned'), variant: 'outline' as const },
  ]

  const { items: enterprises, ensure: ensureEnterprises } = useLazyRef<EnterpriseOption>('enterprises?maxResultCount=1000')
  const { items: departments, ensure: ensureDepartments } = useLazyRef<DepartmentOption>('departments?maxResultCount=2000')
  const { items: positions, ensure: ensurePositions } = useLazyRef<PositionOption>('positions?maxResultCount=2000&isEnabled=true')
  function ensureRefs() { void ensureDepartments(); void ensurePositions(); return ensureEnterprises() }
  useEffect(() => { void ensureDepartments() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const [filters, setFilters] = useUrlListState({
    departmentId: initialDepartmentId,
    status: initialStatus,
    account: initialAccount,
  })

  const list = useCrudResource<Employee>({
    endpoint: 'employees',
    initial: data,
    errorTitle: t('employee.errorTitleListLoad'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    pageSizeMax: EMPLOYEE_LIST_MAX_PAGE_SIZE,
    initialSearch,
    extraDeps: [filters.departmentId, filters.status, filters.account],
    buildParams: (qs) => {
      if (filters.departmentId) { qs.set('departmentId', filters.departmentId); qs.set('includeChildDepartments', 'true') }
      if (filters.status !== '') qs.set('status', filters.status)
      if (filters.account !== '') qs.set('hasAccount', filters.account)
    },
  })

  const [pendingAccountCount, setPendingAccountCount] = useState<number | null>(initialPendingCount)
  const refreshPending = useCallback(async () => {
    try {
      const res = await fetch('/api/proxy/employees?status=0&hasAccount=false&maxResultCount=1')
      if (!res.ok) return
      const d = (await res.json()) as { totalCount?: number }
      setPendingAccountCount(d.totalCount ?? 0)
    } catch { /* 计数仅作提示，失败静默不打扰 */ }
  }, [])
  const reloadAll = () => { void list.reload(); void refreshPending() }
  const reloadAllAndRefresh = () => { void list.reloadAndRefresh(); void refreshPending() }

  const crud = useCrudForm<EmployeeFormState, Employee>({
    entityName,
    endpoint: 'employees',
    defaultForm: defaultEmployeeForm,
    onSuccess: reloadAll,
    toForm: (e) => ({
      enterpriseId: e.enterpriseId, departmentId: e.departmentId ?? '',
      positionId: e.positionId ?? '',
      employeeNo: e.employeeNo, name: e.name, email: e.email ?? '',
      phone: e.phone ?? '', status: String(e.status),
    }),
    toBody: (form) => {
      const enterprise = enterprises.find(e => e.id === form.enterpriseId)
      const department = departments.find(d => d.id === form.departmentId)
      const position = positions.find(p => p.id === form.positionId)
      return {
        enterpriseId: form.enterpriseId,
        enterpriseCode: enterprise?.code ?? null,
        departmentId: form.departmentId || null,
        departmentCode: department?.code ?? null,
        positionId: form.positionId || null,
        positionCode: position?.code ?? null,
        employeeNo: form.employeeNo, name: form.name,
        email: form.email || null, phone: form.phone || null,
        position: null,
        status: Number(form.status),
      }
    },
    beforeCreate: async () => {
      const ents = await ensureRefs()
      return { enterpriseId: ents.find(e => e.isMain)?.id ?? '' }
    },
    beforeEdit: () => { void ensureRefs() },
  })

  const bulk = useEmployeeBulkActions(reloadAllAndRefresh)
  const [generatingId, setGeneratingId] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<string | null>(null)
  const [selectedRowIds, setSelectedRowIds] = useState<RowSelectionState>({})
  const canCreate = useHasPermission(Permissions.EnterpriseManagement.EmployeesCreate)
  const canUpdate = useHasPermission(Permissions.EnterpriseManagement.EmployeesUpdate)
  const canDelete = useHasPermission(Permissions.EnterpriseManagement.EmployeesDelete)
  const canMutate = canUpdate || canDelete

  async function generateAccount(employee: Employee) {
    setGeneratingId(employee.id)
    setFeedback(null)
    try {
      const res = await fetch('/api/proxy/account-users/generate-from-employee', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ employeeId: employee.id }),
      })
      if (!res.ok) throw new Error(await res.text())
      const result = await res.json()
      setFeedback(result.message ?? t('employee.accountAlreadyGeneratedMenuItem'))
      void reloadAllAndRefresh()
    } catch (error) {
      setFeedback(getApiErrorMessage(error, t('employee.accountGenFailed')))
    } finally {
      setGeneratingId(null)
    }
  }

  const columns: ColumnDef<Employee, unknown>[] = [
    primaryColumn<Employee>('employeeNo', tc('employeeNo'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 100,
      className: 'font-mono text-sm',
    }),
    primaryColumn<Employee>('name', t('employee.colName'), {
      onOpen: canUpdate ? crud.openEdit : undefined,
      size: 100,
    }),
    {
      accessorKey: 'enterpriseName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.belongEnterpriseColumnHeader')} />,
      size: 160,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.enterpriseName ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'departmentName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('employee.colDepartment')} />,
      size: 120,
      cell: ({ row }) => <DepartmentCell departmentId={row.original.departmentId} departmentName={row.original.departmentName} />,
    },
    {
      accessorKey: 'positionName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('employee.colPosition')} />,
      size: 130,
      cell: ({ row }) => (
        row.original.positionName
          ? <span className="text-sm">{row.original.positionName}</span>
          : <span className="text-sm text-muted-foreground/60">{t('employee.notBound')}</span>
      ),
    },
    {
      accessorKey: 'email',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('email')} />,
      size: 200,
      cell: ({ row }) => {
        const real = row.original.email && row.original.email.trim()
        return (
          <span className={cn('text-sm', real ? 'text-foreground' : 'text-muted-foreground/70')}>
            {displayEmail(row.original.email, row.original.name)}
          </span>
        )
      },
    },
    {
      accessorKey: 'phone',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('phone')} />,
      size: 130,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">{row.original.phone ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'userId',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.accountEntity')} />,
      size: 90,
      cell: ({ row }) => <Badge variant={row.original.userId ? 'success' : 'outline'}>{row.original.userId ? t('employee.accountGenerated') : t('employee.accountNotGenerated')}</Badge>,
    },
    {
      accessorKey: 'status',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('common.statusColumnHeader')} />,
      meta: { filterVariant: 'select', filterOptions: EMP_STATUS.map(s => ({ value: s.value, label: s.label })) },
      size: 80,
      cell: ({ row }) => {
        const s = EMP_STATUS.find(x => x.value === row.original.status)
        return <Badge variant={s?.variant ?? 'secondary'}>{s?.label ?? t('employee.statusUnknown')}</Badge>
      },
    },
    ...(canMutate
      ? [actionsColumn<Employee>({
          onEdit: canUpdate ? crud.openEdit : undefined,
          onDelete: canDelete ? crud.askDelete : undefined,
          extraItems: canUpdate
            ? (row) => (
              <DropdownMenuItem onClick={() => void generateAccount(row)} disabled={!!row.userId || generatingId === row.id}>
                <KeyRound className="mr-2 size-3.5" />{row.userId ? t('employee.accountAlreadyGeneratedMenuItem') : t('employee.generateAccountMenuItem')}
              </DropdownMenuItem>
            )
            : undefined,
        })]
      : []),
  ]

  return (
    <>
      <PageHeader
        title={t('employee.pageTitle')}
        description={t('employee.countDescription', { count: list.total })}
        extra={feedback && <span className="text-sm text-muted-foreground">{feedback}</span>}
        action={
          <div className="flex items-center gap-2">
            {canUpdate && (
              <Button variant="outline" disabled={bulk.batchBusy} onClick={() => bulk.openBulkDialog()}>
                <KeyRound className="mr-2 size-4" />{t('employee.bulkProvisionButton')}
              </Button>
            )}
            {canCreate && (
              <CreateButton label={t('common.createEntityTitle', { entityName })} onClick={crud.openCreate} />
            )}
          </div>
        }
      />

      <EmployeeBulkDialogs bulk={bulk} pendingAccountCount={pendingAccountCount} />

      <div className="mb-3 flex flex-wrap items-center gap-2 rounded-lg border bg-muted/30 px-3 py-2">
        <span className="text-muted-foreground shrink-0 text-sm">{t('employee.deptFilterLabel')}</span>
        <div className="w-64 min-w-0">
          <Combobox
            options={departments.map(d => ({ value: d.id, label: d.name, description: d.enterpriseName ?? d.code }))}
            value={filters.departmentId}
            onChange={(v) => { setFilters({ departmentId: v }); list.setPage(0) }}
            placeholder={t('employee.allDepartments')}
            searchPlaceholder={t('common.searchDepartment')}
            emptyText={t('employee.deptNotFound')}
            clearable
          />
        </div>
        {filters.departmentId && <span className="text-muted-foreground shrink-0 text-xs">{t('employee.includesChildDeptsHint')}</span>}
        <span className="text-muted-foreground ml-2 shrink-0 text-sm">{t('common.statusColumnHeader')}</span>
        <Select value={filters.status === '' ? 'all' : filters.status} onValueChange={(v) => { setFilters({ status: v === 'all' ? '' : v }); list.setPage(0) }}>
          <SelectTrigger className="h-8 w-28"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('employee.allStatus')}</SelectItem>
            <SelectItem value="0">{t('employee.statusActive')}</SelectItem>
            <SelectItem value="1">{t('employee.statusSuspended')}</SelectItem>
            <SelectItem value="2">{t('employee.statusResigned')}</SelectItem>
          </SelectContent>
        </Select>
        <span className="text-muted-foreground ml-2 shrink-0 text-sm">{t('common.accountEntity')}</span>
        <Select value={filters.account === '' ? 'all' : filters.account} onValueChange={(v) => { setFilters({ account: v === 'all' ? '' : v }); list.setPage(0) }}>
          <SelectTrigger className="h-8 w-28"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t('employee.allAccounts')}</SelectItem>
            <SelectItem value="false">{t('employee.accountNotGenerated')}</SelectItem>
            <SelectItem value="true">{t('employee.accountGenerated')}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <DataTable
        columns={columns}
        data={list.rows}
        searchPlaceholder={t('employee.searchPlaceholder')}
        tableKey="employees"
        {...list.tableProps}
        maxPageSize={EMPLOYEE_LIST_MAX_PAGE_SIZE}
        enableRowSelection={canMutate}
        selectedRowIds={selectedRowIds}
        onSelectedRowIdsChange={setSelectedRowIds}
        getRowId={(row) => row.id}
        bulkActions={canMutate
          ? (_rows, clearSelection, selectedIds) => (
            <EmployeeBulkBar
              ids={selectedIds}
              clear={clearSelection}
              bulk={bulk}
              canUpdate={canUpdate}
              canDelete={canDelete}
            />
          )
          : undefined}
      />

      <EmployeeFormDialog
        crud={crud}
        enterprises={enterprises}
        departments={departments}
        positions={positions}
      />

      <DeleteConfirm
        crud={crud}
        entityName={entityName}
        description={(target) => <>{t('employee.deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
