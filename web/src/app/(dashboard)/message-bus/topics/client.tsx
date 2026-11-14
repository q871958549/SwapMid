'use client'

import { useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { Send } from 'lucide-react'
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
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { type PagedResult } from '@/lib/api-client'
import { toast } from '@/components/ui/use-toast'

const notify = {
  success: (title: string) => toast({ variant: 'success', title }),
  error: (title: string) => toast({ variant: 'destructive', title }),
}

export interface MessageTopic {
  id: string; name: string; description: string | null
  type: number; retryCount: number; retryIntervalSeconds: number
  isEnabled: boolean; creationTime: string
}

// 模块级常量含 UI 文案，改为由持有 useTranslations 的组件调用的工厂函数（纯函数不能直接调 hook）。
function getTopicTypes(t: ReturnType<typeof useTranslations>) {
  return [{ value: 0, label: t('typeQueue') }, { value: 1, label: t('typeBroadcast') }]
}

interface FormState { name: string; description: string; type: string; retryCount: string; retryIntervalSeconds: string; isEnabled: boolean }
const defaultForm: FormState = { name: '', description: '', type: '0', retryCount: '3', retryIntervalSeconds: '30', isEnabled: true }

export function TopicsClient({
  data,
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  data: PagedResult<MessageTopic>
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('MessageBus.topic')
  const rc = useTranslations('MessageBus.common')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const TOPIC_TYPES = getTopicTypes(t)
  // 服务端分页：翻页/搜索下推后端（/message-topics 支持 skipCount/maxResultCount/Filter）。
  // urlSync 把 page/size/q 镜像到 URL，配合 page.tsx 从 searchParams 解析初值下传，切走再切回/刷新可恢复。
  const list = useCrudResource<MessageTopic>({
    endpoint: 'message-topics',
    initial: data,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  // 标准新建/编辑/删除：共享件接管「写操作反馈 5 点」+ POST/PUT/DELETE /api/proxy/message-topics[/{id}]
  const crud = useCrudForm<FormState, MessageTopic>({
    entityName: t('entityName'),
    endpoint: 'message-topics',
    defaultForm,
    toForm: (row) => ({
      name: row.name, description: row.description ?? '', type: String(row.type),
      retryCount: String(row.retryCount), retryIntervalSeconds: String(row.retryIntervalSeconds), isEnabled: row.isEnabled,
    }),
    toBody: (form) => ({
      name: form.name, description: form.description || null, type: Number(form.type),
      retryCount: Number(form.retryCount), retryIntervalSeconds: Number(form.retryIntervalSeconds), isEnabled: form.isEnabled,
    }),
    onSuccess: list.reload,
  })

  // 发布消息：非标准 CRUD 的特殊操作，保留自定义对话框
  const [publishTarget, setPublishTarget] = useState<MessageTopic | null>(null)
  const [pubForm, setPubForm] = useState({ subject: '', payload: '{}' })
  const [publishing, setPublishing] = useState(false)

  // 发布消息到主题：按订阅(含 FilterRule 过滤)异步 HTTP 投递,投递结果见「消息记录」
  async function handlePublish() {
    if (!publishTarget || !pubForm.subject.trim()) return
    setPublishing(true)
    try {
      const res = await fetch('/api/proxy/message-records/publish', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ topicId: publishTarget.id, subject: pubForm.subject.trim(), payload: pubForm.payload }),
      })
      if (!res.ok) throw new Error(await res.text())
      notify.success(t('publishSuccessDesc', { name: publishTarget.name }))
      setPublishTarget(null)
    } catch (e) {
      notify.error(e instanceof Error ? e.message : t('publishFailedFallback'))
    } finally {
      setPublishing(false)
    }
  }

  const columns: ColumnDef<MessageTopic, unknown>[] = [
    primaryColumn<MessageTopic>('name', t('columnName'), { size: 200, onOpen: crud.openEdit }),
    {
      accessorKey: 'type',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('type')} />,
      size: 140,
      meta: { filterVariant: 'select', filterOptions: TOPIC_TYPES.map(o => ({ value: o.value, label: o.label })) },
      cell: ({ row }) => <Badge variant="outline">{TOPIC_TYPES.find(o => o.value === row.original.type)?.label ?? rc('unknown')}</Badge>,
    },
    {
      accessorKey: 'retryCount',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('retryCount')} />,
      size: 100,
      meta: { numeric: true },
      cell: ({ row }) => <span className="text-sm tabular-nums text-muted-foreground">{rc('retryTimes', { count: row.original.retryCount })}</span>,
    },
    booleanBadgeColumn<MessageTopic>('isEnabled', tc('status'), { trueLabel: rc('enabledLabel'), falseLabel: rc('disabledLabel') }),
    {
      accessorKey: 'creationTime',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('creationTime')} />,
      size: 160,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{new Date(row.original.creationTime).toLocaleDateString(locale)}</span>,
    },
    actionsColumn<MessageTopic>({
      onEdit: crud.openEdit,
      onDelete: crud.askDelete,
      extraItems: (row) => (
        <DropdownMenuItem onClick={() => { setPubForm({ subject: '', payload: '{}' }); setPublishTarget(row) }}><Send className="mr-2 size-3.5" />{t('publishMenuItem')}</DropdownMenuItem>
      ),
    }),
  ]

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('countDescription', { count: list.total })} action={<CreateButton label={t('createButtonLabel')} onClick={crud.openCreate} />} />
      <DataTable columns={columns} data={list.rows} {...list.tableProps} searchPlaceholder={t('searchPlaceholder')} tableKey="message-topics" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('createTitle'), edit: t('editTitle') }}
        canSubmit={!!crud.form.name}
        contentClassName="sm:max-w-[460px]"
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('nameFieldLabel')}</Label>
            <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('namePlaceholder')} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('typeFieldLabel')}</Label>
            <Select value={crud.form.type} onValueChange={v => crud.set({ type: v })}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>{TOPIC_TYPES.map(o => <SelectItem key={o.value} value={String(o.value)}>{o.label}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('retryCountFieldLabel')}</Label>
            <Input type="number" value={crud.form.retryCount} onChange={e => crud.set({ retryCount: e.target.value })} min={0} />
          </div>
          <div className="space-y-1.5">
            <Label>{t('retryIntervalFieldLabel')}</Label>
            <Input type="number" value={crud.form.retryIntervalSeconds} onChange={e => crud.set({ retryIntervalSeconds: e.target.value })} min={1} />
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>{tc('description')}</Label>
          <Textarea value={crud.form.description} onChange={e => crud.set({ description: e.target.value })} placeholder={t('descriptionPlaceholder')} className="min-h-[60px]" />
        </div>
        <div className="flex items-center gap-2">
          <Switch id="topic-isEnabled" checked={crud.form.isEnabled} onCheckedChange={v => crud.set({ isEnabled: v })} />
          <Label htmlFor="topic-isEnabled">{t('enableSwitchLabel')}</Label>
        </div>
      </CrudFormDialog>

      <Dialog open={!!publishTarget} onOpenChange={o => !o && setPublishTarget(null)}>
        <DialogContent className="sm:max-w-[480px]" aria-describedby={undefined}>
          <DialogHeader><DialogTitle>{t('publishDialogTitle', { name: publishTarget?.name ?? '' })}</DialogTitle></DialogHeader>
          <div className="grid gap-3 py-2">
            <div className="space-y-1.5">
              <Label>{t('publishSubjectLabel')}</Label>
              <Input value={pubForm.subject} onChange={e => setPubForm(p => ({ ...p, subject: e.target.value }))} placeholder={t('publishSubjectPlaceholder')} />
            </div>
            <div className="space-y-1.5">
              <Label>{t('publishPayloadLabel')}</Label>
              <Textarea value={pubForm.payload} onChange={e => setPubForm(p => ({ ...p, payload: e.target.value }))} className="font-mono text-xs min-h-[100px]" placeholder='{ "orderId": 123 }' />
            </div>
            <p className="text-muted-foreground text-xs">{t('publishHint')}</p>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPublishTarget(null)}>{tc('cancel')}</Button>
            <Button onClick={handlePublish} disabled={publishing || !pubForm.subject.trim()}>{publishing ? t('publishing') : t('publishButton')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={(target) => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
