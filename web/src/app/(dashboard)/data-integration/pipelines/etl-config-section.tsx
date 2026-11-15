'use client'

import { useTranslations } from 'next-intl'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  getAllowedWriteModesForTarget,
  getDestinationKindForTarget,
  isReceiptMode,
  isUpsertConfigValid,
  isUpsertMode,
  isValidDestinationForTarget,
  isWriteModeAllowedForTarget,
  parseEtlWriteMode,
  type EtlTransformFormValue,
} from './transform-config'
import { PipelineSchemaPreflightPanel } from './pipeline-schema-preflight'

export function PipelineEtlConfigSection({
  sourceId,
  targetId,
  targetType,
  form,
  receiptTargetSupported,
  onChange,
}: {
  sourceId: string
  targetId: string
  targetType?: number
  form: EtlTransformFormValue
  receiptTargetSupported: boolean
  onChange: (patch: Partial<EtlTransformFormValue>) => void
}) {
  const t = useTranslations('DataIntegration.pipeline')
  const receiptModeSelected = isReceiptMode(form.mode)
  const upsertModeSelected = isUpsertMode(form.mode)
  const upsertConfigValid = isUpsertConfigValid(form)
  const allowedModes = getAllowedWriteModesForTarget(targetType)
  const modeAllowedForTarget = isWriteModeAllowedForTarget(form.mode, targetType)
  const destinationKind = getDestinationKindForTarget(targetType)
  const hasDestinationInput = form.targetTable.trim().length > 0
  const destinationInvalid = hasDestinationInput && !isValidDestinationForTarget(form.targetTable, targetType)

  const destinationLabel = destinationKind === 'topic'
    ? t('targetTopicFieldLabel')
    : destinationKind === 'prefix'
      ? t('targetPrefixFieldLabel')
      : t('targetTableFieldLabel')

  const destinationPlaceholder = destinationKind === 'topic'
    ? 'orders.events'
    : destinationKind === 'prefix'
      ? 'orders/daily'
      : targetType === 4
        ? 'analytics.orders_copy'
        : 'dbo.users_copy'

  return (
    <div className="space-y-2 rounded-md border bg-muted/30 p-3">
      <Label className="text-foreground text-sm font-medium">{t('etlSectionTitle')}</Label>
      <p className="text-muted-foreground text-xs">{t('etlSectionHint')}</p>
      <div className="space-y-1.5">
        <Label className="text-xs">{t('sourceQueryFieldLabel')}</Label>
        <Textarea
          value={form.sourceQuery}
          onChange={e => onChange({ sourceQuery: e.target.value })}
          placeholder="SELECT id, name FROM users WHERE ..."
          className="font-mono text-xs min-h-[72px]"
        />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label className="text-xs">{destinationLabel}</Label>
          <Input
            value={form.targetTable}
            onChange={e => onChange({ targetTable: e.target.value })}
            placeholder={destinationPlaceholder}
            className="font-mono text-xs"
          />
        </div>
        <div className="space-y-1.5">
          <Label className="text-xs">{t('writeModeFieldLabel')}</Label>
          <Select
            value={form.mode}
            onValueChange={(value) => {
              const mode = parseEtlWriteMode(value)
              if (mode) onChange({ mode, transformConfigValid: true })
            }}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {form.mode === 'unsupported' && (
                <SelectItem value="unsupported" disabled>{t('writeModeUnsupported')}</SelectItem>
              )}
              <SelectItem value="append" disabled={!allowedModes.includes('append')}>
                {t('writeModeAppend')}
              </SelectItem>
              <SelectItem value="truncate" disabled={!allowedModes.includes('truncate')}>
                {t('writeModeTruncate')}
              </SelectItem>
              <SelectItem value="upsert" disabled={!allowedModes.includes('upsert')}>
                {t('writeModeUpsert')}
              </SelectItem>
              <SelectItem
                value="append-receipt-v1"
                disabled={!receiptTargetSupported || !allowedModes.includes('append-receipt-v1')}
              >
                {t('writeModeAppendReceipt')}
              </SelectItem>
              <SelectItem
                value="truncate-receipt-v1"
                disabled={!receiptTargetSupported || !allowedModes.includes('truncate-receipt-v1')}
              >
                {t('writeModeTruncateReceipt')}
              </SelectItem>
              <SelectItem
                value="upsert-receipt-v1"
                disabled={!receiptTargetSupported || !allowedModes.includes('upsert-receipt-v1')}
              >
                {t('writeModeUpsertReceipt')}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>
      {upsertModeSelected && (
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label className="text-xs">{t('keyColumnsFieldLabel')}</Label>
            <Input
              value={form.keyColumnsText ?? ''}
              onChange={e => onChange({ keyColumnsText: e.target.value })}
              placeholder="order_id, line_no"
              className="font-mono text-xs"
            />
          </div>
          <div className="space-y-1.5">
            <Label className="text-xs">{t('watermarkColumnFieldLabel')}</Label>
            <Input
              value={form.watermarkColumn ?? ''}
              onChange={e => onChange({ watermarkColumn: e.target.value })}
              placeholder="updated_at"
              className="font-mono text-xs"
            />
          </div>
        </div>
      )}
      {destinationInvalid && (
        <p role="alert" className="text-destructive text-xs">{t('destinationInvalidHint')}</p>
      )}
      {upsertModeSelected && !upsertConfigValid && (
        <p role="alert" className="text-destructive text-xs">{t('upsertConfigInvalidHint')}</p>
      )}
      {upsertModeSelected && upsertConfigValid && modeAllowedForTarget && (
        <p className="text-muted-foreground text-xs">{t('upsertModeHint')}</p>
      )}
      {!form.transformConfigValid && (
        <p role="alert" className="text-destructive text-xs">{t('writeModeUnsupportedHint')}</p>
      )}
      {form.transformConfigValid && receiptModeSelected && !receiptTargetSupported && (
        <p role="alert" className="text-destructive text-xs">{t('receiptTargetUnsupportedHint')}</p>
      )}
      {form.transformConfigValid && !receiptModeSelected && !modeAllowedForTarget && (
        <p role="alert" className="text-destructive text-xs">{t('writeModeTargetUnsupportedHint')}</p>
      )}
      {receiptModeSelected && receiptTargetSupported && (
        <p className="text-muted-foreground text-xs">{t('receiptModeHint')}</p>
      )}
      {(targetType === 3 || targetType === 5) && modeAllowedForTarget && (
        <p className="text-muted-foreground text-xs">{t('schemaOnWriteSinkHint')}</p>
      )}
      <PipelineSchemaPreflightPanel
        sourceId={sourceId}
        targetId={targetId}
        form={form}
        onMappingsChange={columnMappings => onChange({ columnMappings })}
      />
    </div>
  )
}
