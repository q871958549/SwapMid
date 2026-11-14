'use client'

import { useMemo, useState } from 'react'
import { useSearchParams } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { GitCompare, Loader2, Save, Search, Shield } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { SegmentedControl } from '@/components/ui/segmented-control'
import { DropdownMenuCheckboxItem, DropdownMenuItem, DropdownMenuSeparator } from '@/components/ui/dropdown-menu'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { ListPageShell } from '@/components/data-table/list-page-shell'
import { PageHeader } from '@/components/data-table/page-header'
import { cn } from '@/lib/utils'
import { buildTree, collectNodeNames, findNode, formatPermName, getCheckedState, getGrantedCount, listGrantedDeletePermissions } from './permission-tree'
import { namesInResource, permissionBreadcrumb, selectPermissionScopes, type GrantFilter } from './permission-model'
import { usePermissionEditor } from './use-permission-editor'
import { PermissionActionNode, PermissionCountBadge, PermissionViewCard } from './permission-nodes'
import { PermissionRolePicker } from './permission-role-picker'
import { PermissionChangeDialog } from './permission-change-dialog'
import { ComparePermissionsDialog } from './compare-dialog'
import { ExportPermissionReportButton } from './export-permission-report-button'

export function PermissionsClient() {
  const searchParams = useSearchParams()
  const initialRole = searchParams.get('roleName') ?? ''
  return <PermissionEditor key={initialRole} initialRole={initialRole} />
}

