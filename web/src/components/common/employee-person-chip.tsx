'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { type VariantProps } from 'class-variance-authority'
import { X } from 'lucide-react'
import { Badge, badgeVariants } from '@/components/ui/badge'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { cn } from '@/lib/utils'

/**
 * 人员引用的通用形状（列表 DTO 已带齐预览字段，花片弹层零请求）。
 * 与后端 PerformancePersonRefDto 对齐，但刻意不绑定具体域类型——
 * 任何「后端已解析好员工主数据快照」的场景都可复用。
 * 未命中主数据时只有 employeeNo（name 为空），花片显示工号、弹层给出说明。
 */
export interface EmployeePersonRef {
  employeeId?: string | null
  employeeNo: string
  name?: string | null
  departmentName?: string | null
  positionName?: string | null
  email?: string | null
  phone?: string | null
  /** EmployeeStatus：0 在职 / 1 停薪留职 / 2 离职 */
  status?: number | null
}

type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>['variant']>

function getStatusMeta(t: ReturnType<typeof useTranslations>): Record<number, { label: string; variant: BadgeVariant }> {
  return {
    0: { label: t('employeeChip.statusActive'), variant: 'success' },
    1: { label: t('employeeChip.statusOnLeave'), variant: 'warning' },
    2: { label: t('employeeChip.statusResigned'), variant: 'destructive' },
  }
}

/** 花片正面只标非在职，避免每个在职花片都挂「在职」。 */
function NonActiveStatusSuffix({
  status, t,
}: {
  status?: number | null
  t: ReturnType<typeof useTranslations>
}) {
  if (status !== 1 && status !== 2) return null
  const meta = getStatusMeta(t)[status]
  if (!meta) return null
  return (
    <span className={cn(
      'shrink-0 font-normal',
      status === 2 ? 'text-destructive' : 'text-warning',
    )}>
      {' · '}{meta.label}
    </span>
  )
}

/** 弹层里的员工基本信息（无请求，全部来自传入的 person 快照）。工号/邮箱等可选中复制。 */
function PersonDetail({ person }: { person: EmployeePersonRef }) {
  const t = useTranslations('Common')
  const statusMeta = person.status != null ? getStatusMeta(t)[person.status] : undefined

  if (!person.name) {
    return (
      <div className="select-text space-y-1.5">
        <p className="font-mono text-sm font-medium">{person.employeeNo}</p>
        <p className="text-muted-foreground text-xs">{t('employeeChip.noMasterData')}</p>
      </div>
    )
  }
  const rows: [string, string | null | undefined][] = [
    [t('employeeNo'), person.employeeNo],
    [t('department'), person.departmentName],
    [t('position'), person.positionName],
    [t('email'), person.email],
    [t('phone'), person.phone],
  ]
  return (
    <div className="select-text space-y-2">
      <div className="flex items-center justify-between gap-2">
        <div className="truncate font-medium leading-none">{person.name}</div>
        {statusMeta && (
          <Badge variant={statusMeta.variant} className="shrink-0 text-[10px]">{statusMeta.label}</Badge>
        )}
      </div>
      <dl className="grid grid-cols-[3.5rem_1fr] gap-x-2 gap-y-1.5 text-xs">
        {rows
          .filter(([, value]) => value)
          .map(([name, value]) => (
            <div key={name} className="contents">
              <dt className="text-muted-foreground">{name}</dt>
              <dd className="truncate text-foreground">{value}</dd>
            </div>
          ))}
      </dl>
    </div>
  )
}

/**
 * 可点击的员工花片：Badge 显示姓名（未命中主数据则显示工号），
 * 点击弹出基本信息（工号/部门/岗位/邮箱/手机/在职状态），零额外请求。
 * 传 onRemove 时尾部渲染移除按钮（表单已选人员场景）。
 */
