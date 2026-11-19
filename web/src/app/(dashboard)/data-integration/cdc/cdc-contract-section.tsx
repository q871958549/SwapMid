'use client'

import { useTranslations } from 'next-intl'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { CdcStreamDto } from '@/lib/api/types'
import type { useCrudForm } from '@/lib/crud/use-crud-form'
import type { CdcFormState } from './cdc-form-state'

export function CdcContractSection({
  crud,
}: {
  crud: ReturnType<typeof useCrudForm<CdcFormState, CdcStreamDto>>
}) {
  const t = useTranslations('DataIntegration.cdc')

  return (
    <div className="space-y-3 rounded-md border bg-muted/20 p-3">
      <div className="space-y-0.5">
        <div className="text-xs font-medium">{t('contractSectionTitle')}</div>
        <p className="text-xs text-muted-foreground">{t('contractSectionHint')}</p>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('maxFreshnessLagFieldLabel')}</Label>
          <Input
            type="number"
            min={10}
            max={86400}
            value={crud.form.maxFreshnessLagSeconds}
            onChange={e => crud.set({ maxFreshnessLagSeconds: e.target.value })}
            placeholder="300"
            className="font-mono"
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('maxWalLagMbFieldLabel')}</Label>
          <Input
            type="number"
            min={1}
            max={102400}
            value={crud.form.maxAllowedWalLagMb}
            onChange={e => crud.set({ maxAllowedWalLagMb: e.target.value })}
            placeholder="100"
            className="font-mono"
          />
        </div>
      </div>
      <div className="space-y-1.5">
        <Label>{t('requiredColumnsFieldLabel')}</Label>
        <Input
          value={crud.form.requiredColumns}
          onChange={e => crud.set({ requiredColumns: e.target.value })}
          placeholder="id,order_no,amount,status"
          className="font-mono"
        />
      </div>
    </div>
  )
}
