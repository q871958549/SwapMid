import { isValidElement } from 'react'
import { type Column, type ColumnDef, type Table } from '@tanstack/react-table'
import { Checkbox } from '@/components/ui/checkbox'
import { getColumnKey, STRUCTURAL_COL_IDS } from './use-table-persistence'
import { getRegisteredColumnTitle, registerColumnTitle } from './data-table-column-header'

// 列标题/兜底文案由调用方（data-table.tsx，已持有 useTranslations('Common') 求出的 t）
// 经各导出函数的 t 参数传入——本文件全是普通函数、非 React 组件，不能直接调 hook
// （同 data-table-audit.tsx 的 buildAudit* 拆分理由）。
type CommonT = (key: string) => string

// 列 ID → Common 翻译 key 的兜底名字典：列未显式给 title（meta.title 或 DataTableColumnHeader）
// 时回退查此表，再经 t(key) 求本地化文案。与已有语义重叠的列复用既有 key（如 email/employeeNo/
// department/position/creator/lastModifier/creationTime/lastModificationTime/actions）。
const COLUMN_TITLE_FALLBACK_KEYS: Record<string, string> = {
  id: 'columnId',
  code: 'code',
  name: 'name',
  userName: 'account',
  email: 'email',
  phone: 'generalPhone',
  phoneNumber: 'mobileNumber',
  description: 'description',
  order: 'sortOrder',
  status: 'status',
  isEnabled: 'enabled',
  isActive: 'status',
  isDefault: 'defaultRole',
  isStatic: 'staticRole',
  isPublic: 'publicRole',
  path: 'path',
  httpMethod: 'httpMethod',
  upstreamUrl: 'upstreamUrl',
  groupName: 'groupName',
  routeCount: 'routeCount',
  type: 'type',
  retryCount: 'retryCount',
  topicName: 'topic',
  endpointUrl: 'endpointUrl',
  filterRule: 'filterRule',
  subject: 'topic',
  payload: 'payload',
  errorMessage: 'errorMessage',
  deliveredAt: 'deliveredAt',
  sourceName: 'sourceName',
  targetName: 'targetName',
  scheduleCron: 'scheduleCron',
  lastRunAt: 'lastRunAt',
  enterpriseName: 'ownerEnterprise',
  departmentName: 'department',
  positionName: 'position',
  parentName: 'parentDepartment',
  employeeNo: 'employeeNo',
  level: 'jobLevel',
  contactPerson: 'contactPerson',
  contactPhone: 'contactPhone',
  version: 'version',
  definitionName: 'workflowName',
  definitionVersion: 'workflowVersion',
  currentStepName: 'currentStep',
  triggerType: 'triggerType',
  startedAt: 'startedAt',
  finishedAt: 'finishedAt',
  durationSeconds: 'durationSeconds',
  processedRows: 'processedRows',
  creationTime: 'creationTime',
  creatorName: 'creator',
  creatorId: 'creator',
  lastModificationTime: 'lastModificationTime',
  lastModifierName: 'lastModifier',
  lastModifierId: 'lastModifier',
  creator: 'creator',
  lastModifier: 'lastModifier',
  enterpriseCode: 'enterpriseCode',
  departmentCode: 'departmentCode',
  positionCode: 'positionCode',
  parentCode: 'parentDepartmentCode',
  actions: 'actions',
  select: 'selectColumn',
}

function titleFallback(colId: string, t: CommonT): string {
  const key = COLUMN_TITLE_FALLBACK_KEYS[colId]
  return key ? t(key) : t('unnamedColumn')
}

