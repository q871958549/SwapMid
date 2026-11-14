'use client'

import { type ReactNode } from 'react'
import { type Column, type Row, type Table, flexRender } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { resolveColumnTitle } from './data-table-columns'

interface DataTableCardsProps<TData> {
  table: Table<TData>
  isFiltered: boolean
  loading: boolean
  emptyState?: ReactNode
}

function cellOf<TData>(row: Row<TData>, columnId: string) {
  return row.getVisibleCells().find((cell) => cell.column.id === columnId)
}

/**
 * 窄屏列表：同一套可见列排成卡片。左钉列（没有则第一列数据列）当标题，
 * 其余可见列是「字段名：值」，选择留在卡片上，操作留在底部。
 * 列菜单里藏起的列这里同样不出现。
 */
export function DataTableCards<TData>({ table, isFiltered, loading, emptyState }: DataTableCardsProps<TData>) {
  const t = useTranslations('Common')
  const rows = table.getRowModel().rows
  const columns = table.getVisibleLeafColumns()
  const selectColumn = columns.find((column) => column.id === 'select')
  const actionsColumn = columns.find((column) => column.id === 'actions')
  const dataColumns = columns.filter((column) => column.id !== 'select' && column.id !== 'actions')
  const titleColumn = dataColumns.find((column) => column.columnDef.meta?.pin === 'left') ?? dataColumns[0]
  const fieldColumns = dataColumns.filter((column) => column.id !== titleColumn?.id)
  const selectHeader = selectColumn
    ? table.getHeaderGroups()[0]?.headers.find((header) => header.column.id === 'select')
    : undefined

  return (
    <div
      data-slot="data-table-cards"
      className={cn('scrollbar-fade min-h-0 flex-1 space-y-2 overflow-y-auto', loading && 'pointer-events-none opacity-60')}
    >
      {selectHeader && rows.length > 0 && (
        <div className="flex items-center gap-2 px-1">
          {flexRender(selectHeader.column.columnDef.header, selectHeader.getContext())}
          <span className="text-muted-foreground text-sm">{t('selectCurrentPage')}</span>
        </div>
      )}
      {rows.length === 0 ? (
        <div className="px-1 py-8 text-sm">{isFiltered ? <p className="text-muted-foreground text-center">{t('noMatch')}</p> : emptyState ?? <p className="text-muted-foreground text-center">{t('noData')}</p>}</div>
      ) : (
        rows.map((row) => (
          <Card key={row.id} className="gap-3 py-3">
            <CardContent className="space-y-2 px-3">
              <div className="flex items-start gap-2">
                {selectColumn && renderCell(selectColumn, row, 'pt-0.5 shrink-0')}
                {titleColumn && renderCell(titleColumn, row, 'min-w-0 flex-1 text-sm font-medium')}
              </div>
              {fieldColumns.length > 0 && (
                <dl className="space-y-1.5">
                  {fieldColumns.map((column) => {
                    const cell = cellOf(row, column.id)
                    if (!cell) return null
                    return (
                      <div key={column.id} className="grid grid-cols-[5.5rem_minmax(0,1fr)] items-start gap-2 text-sm">
                        <dt className="text-muted-foreground">{resolveColumnTitle(column, table, t)}</dt>
                        <dd className="min-w-0 break-words">{flexRender(column.columnDef.cell, cell.getContext())}</dd>
                      </div>
                    )
                  })}
                </dl>
              )}
              {actionsColumn && (
                <div className="flex justify-end [&_button]:size-11">
                  {renderCell(actionsColumn, row)}
                </div>
              )}
            </CardContent>
          </Card>
        ))
      )}
    </div>
  )
}

function renderCell<TData>(column: Column<TData, unknown>, row: Row<TData>, className?: string) {
  const cell = cellOf(row, column.id)
  if (!cell) return null
  return <div className={className}>{flexRender(column.columnDef.cell, cell.getContext())}</div>
}
