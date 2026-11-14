import { type Header, type Table } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { ChevronDown } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select'
import { deriveSelectOptions } from './data-table-columns'
import { type RangeFilterValue } from './data-table-filters'

// 列筛选行的单元格——按列 meta.filterVariant 三态渲染：
// 1) select（枚举/布尔）：下拉「全部 + 各选项」，值写成 { eq }；
// 2) numberRange / dateRange：「起 ~ 止」双输入闭区间，值写成 { from?, to?, kind }；
// 3) 兜底：普通文本框（子串匹配）。
// 结构列（选择/操作）不渲染筛选控件——由 data-table-grid 在外层用 STRUCTURAL_COL_IDS 门控。
export function ColumnFilterCell<TData>({
  header,
  table,
}: {
  header: Header<TData, unknown>
  table: Table<TData>
}) {
  const t = useTranslations('Common')
  const meta = header.column.columnDef.meta
  const isSelectLike = meta?.filterVariant === 'select' || meta?.filterVariant === 'multiSelect'
  const opts = isSelectLike
    ? (meta.filterOptions ?? deriveSelectOptions(table, header.column.id))
    : undefined

  if (meta?.filterVariant === 'select' && opts?.length) {
    const fv = header.column.getFilterValue() as { eq: unknown } | undefined
    const cur = fv && typeof fv === 'object' && 'eq' in fv ? String(fv.eq) : '__all'
    return (
      <Select
        value={cur}
        onValueChange={(val) => {
          if (val === '__all') { header.column.setFilterValue(undefined); return }
          const opt = opts.find((o) => String(o.value) === val)
          header.column.setFilterValue(opt ? { eq: opt.value } : undefined)
        }}
      >
        <SelectTrigger className="h-7! w-full justify-between gap-1 rounded-md px-2 py-0 text-xs font-normal [&>svg]:size-3.5">
          <SelectValue placeholder={t('all')} />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="__all">{t('all')}</SelectItem>
          {opts.map((o) => <SelectItem key={String(o.value)} value={String(o.value)}>{o.label}</SelectItem>)}
        </SelectContent>
      </Select>
    )
  }

  if (meta?.filterVariant === 'multiSelect' && opts?.length) {
    const fv = header.column.getFilterValue() as { in: unknown[] } | undefined
    const selected = new Set((fv?.in ?? []).map(String))
    const toggle = (value: string | number | boolean) => {
      const next = new Set(selected)
      const key = String(value)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      // 从 opts 回取原始 value（保留类型），空集则清筛选。
      const inValues = opts.filter((o) => next.has(String(o.value))).map((o) => o.value)
      header.column.setFilterValue(inValues.length ? { in: inValues } : undefined)
    }
    return (
      <Popover>
        <PopoverTrigger asChild>
          <Button variant="outline" size="sm" className="h-7 w-full justify-between gap-1 px-2 text-xs font-normal">
            <span className="truncate">{selected.size === 0 ? t('all') : t('selectedCount', { count: selected.size })}</span>
            <ChevronDown className="size-3.5 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" className="w-44 p-1">
          <div className="max-h-56 space-y-0.5 overflow-auto">
            {opts.map((o) => {
              const key = String(o.value)
              return (
                <label key={key} className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-xs hover:bg-muted">
                  <Checkbox checked={selected.has(key)} onCheckedChange={() => toggle(o.value)} />
                  <span className="truncate">{o.label}</span>
                </label>
              )
            })}
          </div>
        </PopoverContent>
      </Popover>
    )
  }

  if (meta?.filterVariant === 'numberRange' || meta?.filterVariant === 'dateRange') {
    const kind = meta.filterVariant === 'numberRange' ? 'number' : 'date'
    const rv = (header.column.getFilterValue() as RangeFilterValue | undefined) ?? { kind }
    const setBound = (patch: Partial<RangeFilterValue>) => {
      const next = { ...rv, ...patch, kind }
      header.column.setFilterValue(!next.from && !next.to ? undefined : next)
    }
    return (
      <div className="flex items-center gap-1">
        <Input
          type={kind === 'number' ? 'number' : 'text'}
          value={rv.from ?? ''}
          onChange={(e) => setBound({ from: e.target.value })}
          placeholder={kind === 'date' ? t('rangeFromDatePlaceholder') : t('rangeFromNumberPlaceholder')}
          className="h-7 w-full px-1.5 text-xs font-normal"
        />
        <span className="text-muted-foreground text-xs">~</span>
        <Input
          type={kind === 'number' ? 'number' : 'text'}
          value={rv.to ?? ''}
          onChange={(e) => setBound({ to: e.target.value })}
          placeholder={kind === 'date' ? t('rangeToDatePlaceholder') : t('rangeToNumberPlaceholder')}
          className="h-7 w-full px-1.5 text-xs font-normal"
        />
      </div>
    )
  }

  return (
    <Input
      value={String(header.column.getFilterValue() ?? '')}
      onChange={(e) => header.column.setFilterValue(e.target.value || undefined)}
      placeholder={t('filterPlaceholder')}
      className="h-7 w-full px-2 text-xs font-normal"
    />
  )
}
