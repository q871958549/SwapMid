'use client'

import { useTranslations } from 'next-intl'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  isReceiptMode,
  isUpsertConfigValid,
  isUpsertMode,
  parseEtlWriteMode,
  type EtlTransformFormValue,
} from './transform-config'

export function PipelineEtlConfigSection({
  form,
  receiptTargetSupported,
  onChange,
}: {
  form: EtlTransformFormValue
  receiptTargetSupported: boolean
  onChange: (patch: Partial<EtlTransformFormValue>) => void
}) {
  const t = useTranslations('DataIntegration.pipeline')
  const receiptModeSelected = isReceiptMode(form.mode)
  const upsertModeSelected = isUpsertMode(form.mode)
  const upsertConfigValid = isUpsertConfigValid(form)

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
          <Label className="text-xs">{t('targetTableFieldLabel')}</Label>
          <Input
            value={form.targetTable}
            onChange={e => onChange({ targetTable: e.target.value })}
            placeholder="dbo.users_copy"
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
              <SelectItem value="append">{t('writeModeAppend')}</SelectItem>
              <SelectItem value="truncate">{t('writeModeTruncate')}</SelectItem>
              <SelectItem value="upsert">{t('writeModeUpsert')}</SelectItem>
              <SelectItem value="append-receipt-v1" disabled={!receiptTargetSupported}>
                {t('writeModeAppendReceipt')}
              </SelectItem>
              <SelectItem value="truncate-receipt-v1" disabled={!receiptTargetSupported}>
                {t('writeModeTruncateReceipt')}
              </SelectItem>
              <SelectItem value="upsert-receipt-v1" disabled={!receiptTargetSupported}>
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
      {upsertModeSelected && !upsertConfigValid && (
        <p role="alert" className="text-destructive text-xs">{t('upsertConfigInvalidHint')}</p>
      )}
      {upsertModeSelected && upsertConfigValid && (
        <p className="text-muted-foreground text-xs">{t('upsertModeHint')}</p>
      )}
      {!form.transformConfigValid && (
        <p role="alert" className="text-destructive text-xs">{t('writeModeUnsupportedHint')}</p>
      )}
      {form.transformConfigValid && receiptModeSelected && !receiptTargetSupported && (
        <p role="alert" className="text-destructive text-xs">{t('receiptTargetUnsupportedHint')}</p>
      )}
      {receiptModeSelected && receiptTargetSupported && (
        <p className="text-muted-foreground text-xs">{t('receiptModeHint')}</p>
      )}
    </div>
  )
}
