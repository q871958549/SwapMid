'use client'

import { useId, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { permissionBreadcrumb } from './permission-model'
import { listNewlyGrantedOps, type PermissionGroup, type PermissionGuide } from './permission-tree'

export function PermissionChangeDialog({ open, onOpenChange, role, groups, granted, baseline, guides, changes, saving, error, onSave }: {
  open: boolean; onOpenChange: (open: boolean) => void; role: string; groups: PermissionGroup[]
  granted: Record<string, boolean>; baseline: Record<string, boolean>; guides: Record<string, PermissionGuide>
  changes: { name: string; isGranted: boolean }[]; saving: boolean; error: string; onSave: () => Promise<boolean>
}) {
  const t = useTranslations('Account.permission')
  const [acknowledgedScope, setAcknowledgedScope] = useState('')
  const id = useId()
  const ops = new Set(listNewlyGrantedOps(granted, baseline, guides))
  const opsScope = [...ops].sort().join('\n')
  const acknowledged = acknowledgedScope === opsScope
  function close() { setAcknowledgedScope(''); onOpenChange(false) }
  return <Dialog open={open} onOpenChange={value => { if (!saving) { setAcknowledgedScope(''); onOpenChange(value) } }}>
    <DialogContent className="flex max-h-[85vh] flex-col sm:max-w-3xl" onEscapeKeyDown={event => { if (saving) event.preventDefault() }} onPointerDownOutside={event => event.preventDefault()}>
      <DialogHeader><DialogTitle>{t('reviewTitle')}</DialogTitle><DialogDescription>{t('reviewHint', { role, count: changes.length })}</DialogDescription></DialogHeader>
      <div className="min-h-0 space-y-4 overflow-y-auto">
        <p className="text-xs text-muted-foreground">{t('dependencyHint')}</p>
        <div className="grid gap-4 sm:grid-cols-2">{[true, false].map(value => {
          const names = new Set(changes.filter(item => item.isGranted === value).map(item => item.name))
          return <section key={String(value)} className="rounded-lg border p-3">
            <h3 className="mb-2 text-sm font-semibold">{t(value ? 'grantChanges' : 'revokeChanges', { count: names.size })}</h3>
            {names.size === 0 && <p className="text-sm text-muted-foreground">{t('noChangesInSection')}</p>}
            {groups.filter(group => group.permissions.some(item => names.has(item.name))).map(group => <div key={group.name} className="mb-3 space-y-1">
              <h4 className="text-xs font-medium text-muted-foreground">{group.displayName}</h4>
              <ul className="space-y-1 text-sm">{group.permissions.filter(item => names.has(item.name)).map(item => <li key={item.name} className={ops.has(item.name) ? 'text-warning' : undefined}>{permissionBreadcrumb(item, group.permissions)}{ops.has(item.name) && <span> · {t('kindOps')}</span>}</li>)}</ul>
            </div>)}
          </section>
        })}</div>
        {ops.size > 0 && <div className="rounded-lg border border-warning/40 bg-warning-soft p-3 text-sm">
          <p className="mb-2">{t('opsConfirmDescription')}</p>
          <div className="flex items-start gap-2"><Checkbox id={id} checked={acknowledged} disabled={saving} onCheckedChange={value => setAcknowledgedScope(value === true ? opsScope : '')} /><label htmlFor={id}>{t('opsAcknowledge')}</label></div>
        </div>}
        {error && <p role="alert" className="rounded-lg bg-destructive/10 p-3 text-sm text-destructive">{error}</p>}
      </div>
      <DialogFooter><Button variant="outline" disabled={saving} onClick={close}>{t('continueEditing')}</Button><Button disabled={saving || changes.length === 0 || (ops.size > 0 && !acknowledged)} onClick={async () => { if (await onSave()) close() }}>{saving ? t('saving') : t('confirmSave')}</Button></DialogFooter>
    </DialogContent>
  </Dialog>
}
