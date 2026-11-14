'use client'

import { useState, useEffect, useCallback, useRef } from 'react'
import { useTranslations } from 'next-intl'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { IconButton } from '@/components/ui/icon-button'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Badge } from '@/components/ui/badge'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { Pencil, Trash2 } from 'lucide-react'
import { type DataDictionaryDto } from './client'
import { defaultItemValue, nextItemSortOrder } from './dict-item-order'
import { notifyDictionaryChanged } from '@/lib/dict/dictionary-events'
import { useHasPermission } from '@/components/providers/permissions-provider'

export interface DataDictionaryItemDto {
  id: string
  dictionaryId: string
  label: string
  value: string
  sortOrder: number
  isEnabled: boolean
  description: string | null
}

interface ItemFormState {
  label: string
  value: string
  sortOrder: string
  isEnabled: boolean
  description: string
}

const defaultForm: ItemFormState = { label: '', value: '', sortOrder: '1', isEnabled: true, description: '' }

export function DictItemsDialog({
  dict,
  open,
  onOpenChange,
  maxValueLength,
}: {
  dict: DataDictionaryDto
  open: boolean
  onOpenChange: (open: boolean) => void
  maxValueLength?: number
}) {
  const t = useTranslations('System.dataDictionaries.itemsDialog')
  const tc = useTranslations('Common')
  const canRead = useHasPermission('SwapMid.SystemManagement.DataDictionaries')
  const updateAllowed = useHasPermission('SwapMid.SystemManagement.DataDictionaries.Update')
  const canMaintain = canRead && updateAllowed
  const [items, setItems] = useState<DataDictionaryItemDto[]>([])
  const valueTouchedRef = useRef(false)

  const reload = useCallback(async () => {
    try {
      const res = await fetch(`/api/proxy/data-dictionaries/${dict.id}/items`)
      if (!res.ok) throw new Error(await res.text())
      const data = await res.json()
      setItems(data.items ?? [])
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('loadFailedTitle'), description: getApiErrorMessage(e, tc('tryAgainLater')) })
    }
  }, [dict.id, t, tc])

  useEffect(() => {
    if (open && canRead) void Promise.resolve().then(reload)
  }, [open, canRead, reload])

  const crud = useCrudForm<ItemFormState, DataDictionaryItemDto>({
    entityName: t('entityName'),
    endpoint: 'data-dictionaries/items',
    defaultForm,
    getName: (i) => i.label,
    toForm: (i) => ({
      label: i.label,
      value: i.value,
      sortOrder: String(i.sortOrder),
      isEnabled: i.isEnabled,
      description: i.description ?? '',
    }),
    toBody: (form) => ({
      dictionaryId: dict.id,
      label: form.label.trim(),
      value: defaultItemValue(form.label, form.value),
      sortOrder: Number.parseInt(form.sortOrder, 10) || nextItemSortOrder(items),
      isEnabled: form.isEnabled,
      description: form.description || null,
    }),
    onSuccess: async () => {
      notifyDictionaryChanged(dict.code)
      await reload()
    },
    beforeCreate: async () => {
      valueTouchedRef.current = false
      return { sortOrder: String(nextItemSortOrder(items)) }
    },
    beforeEdit: () => {
      valueTouchedRef.current = true
    },
  })

  const resolvedValue = defaultItemValue(crud.form.label, crud.form.value)

  if (!canRead) return null
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[680px]" aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>{t('titleTemplate', { name: dict.name })}</DialogTitle>
        </DialogHeader>

        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">
            {t('dictCodeLabel')} <code className="bg-muted px-1.5 py-0.5 rounded">{dict.code}</code>
          </span>
          {canMaintain && <CreateButton label={t('createButton')} onClick={crud.openCreate} />}
        </div>

        <div className="max-h-[50vh] overflow-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 sticky top-0 z-10">
              <tr className="text-left">
                <th className="px-3 py-2 font-medium whitespace-nowrap">{t('columns.label')}</th>
                <th className="px-3 py-2 font-medium whitespace-nowrap">{t('columns.value')}</th>
                <th className="px-3 py-2 font-medium w-16 whitespace-nowrap">{t('columns.sortOrder')}</th>
                <th className="px-3 py-2 font-medium w-20 whitespace-nowrap">{tc('status')}</th>
                <th className="px-3 py-2 font-medium w-24 text-right whitespace-nowrap">{tc('actions')}</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-3 py-8 text-center text-muted-foreground">
                    {t('emptyState')}
                  </td>
                </tr>
              )}
              {items.map((it) => (
                <tr key={it.id} className="border-t">
                  <td className="px-3 py-2 whitespace-nowrap">{it.label}</td>
                  <td className="px-3 py-2">
                    <code className="bg-muted px-1.5 py-0.5 rounded text-xs">{it.value}</code>
                  </td>
                  <td className="px-3 py-2 tabular-nums text-muted-foreground">{it.sortOrder}</td>
                  <td className="px-3 py-2">
                    <Badge variant={it.isEnabled ? 'success' : 'secondary'}>
                      {it.isEnabled ? tc('enabled') : t('disabledLabel')}
                    </Badge>
                  </td>
                  <td className="px-3 py-2">
                    <div className="flex justify-end gap-1 shrink-0">
                      {canMaintain && (
                        <IconButton label={tc('edit')} className="size-8" onClick={() => crud.openEdit(it)}>
                          <Pencil className="size-4" />
                        </IconButton>
                      )}
                      {canMaintain && (
                        <IconButton label={tc('delete')} className="size-8 text-destructive" onClick={() => crud.askDelete(it)}>
                          <Trash2 className="size-4" />
                        </IconButton>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <CrudFormDialog
          crud={crud}
          title={{ create: t('createButton'), edit: t('editTitle') }}
          canSubmit={canMaintain && !!crud.form.label.trim() && !!resolvedValue && (!maxValueLength || resolvedValue.length <= maxValueLength)}
          contentClassName="sm:max-w-[460px]"
        >
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="dict-item-label">{t('dialog.labelLabel')}</Label>
              <Input
                id="dict-item-label"
                value={crud.form.label}
                onChange={(e) => {
                  const label = e.target.value
                  crud.set(
                    crud.mode === 'create' && !valueTouchedRef.current
                      ? { label, value: label }
                      : { label },
                  )
                }}
                placeholder={t('dialog.labelPlaceholder')}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="dict-item-value">{t('dialog.valueLabel')}</Label>
              <Input
                id="dict-item-value"
                value={crud.form.value}
                maxLength={maxValueLength}
                onChange={(e) => {
                  valueTouchedRef.current = true
                  crud.set({ value: e.target.value })
                }}
                placeholder={t('dialog.valuePlaceholder')}
                className="font-mono"
              />
            </div>
          </div>
          <p className="text-xs text-muted-foreground">{t('dialog.valueHint')}</p>
          {maxValueLength && resolvedValue.length > maxValueLength && (
            <p role="alert" className="text-xs text-destructive">{t('dialog.valueTooLong', { max: maxValueLength })}</p>
          )}
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="dict-item-sort">{t('dialog.sortOrderLabel')}</Label>
              <Input
                id="dict-item-sort"
                type="number"
                value={crud.form.sortOrder}
                onChange={(e) => crud.set({ sortOrder: e.target.value })}
              />
              <p className="text-xs text-muted-foreground">{t('dialog.sortOrderHint')}</p>
            </div>
            <div className="flex items-center gap-2 pt-5">
              <Switch id="item-enabled" checked={crud.form.isEnabled} onCheckedChange={(v) => crud.set({ isEnabled: v })} />
              <Label htmlFor="item-enabled">{tc('enabled')}</Label>
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="dict-item-desc">{tc('description')}</Label>
            <Input
              id="dict-item-desc"
              value={crud.form.description}
              onChange={(e) => crud.set({ description: e.target.value })}
              placeholder={t('dialog.descriptionPlaceholder')}
            />
          </div>
        </CrudFormDialog>

        <DeleteConfirm
          crud={crud}
          entityName={t('entityName')}
          description={(target) => <>{t('delete.descriptionTemplate', { name: target.label })}</>}
        />
      </DialogContent>
    </Dialog>
  )
}
