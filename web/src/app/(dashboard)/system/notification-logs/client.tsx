'use client'

import { useLocale, useTranslations } from 'next-intl'
import type { ColumnDef, SortingState } from '@tanstack/react-table'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { Badge } from '@/components/ui/badge'
import { useCrudResource } from '@/lib/crud/use-crud-resource'

export interface NotificationLog {
  id: string
  channel: number
  title: string
  toUser?: string | null
  level: number
  source?: string | null
  success: boolean
  error?: string | null
  creationTime: string
}

function getChannelMap(t: ReturnType<typeof useTranslations>): Record<number, string> {
  return { 1: t('channelWebhook'), 2: t('channelEmail') }
}
function getLevelMap(t: ReturnType<typeof useTranslations>): Record<number, { text: string; variant: 'default' | 'secondary' | 'destructive' }> {
  return {
    0: { text: t('levelInfo'), variant: 'secondary' },
    1: { text: t('levelWarning'), variant: 'default' },
    2: { text: t('levelCritical'), variant: 'destructive' },
  }
}

function getColumns(t: ReturnType<typeof useTranslations>, locale: string): ColumnDef<NotificationLog>[] {
  const CHANNEL = getChannelMap(t)
  const LEVEL = getLevelMap(t)
  return [
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.time')} />,
      cell: ({ row }) => <span className="text-xs">{new Date(row.original.creationTime).toLocaleString(locale)}</span>,
    },
    {
      accessorKey: 'source',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.source')} />,
      cell: ({ row }) => row.original.source ?? '-',
    },
    {
      accessorKey: 'channel',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.channel')} />,
      cell: ({ row }) => CHANNEL[row.original.channel] ?? String(row.original.channel),
    },
    {
      accessorKey: 'level',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.level')} />,
      cell: ({ row }) => {
        const l = LEVEL[row.original.level] ?? LEVEL[0]
        return <Badge variant={l.variant}>{l.text}</Badge>
      },
    },
    {
      accessorKey: 'title',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.title')} />,
    },
    {
      accessorKey: 'toUser',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.recipient')} />,
      cell: ({ row }) => row.original.toUser ?? '-',
    },
    {
      accessorKey: 'success',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.result')} />,
      cell: ({ row }) =>
        row.original.success ? <Badge variant="secondary">{t('resultSuccess')}</Badge> : <Badge variant="destructive">{t('resultFailed')}</Badge>,
    },
    {
      accessorKey: 'error',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.error')} />,
      cell: ({ row }) => <span className="text-destructive text-xs break-all">{row.original.error ?? ''}</span>,
    },
  ]
}

export function NotificationLogsClient({
  data, initialPage = 0, initialPageSize = 15, initialSorting = [],
}: {
  data: { items: NotificationLog[]; totalCount: number }
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState
}) {
  const t = useTranslations('System.notificationLogs')
  const locale = useLocale()
  const columns = getColumns(t, locale)
  const list = useCrudResource<NotificationLog>({
    endpoint: 'notification/logs',
    initial: data,
    errorTitle: t('errorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
  })
  return (
    <>
      <h1 className="mb-4 shrink-0 text-lg font-semibold">{t('title')}</h1>
      <DataTable columns={columns} data={list.rows} {...list.tableProps} enableSearch={false} tableKey="notification-logs" />
    </>
  )
}