// 行选择列（左固定的复选框列）——DataTable 开 enableRowSelection 时插到列首。
// header 全选/半选当前页、cell 选单行；结构列，宽 44、不可排序/隐藏，中文名「选择」。
export function buildSelectionColumn<TData>(t: CommonT): ColumnDef<TData, unknown> {
  return {
    id: 'select',
    size: 44,
    enableSorting: false,
    enableHiding: false,
    header: ({ table }) => (
      <Checkbox
        aria-label={t('selectCurrentPage')}
        checked={table.getIsAllPageRowsSelected() ? true : table.getIsSomePageRowsSelected() ? 'indeterminate' : false}
        onCheckedChange={(value) => table.toggleAllPageRowsSelected(!!value)}
      />
    ),
    cell: ({ row }) => (
      <Checkbox
        aria-label={t('selectRow')}
        checked={row.getIsSelected()}
        onCheckedChange={(value) => row.toggleSelected(!!value)}
      />
    ),
    meta: { title: t('selectColumn') },
  }
}

// 结构列（选择/操作）若命中兜底名则补 title + header，保证列菜单/导出有中文名。
export function normalizeStructuralColumns<TData>(columns: ColumnDef<TData, unknown>[], t: CommonT): ColumnDef<TData, unknown>[] {
  return columns.map((column) => {
    const key = getColumnKey(column)
    const titleKey = COLUMN_TITLE_FALLBACK_KEYS[key]
    if (!STRUCTURAL_COL_IDS.has(key) || !titleKey) return column
    const title = t(titleKey)
    registerColumnTitle(key, title)
    return {
      ...column,
      id: column.id ?? key,
      size: column.size,
      header: column.header ?? (() => <span className="text-xs font-medium">{title}</span>),
      meta: { ...(column.meta ?? {}), title },
    } as ColumnDef<TData, unknown>
  })
}

// 把所有列的中文名写入全局注册表（供 DataTableColumnHeader 渲染前，列菜单/导出取名）。
export function registerColumnTitles<TData>(columns: ColumnDef<TData, unknown>[], t: CommonT) {
  columns.forEach((column) => {
    const key = getColumnKey(column)
    const titleKey = COLUMN_TITLE_FALLBACK_KEYS[key]
    const title = (column.meta as { title?: string } | undefined)?.title ?? (titleKey ? t(titleKey) : undefined)
    if (key && title) registerColumnTitle(key, title)
  })
}

// 解析列中文显示名：meta.title → string header → header 工厂的 title prop → 注册表 → 兜底。
// 不优先依赖注册表——它在表头"渲染时"才写入（列菜单/导出在首帧先于表头求值），且按 colId 全局
// 共享，多页同名列互相串味（如 orderNo 在工单时效叫"工单号码"、采购退料叫"单据编号"）。
export function resolveColumnTitle<TData>(col: Column<TData, unknown>, table: Table<TData>, t: CommonT): string {
  const def = col.columnDef
  let title: string | null = (def.meta as { title?: string } | undefined)?.title
    ?? (typeof def.header === 'string' ? def.header : null)

  if (!title && typeof def.header === 'function') {
    const el = def.header({ column: col, header: undefined, table } as never)
    if (isValidElement(el)) {
      const propTitle = (el.props as { title?: unknown }).title
      if (typeof propTitle === 'string') title = propTitle
    }
  }

  title ??= getRegisteredColumnTitle(col.id) ?? titleFallback(col.id, t)

  if (title === t('unnamedColumn') && process.env.NODE_ENV !== 'production') {
    console.warn(`[DataTable] column "${col.id}" is missing a Chinese title. Add meta.title or use DataTableColumnHeader.`)
  }

  return title
}

// 下拉筛选选项：列声明 filterOptions 则用之；否则（如外部系统返回的动态字符串状态列）从当前数据去重生成。
export function deriveSelectOptions<TData>(table: Table<TData>, columnId: string): { value: string | number | boolean; label: string }[] {
  const seen = new Set<string>()
  const out: { value: string | number | boolean; label: string }[] = []
  for (const row of table.getCoreRowModel().rows) {
    const v = row.getValue(columnId)
    if (v === null || v === undefined || v === '') continue
    const key = String(v)
    if (seen.has(key)) continue
    seen.add(key)
    out.push({ value: v as string | number | boolean, label: key })
    if (out.length >= 50) break
  }
  return out.sort((a, b) => String(a.label).localeCompare(String(b.label), 'zh-Hans'))
}