export function EmployeePersonChip({
  person, onRemove, className,
}: { person: EmployeePersonRef; onRemove?: () => void; className?: string }) {
  const t = useTranslations('Common')
  const display = person.name || person.employeeNo
  return (
    <Badge variant="secondary" className={cn('max-w-full gap-0 p-0 font-normal', className)}>
      <Popover>
        <PopoverTrigger asChild>
          <button
            type="button"
            className={cn(
              'hover:bg-muted-foreground/15 inline-flex min-w-0 max-w-full items-center rounded-md px-2 py-0.5 focus-visible:outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)',
              onRemove && 'rounded-r-none',
            )}
          >
            <span className="truncate">{display}</span>
            <NonActiveStatusSuffix status={person.status} t={t} />
          </button>
        </PopoverTrigger>
        <PopoverContent align="start" className="w-72 p-3">
          <PersonDetail person={person} />
        </PopoverContent>
      </Popover>
      {onRemove && (
        <button
          type="button"
          aria-label={t('employeeChip.removeAriaLabel', { name: display })}
          className="hover:bg-muted-foreground/20 rounded-r-md p-1 focus-visible:outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
          onClick={onRemove}
        >
          <X className="size-3" />
        </button>
      )}
    </Badge>
  )
}

/**
 * 员工花片折叠组（表格单元格用）：默认露出前 maxVisible 个花片，
 * 其余收进「+N」弹层——弹层内一行一人（姓名 + 工号 · 部门），可选中复制，不撑高表格行。
 */
export function EmployeePersonChipList({
  persons, maxVisible = 3,
}: { persons: EmployeePersonRef[]; maxVisible?: number }) {
  const t = useTranslations('Common')
  const [selected, setSelected] = useState<EmployeePersonRef | null>(null)
  if (persons.length === 0) return <span className="text-muted-foreground text-xs">—</span>
  const visible = persons.slice(0, maxVisible)
  const restCount = persons.length - visible.length
  return (
    <div className="flex flex-wrap items-center gap-1">
      {visible.map((p) => <EmployeePersonChip key={p.employeeNo} person={p} />)}
      {restCount > 0 && (
        <Popover onOpenChange={(open) => { if (!open) setSelected(null) }}>
          <PopoverTrigger asChild>
            <button
              type="button"
              aria-label={t('employeeChip.moreAriaLabel', { count: persons.length })}
              className="rounded-md focus-visible:outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
            >
              <Badge variant="outline" className="hover:bg-accent cursor-pointer px-1.5 font-normal tabular-nums">
                +{restCount}
              </Badge>
            </button>
          </PopoverTrigger>
          <PopoverContent align="start" className="w-72 p-3">
            {selected ? (
              <div className="space-y-2">
                <button
                  type="button"
                  className="text-muted-foreground hover:text-foreground text-xs underline-offset-2 hover:underline"
                  onClick={() => setSelected(null)}
                >
                  {t('employeeChip.backToList')}
                </button>
                <PersonDetail person={selected} />
              </div>
            ) : (
              <>
                <p className="text-muted-foreground mb-2 text-xs font-medium">
                  {t('employeeChip.allCountTitle', { count: persons.length })}
                </p>
                <ul className="max-h-60 space-y-1 overflow-y-auto text-xs">
                  {persons.map((p) => (
                    <li key={p.employeeNo}>
                      <button
                        type="button"
                        className="hover:bg-accent flex w-full items-baseline justify-between gap-2 rounded-md px-1 py-1 text-left"
                        onClick={() => setSelected(p)}
                      >
                        <span className="truncate font-medium">
                          {p.name || p.employeeNo}
                          <NonActiveStatusSuffix status={p.status} t={t} />
                        </span>
                        <span className="text-muted-foreground shrink-0">
                          {[p.name ? p.employeeNo : null, p.departmentName].filter(Boolean).join(' · ')
                            || (p.name ? '' : t('employeeChip.noMasterData'))}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </PopoverContent>
        </Popover>
      )}
    </div>
  )
}
