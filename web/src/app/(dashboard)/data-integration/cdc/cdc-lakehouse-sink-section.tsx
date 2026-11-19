'use client'

import { useTranslations } from 'next-intl'
import { Combobox, type ComboboxOption } from '@/components/ui/combobox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import type { CdcStreamDto } from '@/lib/api/types'
import type { useCrudForm } from '@/lib/crud/use-crud-form'
import {
  type CdcFormState,
  normalizeCatalogType,
  normalizeLakehousePartitionStrategy,
  normalizeLakehouseTableFormat,
  normalizeLakehouseWriteMode,
} from './cdc-form-state'

export function CdcLakehouseSinkSection({
  crud,
  catalogOptions,
}: {
  crud: ReturnType<typeof useCrudForm<CdcFormState, CdcStreamDto>>
  catalogOptions: ComboboxOption[]
}) {
  const t = useTranslations('DataIntegration.cdc')

  return (
    <>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('lakehouseTableFormatFieldLabel')}</Label>
          <Select
            value={crud.form.lakehouseTableFormat ?? '0'}
            onValueChange={v => crud.set({ lakehouseTableFormat: normalizeLakehouseTableFormat(Number(v)) })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="0">{t('lakehouseFormatSwapMid')}</SelectItem>
              <SelectItem value="1">{t('lakehouseFormatDeltaLake')}</SelectItem>
              <SelectItem value="2">{t('lakehouseFormatApacheIceberg')}</SelectItem>
            </SelectContent>
          </Select>
          <p className="text-xs text-muted-foreground">{t('lakehouseTableFormatHint')}</p>
        </div>
        <div className="space-y-1.5">
          <Label>{t('lakehouseCompactMinFilesFieldLabel')}</Label>
          <Input
            type="number"
            min={2}
            max={100}
            value={crud.form.lakehouseCompactMinFiles}
            onChange={e => crud.set({ lakehouseCompactMinFiles: e.target.value })}
            placeholder="5"
            className="font-mono"
          />
          <p className="text-xs text-muted-foreground">{t('lakehouseCompactMinFilesHint')}</p>
        </div>
      </div>

      {crud.form.lakehouseTableFormat === '2' && (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{t('lakehousePartitionStrategyFieldLabel')}</Label>
              <Select
                value={crud.form.lakehousePartitionStrategy ?? '0'}
                onValueChange={v => crud.set({
                  lakehousePartitionStrategy: normalizeLakehousePartitionStrategy(Number(v)),
                })}
              >
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="0">{t('lakehousePartitionNone')}</SelectItem>
                  <SelectItem value="1">{t('lakehousePartitionDailyUtc')}</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">{t('lakehousePartitionStrategyHint')}</p>
            </div>
            <div className="space-y-1.5">
              <Label>{t('lakehouseWriteModeFieldLabel')}</Label>
              <Select
                value={crud.form.lakehouseWriteMode ?? '0'}
                onValueChange={v => crud.set({
                  lakehouseWriteMode: normalizeLakehouseWriteMode(Number(v)),
                })}
              >
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="0">{t('lakehouseWriteModeAppend')}</SelectItem>
                  <SelectItem value="1">{t('lakehouseWriteModeMorUpsert')}</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">{t('lakehouseWriteModeHint')}</p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{t('lakehouseWriteBranchFieldLabel')}</Label>
              <Input
                value={crud.form.lakehouseWriteBranch}
                onChange={e => crud.set({ lakehouseWriteBranch: e.target.value })}
                placeholder="staging"
                className="font-mono"
              />
              <p className="text-xs text-muted-foreground">{t('lakehouseWriteBranchHint')}</p>
            </div>
            <div className="space-y-1.5">
              <Label>{t('lakehouseMaintenanceCronFieldLabel')}</Label>
              <Input
                value={crud.form.lakehouseMaintenanceCron}
                onChange={e => crud.set({ lakehouseMaintenanceCron: e.target.value })}
                placeholder="0 2 * * *"
                className="font-mono"
              />
              <p className="text-xs text-muted-foreground">{t('lakehouseMaintenanceCronHint')}</p>
            </div>
          </div>

          {crud.form.lakehouseMaintenanceCron.trim().length > 0 && (
            <div className="grid grid-cols-3 items-end gap-3 rounded-md border bg-muted/20 p-3">
              <div className="space-y-1.5">
                <Label>{t('icebergRetainLastLabel')}</Label>
                <Input
                  type="number"
                  min={1}
                  max={100}
                  value={crud.form.lakehouseExpireRetainLast}
                  onChange={e => crud.set({ lakehouseExpireRetainLast: e.target.value })}
                  placeholder="5"
                  className="font-mono"
                />
              </div>
              <div className="space-y-1.5">
                <Label>{t('lakehouseExpireOlderThanFieldLabel')}</Label>
                <Input
                  type="number"
                  min={0}
                  max={525600}
                  value={crud.form.lakehouseExpireOlderThanMinutes}
                  onChange={e => crud.set({ lakehouseExpireOlderThanMinutes: e.target.value })}
                  placeholder="1440"
                  className="font-mono"
                />
              </div>
              <label className="flex h-9 items-center gap-2 text-xs text-muted-foreground">
                <Switch
                  checked={crud.form.lakehouseDeleteOrphanedFiles}
                  onCheckedChange={checked => crud.set({ lakehouseDeleteOrphanedFiles: checked })}
                />
                {t('icebergDeleteOrphansLabel')}
              </label>
            </div>
          )}
        </div>
      )}

      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('catalogTypeFieldLabel')}</Label>
            <Select
              value={crud.form.catalogType ?? '0'}
              onValueChange={v => crud.set({ catalogType: normalizeCatalogType(Number(v)) })}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="0">{t('catalogTypeNone')}</SelectItem>
                <SelectItem value="1">{t('catalogTypeUnity')}</SelectItem>
                <SelectItem value="2">{t('catalogTypeIceberg')}</SelectItem>
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">{t('catalogTypeHint')}</p>
          </div>
          {crud.form.catalogType !== '0' && (
            <div className="space-y-1.5">
              <Label>{t('catalogSinkFieldLabel')}</Label>
              <Combobox
                options={catalogOptions}
                value={crud.form.catalogSinkId}
                onChange={v => crud.set({ catalogSinkId: v })}
                placeholder={t('catalogSinkPlaceholder')}
                searchPlaceholder={t('catalogSinkSearchPlaceholder')}
                emptyText={t('catalogSinkEmptyText')}
              />
            </div>
          )}
        </div>
        {crud.form.catalogType !== '0' && (
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{t('catalogNamespaceFieldLabel')}</Label>
              <Input
                value={crud.form.catalogNamespace}
                onChange={e => crud.set({ catalogNamespace: e.target.value })}
                placeholder={crud.form.catalogType === '1' ? 'analytics.swapmid_cdc' : 'swapmid_cdc'}
                className="font-mono"
              />
            </div>
            <div className="space-y-1.5">
              <Label>{t('catalogTableFieldLabel')}</Label>
              <Input
                value={crud.form.catalogTableName}
                onChange={e => crud.set({ catalogTableName: e.target.value })}
                placeholder="orders"
                className="font-mono"
              />
            </div>
          </div>
        )}
      </div>
    </>
  )
}