function PermissionEditor({ initialRole }: { initialRole: string }) {
  const t = useTranslations('Account.permission')
  const editor = usePermissionEditor(initialRole)
  const { role, data, granted, baseline, guides, readOnly, saving } = editor
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<GrantFilter>('all')
  const [groupName, setGroupName] = useState('')
  const [resourceName, setResourceName] = useState('')
  const [showIdentifiers, setShowIdentifiers] = useState(false)
  const [compareOpen, setCompareOpen] = useState(false)
  const [exportOpen, setExportOpen] = useState(false)
  const [reviewOpen, setReviewOpen] = useState(false)
  const [revokeOpen, setRevokeOpen] = useState(false)
  const groups = data?.groups ?? []
  const scopes = useMemo(() => selectPermissionScopes(data?.groups ?? [], query, filter, granted, baseline, guides), [data, query, filter, granted, baseline, guides])
  const selectedGroup = scopes.find(entry => entry.group.name === groupName)
    ?? scopes.find(entry => entry.names.some(name => granted[name])) ?? scopes[0]
  const selectedResource = selectedGroup?.nodes.find(node => node.perm.name === resourceName)
    ?? selectedGroup?.nodes.find(node => collectNodeNames(node).some(name => granted[name])) ?? selectedGroup?.nodes[0]
  const visibleNames = scopes.flatMap(entry => entry.names)
  const resourceScope = selectedResource && selectedGroup ? namesInResource(selectedResource, selectedGroup.names) : []
  const deletes = listGrantedDeletePermissions(groups, granted)
  const totalGranted = Object.values(granted).filter(Boolean).length
  const filterOptions = (['all', 'granted', 'ungranted', 'changed'] as const).map(value => ({ value, label: t(`filter.${value}`) }))
  const clearFilters = () => { setQuery(''); setFilter('all') }
  const roleChange = (value: string) => { editor.requestRole(value) }

  return <ListPageShell header={<PageHeader title={t('pageTitle')} description={t('pageDescription')}
    primaryAction={<Button size="sm" disabled={!editor.dirty || readOnly} onClick={() => setReviewOpen(true)}><Save className="mr-1.5 size-4" />{t('saveButton')}</Button>}
    secondaryActions={[<Button key="compare" variant="outline" size="sm" onClick={() => setCompareOpen(true)}><GitCompare className="mr-1.5 size-4" />{t('compareButton')}</Button>]}
    moreActions={<>
      <DropdownMenuItem onSelect={() => setExportOpen(true)}>{t('exportButton')}</DropdownMenuItem>
      <DropdownMenuSeparator />
      <DropdownMenuItem disabled={!role || saving || editor.loading} onSelect={editor.refresh}>{t('reload')}</DropdownMenuItem>
      <DropdownMenuItem disabled={!editor.dirty || saving} onSelect={editor.reset}>{t('resetChanges')}</DropdownMenuItem>
      <DropdownMenuCheckboxItem checked={showIdentifiers} onCheckedChange={setShowIdentifiers}>{t('showIdentifiers')}</DropdownMenuCheckboxItem>
    </>}
    extra={<span aria-live="polite">{editor.dirty ? <Badge variant="secondary">{t('changeCount', { count: editor.changes.length })}</Badge> : editor.saved && <Badge variant="success">{t('savedBadge')}</Badge>}</span>}
  />}>
    <div className="shrink-0 space-y-3 rounded-lg border bg-card px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <div className="w-56"><PermissionRolePicker value={role} onChange={roleChange} disabled={saving} /></div>
        {data && <span className="text-sm text-muted-foreground">{t.rich('grantedCountLabel', { granted: totalGranted, total: Object.keys(granted).length, b: chunks => <strong>{chunks}</strong> })}</span>}
        <Button variant="outline" size="sm" disabled={readOnly || deletes.length === 0} onClick={() => setRevokeOpen(true)}>{t('revokeDeleteButton')}</Button>
        {data && <div className="relative ml-auto w-80 max-w-full"><Search className="absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" /><Input aria-label={t('moduleSearchPlaceholder')} placeholder={t('moduleSearchPlaceholder')} value={query} onChange={event => setQuery(event.target.value)} className="pl-8" /></div>}
      </div>
      {data && <div className="flex flex-wrap items-center gap-3"><SegmentedControl aria-label={t('filterLabel')} value={filter} onValueChange={setFilter} options={filterOptions} /><span className="text-xs text-muted-foreground">{t('scopeHint', { count: visibleNames.length })}</span>{(query || filter !== 'all') && <Button size="sm" variant="ghost" onClick={clearFilters}>{t('clearFilters')}</Button>}</div>}
    </div>

    {editor.saveError && !reviewOpen && <p role="alert" className="rounded-lg border border-destructive/30 p-3 text-sm text-destructive">{editor.saveError}</p>}
    {editor.draftWarning && <p role="alert" className="rounded-lg border border-warning/40 p-3 text-sm text-warning">{editor.draftWarning}</p>}
    {editor.recoverableDraft && <div className="flex shrink-0 flex-wrap items-center gap-3 rounded-lg border bg-card p-3 text-sm"><p className="flex-1">{t(editor.draftStale ? 'draftStale' : 'draftFound')}</p><Button size="sm" variant="outline" disabled={editor.draftStale || editor.admin} onClick={editor.restoreDraft}>{t('restoreDraft')}</Button><Button size="sm" variant="ghost" onClick={editor.discardDraft}>{t('discardDraft')}</Button></div>}
    {!role && <div className="flex flex-1 flex-col items-center justify-center gap-3 rounded-lg border bg-card p-8 text-muted-foreground"><Shield className="size-10 opacity-40" /><p>{t('selectRoleFirstHint')}</p></div>}
    {role && editor.loading && <div role="status" className="flex flex-1 items-center justify-center gap-2 p-8 text-muted-foreground"><Loader2 className="size-5 animate-spin" />{t('loadingPermissions')}</div>}
    {role && !editor.loading && editor.loadError && <div role="alert" className="flex flex-1 flex-col items-center justify-center gap-3 rounded-lg border bg-card p-8"><p className="font-medium">{t('loadFailedTitle')}</p><p className="max-w-xl break-words text-sm text-muted-foreground">{editor.loadError}</p><Button variant="outline" onClick={editor.refresh}>{t('retry')}</Button></div>}
    {role && !editor.loading && data && <>
      {editor.admin && <p className="shrink-0 rounded-lg border border-warning/40 bg-warning-soft p-3 text-sm text-warning">{t('adminReadonlyHint')}</p>}
      {scopes.length === 0 ? <div className="flex flex-1 flex-col items-center justify-center gap-3 rounded-lg border bg-card p-8 text-sm text-muted-foreground"><p>{Object.keys(granted).length === 0 ? t('emptyPermissions') : t('noMatchFound', { query })}</p>{Object.keys(granted).length > 0 && <Button variant="outline" size="sm" onClick={clearFilters}>{t('clearFilters')}</Button>}</div> : <div className="grid min-h-0 flex-1 overflow-hidden rounded-xl border bg-card lg:grid-cols-[240px_280px_minmax(0,1fr)] 2xl:grid-cols-[280px_320px_minmax(0,1fr)]">
        <section aria-label={t('permissionGroupLabel')} className="flex min-h-0 flex-col border-b lg:border-b-0 lg:border-r">
          <div className="flex shrink-0 items-center gap-2 border-b px-4 py-3"><Checkbox aria-label={t('selectFilteredAria')} checked={getCheckedState(visibleNames, granted)} disabled={readOnly || !visibleNames.length} onCheckedChange={value => editor.toggle(visibleNames, value === true)} /><div><h2 className="text-sm font-semibold">{t('permissionGroupLabel')}</h2><p className="text-xs text-muted-foreground">{t('moduleCount', { count: scopes.length })}</p></div></div>
          <div className="min-h-0 flex-1 space-y-1 overflow-y-auto p-2 [overflow-anchor:none]">{scopes.map(entry => {
            const active = entry === selectedGroup
            const allNames = entry.group.permissions.map(item => item.name)
            return <div key={entry.group.name} className={cn('flex items-center gap-2 rounded-lg p-2', active ? 'bg-primary/10' : 'hover:bg-muted/60')}>
              <Checkbox aria-label={t('selectModuleAria', { name: entry.group.displayName })} checked={getCheckedState(entry.names, granted)} disabled={readOnly || !entry.names.length} onCheckedChange={value => editor.toggle(entry.names, value === true)} />
              <button type="button" aria-current={active ? 'true' : undefined} className="min-w-0 flex-1 rounded text-left outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)" onClick={() => { setGroupName(entry.group.name); setResourceName('') }}><span className="block text-sm font-medium">{entry.group.displayName}</span>{showIdentifiers && <span className="block break-all font-mono text-xs text-muted-foreground">{formatPermName(entry.group.name)}</span>}</button>
              <PermissionCountBadge grantedCount={getGrantedCount(allNames, granted)} totalCount={allNames.length} />
            </div>
          })}</div>
        </section>
        <section aria-label={t('resourcesLabel')} className="flex min-h-0 flex-col border-b lg:border-b-0 lg:border-r">
          <div className="shrink-0 border-b px-4 py-3"><h2 className="text-sm font-semibold">{selectedGroup?.group.displayName}</h2><p className="text-xs text-muted-foreground">{t('resourceCount', { count: selectedGroup?.nodes.length ?? 0 })}</p></div>
          <div className="min-h-0 flex-1 space-y-1 overflow-y-auto p-2 [overflow-anchor:none]">{selectedGroup?.nodes.map(node => {
            const names = namesInResource(node, selectedGroup.names)
            const active = node === selectedResource
            const allNames = collectNodeNames(findNode(buildTree(selectedGroup.group.permissions), node.perm.name) ?? node)
            return <div key={node.perm.name} className={cn('flex items-center gap-2 rounded-lg p-2', active ? 'bg-primary/10' : 'hover:bg-muted/60')}>
              <Checkbox aria-label={t('selectResourceAria', { name: node.perm.displayName })} checked={getCheckedState(names, granted)} disabled={readOnly || !names.length} onCheckedChange={value => editor.toggle(names, value === true)} />
              <button type="button" aria-current={active ? 'true' : undefined} className="min-w-0 flex-1 rounded text-left outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)" onClick={() => setResourceName(node.perm.name)}><span className="block text-sm font-medium">{node.perm.displayName}</span>{showIdentifiers && <span className="block break-all font-mono text-xs text-muted-foreground">{formatPermName(node.perm.name)}</span>}</button>
              <PermissionCountBadge grantedCount={getGrantedCount(allNames, granted)} totalCount={allNames.length} />
            </div>
          })}</div>
        </section>
        <section aria-label={t('actionsLabel')} className="flex min-h-0 flex-col">
          {selectedResource && <>
            <div className="flex shrink-0 items-center gap-3 border-b px-5 py-3"><Checkbox aria-label={t('selectResourceAria', { name: selectedResource.perm.displayName })} checked={getCheckedState(resourceScope, granted)} disabled={readOnly || !resourceScope.length} onCheckedChange={value => editor.toggle(resourceScope, value === true)} /><div><h2 className="font-semibold">{selectedResource.perm.displayName}</h2><p className="text-xs text-muted-foreground">{t('dependencyHint')}</p></div></div>
            <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-5 [overflow-anchor:none]">
              <PermissionViewCard node={selectedResource} granted={granted[selectedResource.perm.name] === true} onToggle={value => editor.toggle([selectedResource.perm.name], value)} disabled={readOnly} guide={guides[selectedResource.perm.name]} showIdentifiers={showIdentifiers} />
              {selectedResource.children.length > 0 ? <div className="grid items-start gap-3 xl:grid-cols-2">{selectedResource.children.map(node => <PermissionActionNode key={node.perm.name} node={node} granted={granted} onToggleNode={(item, value) => editor.toggle([item.perm.name], value)} disabled={readOnly} guides={guides} showIdentifiers={showIdentifiers} />)}</div> : <p className="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">{t('noChildActions')}</p>}
            </div>
          </>}
        </section>
      </div>}
    </>}
    <ConfirmDialog open={!!editor.pending} onOpenChange={open => { if (!open) editor.cancelPending() }} title={t('discardTitle')} description={t('discardHint', { role, count: editor.pendingChangeCount })} confirmText={t('discardConfirm')} onConfirm={editor.confirmPending} />
    <ConfirmDialog open={revokeOpen} onOpenChange={setRevokeOpen} title={t('revokeDeleteTitle')} description={<div className="space-y-3"><p>{t('revokeDeleteDescription', { role, count: deletes.length })}</p><ul className="max-h-64 space-y-1 overflow-y-auto">{deletes.map(item => { const group = groups.find(group => group.permissions.some(permission => permission.name === item.name)); return <li key={item.name}>{group?.displayName} · {permissionBreadcrumb(item, group?.permissions ?? [])}</li> })}</ul></div>} confirmText={t('revokeDeleteConfirm')} onConfirm={() => { editor.toggle(deletes.map(item => item.name), false); setRevokeOpen(false) }} />
    <PermissionChangeDialog key={role} open={reviewOpen} onOpenChange={setReviewOpen} role={role} groups={groups} granted={granted} baseline={baseline} guides={guides} changes={editor.changes} saving={saving} error={editor.saveError} onSave={editor.save} />
    <ComparePermissionsDialog open={compareOpen} onOpenChange={setCompareOpen} initialRole={role} dirty={editor.dirty} />
    <ExportPermissionReportButton dirty={editor.dirty} controlledOpen={exportOpen} onOpenChange={setExportOpen} />
  </ListPageShell>
}
