'use client'

import { useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { Wand2 } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, booleanBadgeColumn, primaryColumn } from '@/components/data-table/column-factories'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { type PagedResult } from '@/lib/api-client'

export interface CodeRuleDto {
  id: string
  entityType: string
  displayName: string
  prefix: string
  suffix: string | null
  includeDate: boolean
  dateFormat: string | null
  sequencePad: number
  resetCycle: number
  currentSequence: number
  isEnabled: boolean
  description: string | null
  sample: string
  creationTime: string
}

// 可选业务对象清单现由**后端**提供（GET /code-rules/entity-types，各域贡献者注册），
// 前端下拉选项与列表中文映射均从 entityTypes prop 派生——不再硬编码闭集，新域扩展零前端改动。
export interface CodeRuleEntityType {
  entityType: string
  displayName: string
  group: string
}

/** 模块级常量含 UI 文案，改为由持有 useTranslations 的组件调用的工厂函数（纯函数不能直接调 hook）。 */
function getResetCycles(t: ReturnType<typeof useTranslations>) {
  return [
    { value: 0, label: t('resetCycles.never') },
    { value: 1, label: t('resetCycles.daily') },
    { value: 2, label: t('resetCycles.monthly') },
    { value: 3, label: t('resetCycles.yearly') },
  ]
}

function getDateFormats(t: ReturnType<typeof useTranslations>) {
  return [
    { value: '', label: t('dateFormats.none') },
    { value: 'yyyyMMdd', label: 'yyyyMMdd（20240101）' },
    { value: 'yyyyMM', label: 'yyyyMM（202401）' },
    { value: 'yyyy', label: 'yyyy（2024）' },
  ]
}

interface FormState {
  entityType: string; displayName: string; prefix: string; suffix: string
  includeDate: boolean; dateFormat: string; sequencePad: string
  resetCycle: string; isEnabled: boolean; description: string
}

const defaultForm: FormState = {
  entityType: '', displayName: '', prefix: '', suffix: '',
  includeDate: false, dateFormat: 'yyyyMMdd', sequencePad: '4',
  resetCycle: '0', isEnabled: true, description: '',
}

function buildSample(form: FormState): string {
  const actualDate = form.includeDate && form.dateFormat
    ? (() => {
        try {
          const d = new Date()
          const fmt = form.dateFormat
          if (fmt === 'yyyyMMdd') return `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`
          if (fmt === 'yyyyMM') return `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}`
          if (fmt === 'yyyy') return `${d.getFullYear()}`
          return ''
        } catch { return '' }
      })()
    : ''
  const pad = Math.max(1, Math.min(20, Number(form.sequencePad) || 4))
  const seq = '1'.padStart(pad, '0')
  return `${form.prefix}${actualDate}${seq}${form.suffix}`
}

