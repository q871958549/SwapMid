'use client'

import { useEffect, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Loader2 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { SegmentedControl } from '@/components/ui/segmented-control'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { diffGranted, type PermissionGroup, type PermissionResponse } from './permission-tree'
import { permissionBreadcrumb } from './permission-model'
import { PermissionRolePicker } from './permission-role-picker'

export function ComparePermissionsDialog({ open, onOpenChange, initialRole = '', dirty = false }: {
  open: boolean; onOpenChange: (open: boolean) => void; initialRole?: string; dirty?: boolean
}) {
  const t = useTranslations('Account.permission')
  const [roleA, setRoleA] = useState(initialRole)
  const [roleB, setRoleB] = useState('')
  const [result, setResult] = useState<{ a: string; b: string; dataA: PermissionResponse; dataB: PermissionResponse } | null>(null)
  const [failed, setFailed] = useState(false)
  const [query, setQuery] = useState('')
  const [mode, setMode] = useState<'differences' | 'all'>('differences')
  const [retry, setRetry] = useState(0)
  const [prevOpen, setPrevOpen] = useState(open)
  if (open !== prevOpen) {
    setPrevOpen(open)
    if (open) { setRoleA(initialRole); setRoleB(''); setResult(null); setFailed(false); setQuery(''); setMode('differences') }
  }
  useEffect(() => {
    if (!open || !roleA || !roleB || roleA === roleB) return
    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      setFailed(false); setResult(null)
      try {
        const results = await Promise.all([roleA, roleB].map(async role => {
          const response = await fetch(`/api/proxy/account-roles/${encodeURIComponent(role)}/permissions`, { signal: controller.signal })
          if (!response.ok) throw new Error('permission comparison failed')
          return await response.json() as PermissionResponse
        }))
        if (!controller.signal.aborted) setResult({ a: roleA, b: roleB, dataA: results[0], dataB: results[1] })
      } catch { if (!controller.signal.aborted) setFailed(true) }
    }, 0)
    return () => { controller.abort(); window.clearTimeout(timer) }
  }, [open, roleA, roleB, retry])
  const selected = !!roleA && !!roleB && roleA !== roleB
  const current = result?.a === roleA && result?.b === roleB ? result : null
  const diff = current ? diffGranted(current.dataA.groups.flatMap(group => group.permissions.filter(item => item.isGranted).map(item => item.name)), current.dataB.groups.flatMap(group => group.permissions.filter(item => item.isGranted).map(item => item.name))) : null
  return <Dialog open={open} onOpenChange={onOpenChange}>
    <DialogContent className="flex max-h-[85vh] flex-col overflow-hidden sm:max-w-5xl">
      <DialogHeader><DialogTitle>{t('compareDialogTitle')}</DialogTitle><DialogDescription>{t(dirty ? 'compareUnsavedHint' : 'compareSavedHint')}</DialogDescription></DialogHeader>
      <div className="grid shrink-0 gap-3 sm:grid-cols-2">{[{ value: roleA, onChange: setRoleA, label: t('compareRoleALabel') }, { value: roleB, onChange: setRoleB, label: t('compareRoleBLabel') }].map(item => <div key={item.label} className="space-y-1"><p className="text-xs text-muted-foreground">{item.label}</p><PermissionRolePicker {...item} /></div>)}</div>
      {!roleA || !roleB ? <p className="py-8 text-center text-sm text-muted-foreground">{t('compareSelectBothHint')}</p> : roleA === roleB && <p className="py-8 text-center text-sm text-muted-foreground">{t('compareSameRoleHint')}</p>}
      {selected && failed && <div role="alert" className="space-y-2 py-6 text-center"><p className="text-destructive">{t('compareLoadFailedTitle')}</p><Button variant="outline" onClick={() => setRetry(value => value + 1)}>{t('retry')}</Button></div>}
      {selected && !failed && !current && <p role="status" className="flex items-center justify-center gap-2 py-8 text-sm text-muted-foreground"><Loader2 className="size-4 animate-spin" />{t('compareLoading')}</p>}
      {selected && !failed && current && diff && <>
        <div className="flex shrink-0 flex-wrap gap-3"><SegmentedControl aria-label={t('compareScope')} value={mode} onValueChange={setMode} options={[{ value: 'differences', label: t('compareDifferences') }, { value: 'all', label: t('filter.all') }]} /><Input className="min-w-48 flex-1" value={query} onChange={event => setQuery(event.target.value)} placeholder={t('compareSearch')} aria-label={t('compareSearch')} /></div>
        <div className={`grid min-h-0 flex-1 gap-3 overflow-y-auto ${mode === 'all' ? 'sm:grid-cols-3' : 'sm:grid-cols-2'}`}>
          <ComparePane title={t('compareOnlyALabel', { name: roleA })} groups={current.dataA.groups} names={diff.onlyA} query={query} />
          {mode === 'all' && <ComparePane title={t('compareBothLabel')} groups={current.dataA.groups} names={diff.both} query={query} />}
          <ComparePane title={t('compareOnlyBLabel', { name: roleB })} groups={current.dataB.groups} names={diff.onlyB} query={query} />
        </div>
      </>}
    </DialogContent>
  </Dialog>
}

function ComparePane({ title, groups, names, query }: { title: string; groups: PermissionGroup[]; names: string[]; query: string }) {
  const t = useTranslations('Account.permission')
  const set = new Set(names)
  const search = query.trim().toLowerCase()
  const entries = groups.map(group => ({ group, items: group.permissions.filter(item => set.has(item.name) && `${group.displayName} ${item.name} ${permissionBreadcrumb(item, group.permissions)}`.toLowerCase().includes(search)) })).filter(entry => entry.items.length)
  const count = entries.reduce((sum, entry) => sum + entry.items.length, 0)
  return <section className="flex min-h-0 flex-col rounded-lg border"><div className="flex items-center justify-between gap-2 border-b p-3"><h3 className="text-sm font-medium">{title}</h3><Badge variant="secondary">{count}</Badge></div><div className="min-h-0 flex-1 space-y-3 overflow-y-auto p-3">
    {count === 0 && <p className="py-6 text-center text-sm text-muted-foreground">{t('compareEmpty')}</p>}
    {entries.map(({ group, items }) => <div key={group.name}><h4 className="mb-1 text-xs font-medium text-muted-foreground">{group.displayName}</h4><ul className="space-y-1 text-sm">{items.map(item => <li key={item.name}>{permissionBreadcrumb(item, group.permissions)}</li>)}</ul></div>)}
  </div></section>
}
