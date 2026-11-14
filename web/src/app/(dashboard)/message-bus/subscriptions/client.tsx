'use client'

import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
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
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Combobox } from '@/components/ui/combobox'
import { type PagedResult } from '@/lib/api-client'

export interface MessageSubscription {
  id: string; topicId: string; topicName: string | null; name: string
  deliveryType: number; endpointUrl: string | null; exchangeName: string | null; routingKey: string | null
  filterRule: string | null; hasSecret: boolean; isEnabled: boolean; creationTime: string
}

export interface TopicOption { id: string; name: string }

function getDeliveryTypes(t: ReturnType<typeof useTranslations>) {
  return [{ value: 0, label: t('deliveryHttp') }, { value: 1, label: t('deliveryRabbit') }, { value: 2, label: t('deliveryBotWebhook') }]
}

interface FormState { topicId: string; name: string; deliveryType: string; endpointUrl: string; exchangeName: string; routingKey: string; filterRule: string; secret: string; hasSecret: boolean; isEnabled: boolean }
const defaultForm: FormState = { topicId: '', name: '', deliveryType: '0', endpointUrl: '', exchangeName: '', routingKey: '', filterRule: '', secret: '', hasSecret: false, isEnabled: true }

export function SubscriptionsClient({
  data, topics,
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  data: PagedResult<MessageSubscription>; topics: TopicOption[]
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('MessageBus.subscription')
  const rc = useTranslations('MessageBus.common')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const DELIVERY_TYPES = getDeliveryTypes(t)
  const list = useCrudResource<MessageSubscription>({
    endpoint: 'message-subscriptions',
    initial: data,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  const crud = useCrudForm<FormState, MessageSubscription>({
    entityName: t('entityName'),
    endpoint: 'message-subscriptions',
    defaultForm,
    toForm: (s) => ({
      topicId: s.topicId, name: s.name, deliveryType: String(s.deliveryType),
      endpointUrl: s.endpointUrl ?? '', exchangeName: s.exchangeName ?? '',
      routingKey: s.routingKey ?? '', filterRule: s.filterRule ?? '',
      secret: '', hasSecret: s.hasSecret, isEnabled: s.isEnabled,
    }),
    toBody: (form) => ({
      topicId: form.topicId, name: form.name, deliveryType: Number(form.deliveryType),
      endpointUrl: form.deliveryType === '1' ? null : form.endpointUrl,
      exchangeName: form.deliveryType === '1' ? form.exchangeName : null,
      routingKey: form.deliveryType === '1' ? (form.routingKey || null) : null,
      filterRule: form.filterRule || null,
      secret: form.deliveryType === '0' ? (form.secret || null) : null,
      isEnabled: form.isEnabled,
    }),
    onSuccess: list.reload,
  })

  const columns: ColumnDef<MessageSubscription, unknown>[] = [
    primaryColumn<MessageSubscription>('name', t('columnName'), { size: 180, onOpen: crud.openEdit }),
    {
      accessorKey: 'topicName',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('topic')} />,
      size: 140,
      cell: ({ row }) => <Badge variant="outline">{row.original.topicName ?? '—'}</Badge>,
    },
    {
      accessorKey: 'deliveryType',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnDeliveryType')} />,
      size: 120,
      meta: { filterVariant: 'select', filterOptions: DELIVERY_TYPES.map(o => ({ value: o.value, label: o.label })) },
      cell: ({ row }) => <Badge variant="outline">{DELIVERY_TYPES.find(o => o.value === row.original.deliveryType)?.label ?? rc('unknown')}</Badge>,
    },
    {
      id: 'target',
      enableSorting: false,
      accessorFn: (r) => r.deliveryType === 1 ? (r.exchangeName ?? '') : (r.endpointUrl ?? ''),
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnTarget')} />,
      size: 240,
      cell: ({ row }) => {
        const r = row.original
        const text = r.deliveryType === 1
          ? `${r.exchangeName ?? '—'}${r.routingKey ? ' / ' + r.routingKey : ''}`
          : (r.endpointUrl ?? '—')
        return <span className="text-sm text-muted-foreground truncate max-w-48 block" title={text}>{text}</span>
      },
    },
    booleanBadgeColumn<MessageSubscription>('isEnabled', tc('status'), { trueLabel: rc('enabledLabel'), falseLabel: rc('disabledLabel') }),
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('creationTime')} />,
      size: 160,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{new Date(row.original.creationTime).toLocaleDateString(locale)}</span>,
    },
    actionsColumn<MessageSubscription>({ onEdit: crud.openEdit, onDelete: crud.askDelete }),
  ]

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('countDescription', { count: list.total })} action={<CreateButton label={t('createButtonLabel')} onClick={crud.openCreate} />} />
      <DataTable columns={columns} data={list.rows} {...list.tableProps} searchPlaceholder={t('searchPlaceholder')} tableKey="message-subscriptions" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('createTitle'), edit: t('editTitle') }}
        canSubmit={!!crud.form.name && !!crud.form.topicId && (crud.form.deliveryType === '1' ? !!crud.form.exchangeName : !!crud.form.endpointUrl)}
        contentClassName="sm:max-w-[480px]"
      >
        <div className="space-y-1.5">
          <Label>{t('topicFieldLabel')}</Label>
          <Combobox
            options={topics.map(o => ({ value: o.id, label: o.name }))}
            value={crud.form.topicId}
            onChange={v => crud.set({ topicId: v })}
            placeholder={t('topicPlaceholder')}
            searchPlaceholder={t('topicSearchPlaceholder')}
            emptyText={t('topicEmptyText')}
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('nameFieldLabel')}</Label>
          <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('namePlaceholder')} />
        </div>
        <div className="space-y-1.5">
          <Label>{t('deliveryTypeFieldLabel')}</Label>
          <Select value={crud.form.deliveryType} onValueChange={v => crud.set({ deliveryType: v })}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>{DELIVERY_TYPES.map(o => <SelectItem key={o.value} value={String(o.value)}>{o.label}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        {crud.form.deliveryType === '1' ? (
          <>
            <div className="space-y-1.5">
              <Label>{t('exchangeFieldLabel')}</Label>
              <Input value={crud.form.exchangeName} onChange={e => crud.set({ exchangeName: e.target.value })} placeholder={t('exchangePlaceholder')} />
            </div>
            <div className="space-y-1.5">
              <Label>{t('routingKeyFieldLabel')}</Label>
              <Input value={crud.form.routingKey} onChange={e => crud.set({ routingKey: e.target.value })} placeholder={t('routingKeyPlaceholder')} />
            </div>
          </>
        ) : (
          <div className="space-y-1.5">
            <Label>{crud.form.deliveryType === '2' ? t('webhookFieldLabel') : t('endpointFieldLabel')}</Label>
            <Input
              value={crud.form.endpointUrl}
              onChange={e => crud.set({ endpointUrl: e.target.value })}
              placeholder={crud.form.deliveryType === '2' ? t('webhookPlaceholder') : t('endpointPlaceholder')}
            />
            {crud.form.deliveryType === '2' && (
              <p className="text-xs text-muted-foreground">{t('botWebhookHint')}</p>
            )}
          </div>
        )}
        <div className="space-y-1.5">
          <Label>{tc('filterRule')}</Label>
          <Textarea value={crud.form.filterRule} onChange={e => crud.set({ filterRule: e.target.value })} placeholder={t('filterRulePlaceholder')} className="min-h-[60px]" />
        </div>
        {crud.form.deliveryType === '0' && (
          <div className="space-y-1.5">
            <Label>{t('secretFieldLabel')}</Label>
            <Input
              type="password"
              value={crud.form.secret}
              onChange={e => crud.set({ secret: e.target.value })}
              placeholder={crud.form.hasSecret ? t('secretPlaceholderConfigured') : t('secretPlaceholderEmpty')}
            />
            <p className="text-xs text-muted-foreground">{t.rich('secretHint', { code: (chunks) => <code>{chunks}</code> })}</p>
          </div>
        )}
        <div className="flex items-center gap-2">
          <Switch id="sub-isEnabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
          <Label htmlFor="sub-isEnabled">{t('enableSwitchLabel')}</Label>
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={(target) => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