export function CodeRulesClient({
  data, entityTypes, initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  data: PagedResult<CodeRuleDto>
  entityTypes: CodeRuleEntityType[]
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('System.codeRules')
  const RESET_CYCLES = getResetCycles(t)
  const DATE_FORMATS = getDateFormats(t)
  // 业务对象 中文名映射 + 按分组归并的下拉选项，均从后端清单派生（单一事实源，映射不到才回退英文）——
  // displayName/group 是后端各域贡献者注册的业务数据，不翻译。
  const entityTypeLabel = (v: string) => entityTypes.find(et => et.entityType === v)?.displayName ?? v
  const entityTypeGroups = entityTypes.reduce<Record<string, CodeRuleEntityType[]>>((acc, et) => {
    (acc[et.group] ??= []).push(et)
    return acc
  }, {})

  // 服务端分页：翻页/搜索下推后端（/code-rules 支持 skipCount/maxResultCount/Filter）。
  // urlSync：把 page/size/q 镜像到 URL，切走再切回/刷新可恢复。初值来自 page.tsx 的 searchParams。
  const list = useCrudResource<CodeRuleDto>({
    endpoint: 'code-rules',
    initial: data,
    errorTitle: t('errorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  const crud = useCrudForm<FormState, CodeRuleDto>({
    entityName: t('entityName'),
    endpoint: 'code-rules',
    defaultForm,
    getName: (item) => item.displayName,
    toForm: (rule) => ({
      entityType: rule.entityType,
      displayName: rule.displayName,
      prefix: rule.prefix,
      suffix: rule.suffix ?? '',
      includeDate: rule.includeDate,
      dateFormat: rule.dateFormat ?? 'yyyyMMdd',
      sequencePad: String(rule.sequencePad),
      resetCycle: String(rule.resetCycle),
      isEnabled: rule.isEnabled,
      description: rule.description ?? '',
    }),
    toBody: (form) => ({
      entityType: form.entityType,
      displayName: form.displayName,
      prefix: form.prefix,
      suffix: form.suffix || null,
      includeDate: form.includeDate,
      dateFormat: form.includeDate ? form.dateFormat || null : null,
      sequencePad: Number(form.sequencePad) || 4,
      resetCycle: Number(form.resetCycle),
      isEnabled: form.isEnabled,
      description: form.description || null,
    }),
    onSuccess: list.reload,
  })

  const tc = useTranslations('Common')
  const sc = useTranslations('System.common')

  const columns: ColumnDef<CodeRuleDto, unknown>[] = [
    {
      accessorKey: 'entityType',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.entityType')} />,
      size: 130,
      cell: ({ row }) => (
        <Badge variant="outline" className="font-mono text-xs">{entityTypeLabel(row.original.entityType)}</Badge>
      ),
    },
    primaryColumn<CodeRuleDto>('displayName', t('columns.displayName'), {
      size: 160,
      onOpen: crud.openEdit,
    }),
    {
      accessorKey: 'prefix',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.prefix')} />,
      size: 100,
      cell: ({ row }) => <code className="text-sm bg-muted px-1 py-0.5 rounded">{row.original.prefix || '—'}</code>,
    },
    {
      accessorKey: 'sample',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.sample')} />,
      size: 180,
      cell: ({ row }) => (
        <code className="text-sm font-semibold text-primary bg-primary/5 px-1.5 py-0.5 rounded">
          {row.original.sample}
        </code>
      ),
    },
    {
      accessorKey: 'sequencePad',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.sequencePad')} />,
      size: 90,
      meta: { numeric: true },
      cell: ({ row }) => <span className="text-sm tabular-nums text-muted-foreground">{t('columns.sequencePadValue', { count: row.original.sequencePad })}</span>,
    },
    {
      accessorKey: 'resetCycle',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.resetCycle')} />,
      size: 100,
      cell: ({ row }) => {
        const c = RESET_CYCLES.find(r => r.value === row.original.resetCycle)
        return <span className="text-sm text-muted-foreground">{c?.label ?? '—'}</span>
      },
    },
    {
      accessorKey: 'currentSequence',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.currentSequence')} />,
      size: 90,
      cell: ({ row }) => (
        <span className="text-sm tabular-nums text-muted-foreground">{row.original.currentSequence}</span>
      ),
    },
    booleanBadgeColumn<CodeRuleDto>('isEnabled', tc('status'), { trueLabel: tc('enabled'), falseLabel: sc('disabledLabel') }),
    actionsColumn<CodeRuleDto>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
  ]

  const sample = buildSample(crud.form)

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('pageDescriptionTemplate', { count: list.total })}
        action={<CreateButton label={t('createButton')} onClick={crud.openCreate} />}
      />
      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        searchPlaceholder={t('searchPlaceholder')}
        tableKey="code-rules"
        showAuditColumns={false}
      />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('dialog.createTitle'), edit: t('dialog.editTitle') }}
        canSubmit={!!crud.form.entityType && !!crud.form.displayName}
        contentClassName="sm:max-w-[540px]"
      >
        {/* 业务对象 + 规则名称 */}
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('dialog.entityTypeLabel')}</Label>
            {/* 业务对象是规则的身份键（GenerateAsync 按 EntityType 查规则、种子按它幂等），编辑态禁止改动：
                改了会与其它规则撞唯一键或让编号生成失灵。仅新建时可选。 */}
            <Select
              value={crud.form.entityType}
              disabled={crud.mode === 'edit'}
              onValueChange={v => crud.set({ entityType: v, displayName: `${entityTypeLabel(v)}${t('dialog.displayNameSuffix')}` })}
            >
              <SelectTrigger className="h-9"><SelectValue placeholder={t('dialog.entityTypePlaceholder')} /></SelectTrigger>
              <SelectContent>
                {Object.entries(entityTypeGroups).map(([group, items]) => (
                  <SelectGroup key={group}>
                    <SelectLabel>{group}</SelectLabel>
                    {items.map(et => (
                      <SelectItem key={et.entityType} value={et.entityType}>{et.displayName}</SelectItem>
                    ))}
                  </SelectGroup>
                ))}
              </SelectContent>
            </Select>
            {crud.mode === 'edit' && (
              <p className="text-[11px] text-muted-foreground">{t('dialog.entityTypeLockedHint')}</p>
            )}
          </div>
          <div className="space-y-1.5">
            <Label>{t('dialog.displayNameLabel')}</Label>
            <Input value={crud.form.displayName} onChange={e => crud.set({ displayName: e.target.value })} placeholder={t('dialog.displayNamePlaceholder')} />
          </div>
        </div>

        {/* 前缀 + 后缀 */}
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('dialog.prefixLabel')}</Label>
            <Input value={crud.form.prefix} onChange={e => crud.set({ prefix: e.target.value })} placeholder="DEPT-" className="font-mono" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('dialog.suffixLabel')}</Label>
            <Input value={crud.form.suffix} onChange={e => crud.set({ suffix: e.target.value })} placeholder={t('dialog.suffixPlaceholder')} className="font-mono" />
          </div>
        </div>

        {/* 日期 + 格式 */}
        <div className="grid grid-cols-2 gap-3">
          <div className="flex items-center gap-2 pt-5">
            <Switch id="include-date" checked={crud.form.includeDate} onCheckedChange={v => crud.set({ includeDate: v })} />
            <Label htmlFor="include-date">{t('dialog.includeDateLabel')}</Label>
          </div>
          <div className="space-y-1.5">
            <Label>{t('dialog.dateFormatLabel')}</Label>
            <Select
              value={crud.form.includeDate ? crud.form.dateFormat : ''}
              onValueChange={v => crud.set({ dateFormat: v })}
              disabled={!crud.form.includeDate}
            >
              <SelectTrigger className="h-9"><SelectValue /></SelectTrigger>
              <SelectContent>
                {DATE_FORMATS.filter(d => d.value).map(d => (
                  <SelectItem key={d.value} value={d.value}>{d.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        {/* 序号位数 + 重置周期 */}
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('dialog.sequencePadLabel')}</Label>
            <Input
              type="number" min={1} max={20}
              value={crud.form.sequencePad}
              onChange={e => crud.set({ sequencePad: e.target.value })}
            />
          </div>
          <div className="space-y-1.5">
            <Label>{t('dialog.resetCycleLabel')}</Label>
            <Select value={crud.form.resetCycle} onValueChange={v => crud.set({ resetCycle: v })}>
              <SelectTrigger className="h-9"><SelectValue /></SelectTrigger>
              <SelectContent>
                {RESET_CYCLES.map(r => (
                  <SelectItem key={r.value} value={String(r.value)}>{r.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        {/* 示例预览 */}
        <div className="rounded-lg bg-muted/50 border px-4 py-2.5 flex items-center gap-3">
          <Wand2 className="size-4 text-muted-foreground shrink-0" />
          <div>
            <p className="text-xs text-muted-foreground mb-0.5">{t('dialog.samplePreviewLabel')}</p>
            <code className="text-sm font-bold text-primary">{sample || t('dialog.samplePlaceholder')}</code>
          </div>
        </div>

        {/* 描述 + 启用 */}
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('dialog.descriptionLabel')}</Label>
            <Input value={crud.form.description} onChange={e => crud.set({ description: e.target.value })} placeholder={t('dialog.descriptionPlaceholder')} />
          </div>
          <div className="flex items-center gap-2 pt-5">
            <Switch id="rule-enabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
            <Label htmlFor="rule-enabled">{t('dialog.enabledLabel')}</Label>
          </div>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('delete.entityName')}
        description={(target) => <>{t('delete.descriptionTemplate', { name: target.displayName })}</>}
      />
    </>
  )
}
