'use client'

import { useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { X } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, booleanBadgeColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { type PagedResult } from '@/lib/api-client'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'

function splitSources(sources: string): string[] {
  return [...new Set(sources.split(',').map((s) => s.trim()).filter(Boolean))]
}

export interface NotificationRecipient {
  id: string
  name: string
  email?: string | null
  webhookTarget?: string | null
  minLevel: number
  sources?: string | null
  isEnabled: boolean
}

interface FormState {
  name: string
  email: string
  webhookTarget: string
  minLevel: string
  sources: string
  isEnabled: boolean
}

const defaultForm: FormState = {
  name: '',
  email: '',
  webhookTarget: '',
  minLevel: '0',
  sources: '',
  isEnabled: true,
}

function getLevels(t: ReturnType<typeof useTranslations>): Record<number, { label: string; variant: 'secondary' | 'default' | 'destructive' }> {
  return {
    0: { label: t('levels.hint'), variant: 'secondary' },
    1: { label: t('levels.warning'), variant: 'default' },
    2: { label: t('levels.critical'), variant: 'destructive' },
  }
}

export function NotificationRecipientsClient({
  data, knownSources = [], initialPage = 0, initialPageSize = 15, initialSorting = [],
}: {
  data: PagedResult<NotificationRecipient>
  knownSources?: string[]
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState
}) {
  const t = useTranslations('System.notificationRecipients')
  const tc = useTranslations('Common')
  const sc = useTranslations('System.common')
  const LEVELS = getLevels(t)
  const list = useCrudResource<NotificationRecipient>({
    endpoint: 'notification/recipients',
    initial: data,
    errorTitle: t('errorTitle'),
    urlSync: true,
    initialPage,
    pageSize: initialPageSize,
    initialSorting,
    buildParams: (qs) => { if (!qs.has('Sorting')) qs.set('sorting', 'name') },
  })
  const crud = useCrudForm<FormState, NotificationRecipient>({
    entityName: t('entityName'),
    endpoint: 'notification/recipients',
    defaultForm,
    toForm: (r) => ({
      name: r.name,
      email: r.email ?? '',
      webhookTarget: r.webhookTarget ?? '',
      minLevel: String(r.minLevel),
      sources: r.sources ?? '',
      isEnabled: r.isEnabled,
    }),
    toBody: (f) => ({
      name: f.name,
      email: f.email || null,
      webhookTarget: f.webhookTarget || null,
      minLevel: Number(f.minLevel),
      sources: f.sources || null,
      isEnabled: f.isEnabled,
    }),
    onSuccess: list.reload,
  })

  const columns: ColumnDef<NotificationRecipient, unknown>[] = [
    primaryColumn<NotificationRecipient>('name', t('columns.name'), {
      size: 120,
      onOpen: crud.openEdit,
    }),
    {
      accessorKey: 'email',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.email')} />,
      size: 220,
      cell: ({ row }) => <span className="text-sm">{row.original.email ?? '—'}</span>,
    },
    {
      accessorKey: 'webhookTarget',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.webhookTarget')} />,
      size: 160,
      cell: ({ row }) => <span className="text-muted-foreground text-sm">{row.original.webhookTarget ?? '—'}</span>,
    },
    {
      accessorKey: 'minLevel',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.minLevel')} />,
      size: 90,
      cell: ({ row }) => {
        const l = LEVELS[row.original.minLevel] ?? LEVELS[0]
        return <Badge variant={l.variant}>{l.label}</Badge>
      },
    },
    {
      accessorKey: 'sources',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.sources')} />,
      size: 180,
      cell: ({ row }) => <span className="text-muted-foreground text-sm">{row.original.sources || t('columns.sourcesAll')}</span>,
    },
    booleanBadgeColumn<NotificationRecipient>('isEnabled', tc('status'), { trueLabel: tc('enabled'), falseLabel: sc('disabledLabel') }),
    actionsColumn<NotificationRecipient>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
  ]

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
        enableSearch={false}
        tableKey="notification-recipients"
      />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('dialog.createTitle'), edit: t('dialog.editTitle') }}
        canSubmit={!!crud.form.name && (!!crud.form.email || !!crud.form.webhookTarget)}
      >
        <div className="space-y-1.5">
          <Label>{t('dialog.nameLabel')}</Label>
          <Input value={crud.form.name} onChange={(e) => crud.set({ name: e.target.value })} placeholder={t('dialog.namePlaceholder')} />
        </div>
        <div className="space-y-1.5">
          <Label>{t('dialog.emailLabel')}</Label>
          <Input value={crud.form.email} onChange={(e) => crud.set({ email: e.target.value })} placeholder={t('dialog.emailPlaceholder')} />
        </div>
        <div className="space-y-1.5">
          <Label>{t('dialog.webhookTargetLabel')}</Label>
          <Input value={crud.form.webhookTarget} onChange={(e) => crud.set({ webhookTarget: e.target.value })} placeholder={t('dialog.webhookTargetPlaceholder')} />
        </div>
        <p className="text-muted-foreground text-xs">{t('dialog.channelHint')}</p>
        <div className="space-y-1.5">
          <Label>{t('dialog.minLevelLabel')}</Label>
          <Select value={crud.form.minLevel} onValueChange={(v) => crud.set({ minLevel: v })}>
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="0">{t('dialog.levelOptionHint')}</SelectItem>
              <SelectItem value="1">{t('dialog.levelOptionWarning')}</SelectItem>
              <SelectItem value="2">{t('dialog.levelOptionCritical')}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>{t('dialog.sourcesLabel')}</Label>
          {knownSources.length > 0 ? (
            <div className="grid max-h-44 grid-cols-2 gap-x-3 gap-y-1.5 overflow-y-auto rounded-md border p-2.5">
              {knownSources.map((s) => {
                const selected = splitSources(crud.form.sources)
                const checked = selected.includes(s)
                return (
                  <label key={s} className="flex cursor-pointer items-center gap-2 text-sm">
                    <Checkbox
                      checked={checked}
                      onCheckedChange={(v) => {
                        const next = v === true ? [...selected, s] : selected.filter((x) => x !== s)
                        crud.set({ sources: next.join(',') })
                      }}
                    />
                    <span className="truncate" title={s}>{s}</span>
                  </label>
                )
              })}
            </div>
          ) : (
            <Input value={crud.form.sources} onChange={(e) => crud.set({ sources: e.target.value })} placeholder={t('dialog.sourcesPlaceholder')} />
          )}
          {knownSources.length > 0 && (() => {
            const custom = splitSources(crud.form.sources).filter((s) => !knownSources.includes(s))
            if (custom.length === 0) return null
            return (
              <div className="flex flex-wrap items-center gap-1.5">
                <span className="text-muted-foreground text-xs">{t('dialog.customSourcesLabel')}</span>
                {custom.map((s) => (
                  <Badge key={s} variant="secondary" className="gap-1 font-normal">
                    {s}
                    <button
                      type="button"
                      aria-label={t('dialog.removeSourceAriaLabel', { source: s })}
                      className="hover:text-destructive"
                      onClick={() => crud.set({ sources: splitSources(crud.form.sources).filter((x) => x !== s).join(',') })}
                    >
                      <X className="size-3" />
                    </button>
                  </Badge>
                ))}
              </div>
            )
          })()}
        </div>
        <div className="flex items-center gap-2">
          <Switch id="rcpt-enabled" checked={crud.form.isEnabled} onCheckedChange={(v) => crud.set({ isEnabled: v })} />
          <Label htmlFor="rcpt-enabled">{t('dialog.enabledFieldLabel')}</Label>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('delete.entityName')}
        description={(target) => <>{t('delete.descriptionTemplate', { name: target.name })}</>}
      />
    </>
  )
}
