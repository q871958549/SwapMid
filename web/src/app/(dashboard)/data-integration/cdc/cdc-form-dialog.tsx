'use client'

import { useTranslations } from 'next-intl'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { Combobox, type ComboboxOption } from '@/components/ui/combobox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import type { CdcStreamDto, DataPipelineDto, DataSourceDto, MessageTopicDto } from '@/lib/api/types'
import type { useCrudForm } from '@/lib/crud/use-crud-form'

export type CdcSourceOption = Pick<DataSourceDto, 'id' | 'name' | 'type' | 'config'>
export type CdcPipelineOption = Pick<DataPipelineDto, 'id' | 'name'>
export type CdcTopicOption = Pick<MessageTopicDto, 'id' | 'name'>

export interface CdcFormState {
  name: string
  description: string
  sourceId: string
  publicationName: string
  slotName: string
  monitoredTables: string
  autoCreatePublicationAndSlot: boolean
  pollIntervalSeconds: string
  maxBatchSize: string
  targetMode: '0' | '1'
  targetTopicName: string
  targetPipelineId: string
}

export const DEFAULT_CDC_TOPIC = 'integration.cdc.events'

export const defaultCdcForm: CdcFormState = {
  name: '',
  description: '',
  sourceId: '',
  publicationName: 'swapmid_cdc_pub',
  slotName: 'swapmid_cdc_slot',
  monitoredTables: '',
  autoCreatePublicationAndSlot: true,
  pollIntervalSeconds: '15',
  maxBatchSize: '500',
  targetMode: '0',
  targetTopicName: DEFAULT_CDC_TOPIC,
  targetPipelineId: '',
}

export function toCdcForm(item: CdcStreamDto): CdcFormState {
  return {
    name: item.name,
    description: item.description ?? '',
    sourceId: item.sourceId,
    publicationName: item.publicationName,
    slotName: item.slotName,
    monitoredTables: item.monitoredTables ?? '',
    autoCreatePublicationAndSlot: item.autoCreatePublicationAndSlot,
    pollIntervalSeconds: String(item.pollIntervalSeconds || 15),
    maxBatchSize: String(item.maxBatchSize || 500),
    targetMode: Number(item.targetMode) === 1 ? '1' : '0',
    targetTopicName: item.targetTopicName ?? DEFAULT_CDC_TOPIC,
    targetPipelineId: item.targetPipelineId ?? '',
  }
}

export function toCdcBody(form: CdcFormState) {
  const poll = Number.parseInt(form.pollIntervalSeconds, 10)
  const batch = Number.parseInt(form.maxBatchSize, 10)
  const mode = form.targetMode === '1' ? 1 : 0
  return {
    name: form.name.trim(),
    description: form.description.trim() || null,
    sourceId: form.sourceId,
    publicationName: form.publicationName.trim(),
    slotName: form.slotName.trim(),
    monitoredTables: form.monitoredTables.trim() || null,
    autoCreatePublicationAndSlot: form.autoCreatePublicationAndSlot,
    pollIntervalSeconds: Number.isFinite(poll) ? poll : 15,
    maxBatchSize: Number.isFinite(batch) ? batch : 500,
    targetMode: mode,
    targetTopicName: mode === 0 ? (form.targetTopicName.trim() || null) : null,
    targetPipelineId: mode === 1 ? (form.targetPipelineId || null) : null,
  }
}

export function isCdcFormValid(form: CdcFormState): boolean {
  if (!form.name.trim() || !form.sourceId || !form.publicationName.trim() || !form.slotName.trim()) {
    return false
  }
  const poll = Number.parseInt(form.pollIntervalSeconds, 10)
  const batch = Number.parseInt(form.maxBatchSize, 10)
  if (!Number.isFinite(poll) || poll < 5 || poll > 3600) return false
  if (!Number.isFinite(batch) || batch < 1 || batch > 5000) return false
  if (form.targetMode === '0') {
    return form.targetTopicName.trim().length > 0
  }
  return form.targetPipelineId.trim().length > 0
}

export function CdcFormDialog({
  crud,
  sources,
  pipelines,
  topics,
}: {
  crud: ReturnType<typeof useCrudForm<CdcFormState, CdcStreamDto>>
  sources: CdcSourceOption[]
  pipelines: CdcPipelineOption[]
  topics: CdcTopicOption[]
}) {
  const t = useTranslations('DataIntegration.cdc')
  const rc = useTranslations('DataIntegration.common')
  const tc = useTranslations('Common')

  const sourceOptions: ComboboxOption[] = sources.map(s => ({ value: s.id, label: s.name }))
  const pipelineOptions: ComboboxOption[] = pipelines.map(p => ({ value: p.id, label: p.name }))
  const topicNames = Array.from(new Set([DEFAULT_CDC_TOPIC, ...topics.map(item => item.name).filter(Boolean)]))
  const topicOptions: ComboboxOption[] = topicNames.map(name => ({ value: name, label: name }))

  return (
    <CrudFormDialog
      crud={crud}
      title={{ create: t('createTitle'), edit: t('editTitle') }}
      contentClassName="sm:max-w-xl"
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
            onChange={v => crud.set({ sourceId: v })}
            placeholder={t('sourcePlaceholder')}
            searchPlaceholder={t('sourceSearchPlaceholder')}
            emptyText={t('sourceEmptyText')}
          />
        </div>
      </div>

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

      <div className="space-y-1.5">
        <Label>{t('monitoredTablesFieldLabel')}</Label>
        <Input
          value={crud.form.monitoredTables}
          onChange={e => crud.set({ monitoredTables: e.target.value })}
          placeholder={t('monitoredTablesPlaceholder')}
          className="font-mono"
        />
        <p className="text-xs text-muted-foreground">{t('monitoredTablesHint')}</p>
      </div>

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
            onValueChange={v => crud.set({ targetMode: v === '1' ? '1' : '0' })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="0">{t('targetModeTopic')}</SelectItem>
              <SelectItem value="1">{t('targetModePipeline')}</SelectItem>
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
        ) : (
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
        )}
      </div>

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
