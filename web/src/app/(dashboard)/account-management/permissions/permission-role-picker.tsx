'use client'

import { useEffect, useRef, useState } from 'react'
import { useTranslations } from 'next-intl'
import { RemoteCombobox } from '@/components/ui/remote-combobox'
import { Button } from '@/components/ui/button'
import { fetchRoleOptions } from './permission-tree'

export function PermissionRolePicker({ value, onChange, disabled, label }: { value: string; onChange: (value: string) => void; disabled?: boolean; label?: string }) {
  const t = useTranslations('Account')
  const [failed, setFailed] = useState(false)
  const [retry, setRetry] = useState(0)
  const [retrying, setRetrying] = useState(false)
  const retryController = useRef<AbortController | null>(null)
  useEffect(() => () => retryController.current?.abort(), [])
  async function retryRoles() {
    retryController.current?.abort()
    const controller = new AbortController()
    retryController.current = controller
    setRetrying(true)
    try {
      await fetchRoleOptions('', t('common.systemRoleLabel'), controller.signal)
      if (!controller.signal.aborted) { setFailed(false); setRetry(value => value + 1) }
    } catch { if (!controller.signal.aborted) setFailed(true) }
    finally { if (!controller.signal.aborted) setRetrying(false) }
  }
  return <div className="space-y-1" role="group" aria-label={label ?? t('permission.rolePlaceholder')}>
    <RemoteCombobox value={value} selectedLabel={value || undefined} onChange={onChange} disabled={disabled} scopeKey={String(retry)}
      fetchOptions={async (query, signal) => {
        try {
          const options = await fetchRoleOptions(query, t('common.systemRoleLabel'), signal)
          if (!signal.aborted) setFailed(false)
          return options
        } catch (error) { if (!signal.aborted) setFailed(true); throw error }
      }}
      placeholder={t('permission.rolePlaceholder')} searchPlaceholder={t('permission.roleSearchPlaceholder')}
      emptyText={failed ? t('permission.roleLoadFailed') : t('permission.roleEmptyText')} loadingText={t('permission.roleLoadingText')} clearable />
    {failed && <div role="alert" className="text-xs text-destructive">{t('permission.roleLoadFailed')} <Button size="sm" variant="ghost" disabled={retrying} onClick={() => void retryRoles()}>{t('permission.retry')}</Button></div>}
  </div>
}
