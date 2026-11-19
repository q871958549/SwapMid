'use client'

import { useTranslations } from 'next-intl'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { Combobox, type ComboboxOption } from '@/components/ui/combobox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import type { CdcStreamDto } from '@/lib/api/types'
import type { useCrudForm } from '@/lib/crud/use-crud-form'
import {
  type CdcFormState,
  type CdcPipelineOption,
  type CdcSourceOption,
  type CdcTopicOption,
  DEFAULT_CDC_TOPIC,
  detectCdcSourceTypeFromSource,
  getSinkTypeTag,
  isCdcFormValid,
  normalizeCatalogType,
  normalizeDeletePolicy,
  normalizeDriftPolicy,
  normalizeLakehouseTableFormat,
  normalizeSourceType,
  normalizeTargetMode,
} from './cdc-form-state'
import { CdcContractSection } from './cdc-contract-section'
import { CdcRelationalMirrorSection } from './cdc-relational-mirror-section'

export function CdcFormDialog({
  crud,
  sources,
  relationalTargets,
  sinks = [],
  catalogSources = [],
  pipelines,
  topics,
}: {
  crud: ReturnType<typeof useCrudForm<CdcFormState, CdcStreamDto>>
  sources: CdcSourceOption[]
  relationalTargets?: CdcSourceOption[]
  sinks?: CdcSourceOption[]
  catalogSources?: CdcSourceOption[]
  pipelines: CdcPipelineOption[]
  topics: CdcTopicOption[]
}) {
  const t = useTranslations('DataIntegration.cdc')
  const rc = useTranslations('DataIntegration.common')
  const tc = useTranslations('Common')

  const sourceOptions: ComboboxOption[] = sources.map(s => ({ value: s.id, label: s.name }))
  const relOptions: ComboboxOption[] = (relationalTargets && relationalTargets.length > 0 ? relationalTargets : sources)
    .map(s => ({ value: s.id, label: s.name }))
  const sinkOptions: ComboboxOption[] = sinks.map(s => ({
    value: s.id,
    label: `${s.name} (${getSinkTypeTag(s.type)})`,
  }))
  const catalogOptions: ComboboxOption[] = catalogSources.map(s => ({ value: s.id, label: s.name }))
  const pipelineOptions: ComboboxOption[] = pipelines.map(p => ({ value: p.id, label: p.name }))
  const topicNames = Array.from(new Set([DEFAULT_CDC_TOPIC, ...topics.map(item => item.name).filter(Boolean)]))
  const topicOptions: ComboboxOption[] = topicNames.map(name => ({ value: name, label: name }))
  const selectedSink = sinks.find(s => s.id === crud.form.targetSinkId)
  const sinkDestPlaceholder = selectedSink?.type === 3
    ? t('sinkDestKafkaPlaceholder')
    : selectedSink?.type === 4
      ? t('sinkDestClickHousePlaceholder')
      : t('sinkDestObjectStoragePlaceholder')

  return (
    <CrudFormDialog
      crud={crud}
      title={{ create: t('createTitle'), edit: t('editTitle') }}
      contentClassName="sm:max-w-2xl"
      canSubmit={isCdcFormValid(crud.form)}
    >
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('nameFieldLabel')}</Label>
          <Input
            value={crud.form.name}
            onChange={e => crud.set({ name: e.target.value })}
            placeholder={t('namePlaceholder')}
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('sourceFieldLabel')}</Label>
          <Combobox
            options={sourceOptions}
            value={crud.form.sourceId}
            onChange={v => crud.set({ sourceId: v, sourceType: detectCdcSourceTypeFromSource(v, sources) })}
            placeholder={t('sourcePlaceholder')}
            searchPlaceholder={t('sourceSearchPlaceholder')}
            emptyText={t('sourceEmptyText')}
          />
        </div>
      </div>

      <div className="space-y-1.5">
        <Label>{t('sourceTypeFieldLabel')}</Label>
        <Select
          value={crud.form.sourceType ?? '0'}
          onValueChange={v => crud.set({ sourceType: normalizeSourceType(Number(v)) })}
        >
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="0">{t('sourceTypePostgres')}</SelectItem>
            <SelectItem value="1">{t('sourceTypeSqlServerCt')}</SelectItem>
            <SelectItem value="2">{t('sourceTypeMySqlBinlog')}</SelectItem>
          </SelectContent>
        </Select>
        {crud.form.sourceType === '1' && (
          <p className="text-xs text-muted-foreground">{t('sourceTypeSqlServerHint')}</p>
        )}
        {crud.form.sourceType === '2' && (
          <p className="text-xs text-muted-foreground">{t('sourceTypeMySqlBinlogHint')}</p>
        )}
      </div>

      {crud.form.sourceType === '0' && (
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('publicationFieldLabel')}</Label>
            <Input
              value={crud.form.publicationName}
              onChange={e => crud.set({ publicationName: e.target.value })}
              placeholder="swapmid_cdc_pub"
              className="font-mono"
            />
          </div>
          <div className="space-y-1.5">
            <Label>{t('slotFieldLabel')}</Label>
            <Input
              value={crud.form.slotName}
              onChange={e => crud.set({ slotName: e.target.value })}
              placeholder="swapmid_cdc_slot"
              className="font-mono"
            />
          </div>
        </div>
      )}

      <div className="space-y-1.5">
        <Label>{t('monitoredTablesFieldLabel')}</Label>
        <Input
          value={crud.form.monitoredTables}
          onChange={e => crud.set({ monitoredTables: e.target.value })}
          placeholder={
            crud.form.sourceType === '2'
              ? 'appdb.orders'
              : (crud.form.sourceType === '1' ? 'dbo.orders' : t('monitoredTablesPlaceholder'))
          }
          className="font-mono"
        />
        <p className="text-xs text-muted-foreground">
          {crud.form.sourceType === '2'
            ? t('monitoredTablesMySqlHint')
            : (crud.form.sourceType === '1' ? t('monitoredTablesSqlServerHint') : t('monitoredTablesHint'))}
        </p>
      </div>

      {crud.form.sourceType === '0' && (
        <div className="flex items-center justify-between gap-4 rounded-md border bg-muted/30 px-3 py-2">
          <div className="space-y-0.5">
            <Label htmlFor="cdc-auto-create">{t('autoCreateSwitchLabel')}</Label>
            <p className="text-xs text-muted-foreground">{t('autoCreateHint')}</p>
          </div>
          <Switch
            id="cdc-auto-create"
            checked={crud.form.autoCreatePublicationAndSlot}
            onCheckedChange={v => crud.set({ autoCreatePublicationAndSlot: v })}
          />
        </div>
      )}

      <div className="flex items-center justify-between gap-4 rounded-md border bg-muted/30 px-3 py-2">
        <div className="space-y-0.5">
          <Label htmlFor="cdc-initial-snapshot">{t('initialSnapshotSwitchLabel')}</Label>
          <p className="text-xs text-muted-foreground">{t('initialSnapshotHint')}</p>
        </div>
        <Switch
          id="cdc-initial-snapshot"
          checked={crud.form.initialSnapshotEnabled}
          onCheckedChange={v => crud.set({ initialSnapshotEnabled: v })}
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('pollIntervalFieldLabel')}</Label>
          <Input
            type="number"
            min={5}
            max={3600}
            value={crud.form.pollIntervalSeconds}
            onChange={e => crud.set({ pollIntervalSeconds: e.target.value })}
            className="font-mono"
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('maxBatchSizeFieldLabel')}</Label>
          <Input
            type="number"
            min={1}
            max={5000}
            value={crud.form.maxBatchSize}
            onChange={e => crud.set({ maxBatchSize: e.target.value })}
            className="font-mono"
          />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('targetModeFieldLabel')}</Label>
          <Select
            value={crud.form.targetMode}
            onValueChange={v => crud.set({ targetMode: normalizeTargetMode(Number(v)) })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="0">{t('targetModeTopic')}</SelectItem>
              <SelectItem value="1">{t('targetModePipeline')}</SelectItem>
              <SelectItem value="2">{t('targetModeExternalSink')}</SelectItem>
              <SelectItem value="3">{t('targetModeRelationalTable')}</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {crud.form.targetMode === '0' ? (
          <div className="space-y-1.5">
            <Label>{t('targetTopicFieldLabel')}</Label>
            <Combobox
              options={topicOptions}
              value={crud.form.targetTopicName}
              onChange={v => crud.set({ targetTopicName: v })}
              placeholder={DEFAULT_CDC_TOPIC}
              searchPlaceholder={t('topicSearchPlaceholder')}
              emptyText={t('topicEmptyText')}
            />
          </div>
        ) : crud.form.targetMode === '1' ? (
          <div className="space-y-1.5">
            <Label>{t('targetPipelineFieldLabel')}</Label>
            <Combobox
              options={pipelineOptions}
              value={crud.form.targetPipelineId}
              onChange={v => crud.set({ targetPipelineId: v })}
              placeholder={t('pipelinePlaceholder')}
              searchPlaceholder={t('pipelineSearchPlaceholder')}
              emptyText={t('pipelineEmptyText')}
            />
          </div>
        ) : crud.form.targetMode === '3' ? (
          <div className="space-y-1.5">
            <Label>{t('targetRelationalDbFieldLabel')}</Label>
            <Combobox
              options={relOptions}
              value={crud.form.targetSinkId}
              onChange={v => crud.set({ targetSinkId: v })}
              placeholder={t('relationalDbPlaceholder')}
              searchPlaceholder={t('relationalDbSearchPlaceholder')}
              emptyText={t('relationalDbEmptyText')}
            />
          </div>
        ) : (
          <div className="space-y-1.5">
            <Label>{t('targetSinkFieldLabel')}</Label>
            <Combobox
              options={sinkOptions}
              value={crud.form.targetSinkId}
              onChange={v => crud.set({ targetSinkId: v })}
              placeholder={t('sinkPlaceholder')}
              searchPlaceholder={t('sinkSearchPlaceholder')}
              emptyText={t('sinkEmptyText')}
            />
          </div>
        )}
      </div>

      {(crud.form.targetMode === '2' || crud.form.targetMode === '3') && (
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label>
              {crud.form.targetMode === '3'
                ? t('targetRelationalTableFieldLabel')
                : t('targetSinkDestFieldLabel')}
            </Label>
            <Input
              value={crud.form.targetSinkDestination}
              onChange={e => crud.set({ targetSinkDestination: e.target.value })}
              placeholder={crud.form.targetMode === '3' ? 'public.etl_demo_orders_mirror' : sinkDestPlaceholder}
              className="font-mono"
            />
            <p className="text-xs text-muted-foreground">
              {crud.form.targetMode === '3' ? t('targetRelationalTableHint') : t('targetSinkDestHint')}
            </p>
          </div>
          <div className="space-y-1.5">
            <Label>
              {crud.form.targetMode === '3'
                ? t('targetSinkKeyColumnsRequiredFieldLabel')
                : t('targetSinkKeyColumnsFieldLabel')}
            </Label>
            <Input
              value={crud.form.targetSinkKeyColumns}
              onChange={e => crud.set({ targetSinkKeyColumns: e.target.value })}
              placeholder="order_id"
              className="font-mono"
            />
            <p className="text-xs text-muted-foreground">{t('targetSinkKeyColumnsHint')}</p>
          </div>
          {crud.form.targetMode === '2' && selectedSink?.type === 5 && (
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
          )}
          {crud.form.targetMode === '2' && selectedSink?.type === 5 && (
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
          )}
        </div>
      )}

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('schemaDriftPolicyFieldLabel')}</Label>
          <Select
            value={crud.form.schemaDriftPolicy ?? '0'}
            onValueChange={v => crud.set({ schemaDriftPolicy: normalizeDriftPolicy(Number(v)) })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="0">{t('schemaDriftPolicyIgnore')}</SelectItem>
              <SelectItem value="1">{t('schemaDriftPolicyWarn')}</SelectItem>
              <SelectItem value="2">{t('schemaDriftPolicyPauseAndAlert')}</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {crud.form.targetMode === '3' && (
          <div className="space-y-1.5">
            <Label>{t('targetDeletePolicyFieldLabel')}</Label>
            <Select
              value={crud.form.targetDeletePolicy ?? '0'}
              onValueChange={v => crud.set({ targetDeletePolicy: normalizeDeletePolicy(Number(v)) })}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="0">{t('deletePolicyDeleteRow')}</SelectItem>
                <SelectItem value="1">{t('deletePolicyIgnore')}</SelectItem>
                <SelectItem value="2">{t('deletePolicySoftDelete')}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        )}
      </div>

      {crud.form.targetMode === '3' && <CdcRelationalMirrorSection crud={crud} />}

      <CdcContractSection crud={crud} />

      <div className="space-y-1.5">
        <Label>{tc('description')}</Label>
        <Textarea
          value={crud.form.description}
          onChange={e => crud.set({ description: e.target.value })}
          placeholder={rc('descriptionPlaceholder')}
          className="min-h-[60px]"
        />
      </div>
    </CrudFormDialog>
  )
}
