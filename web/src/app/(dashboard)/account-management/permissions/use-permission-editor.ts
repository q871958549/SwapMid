'use client'

import { useEffect, useMemo, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import type { AccountPermissionUpdateDto } from '@/lib/api/types'
import { getApiErrorMessage } from '@/lib/api-error'
import { toast } from '@/components/ui/use-toast'
import { useAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import { authenticatedBrowserStoragePolicies } from '@/lib/browser-storage-policy'
import { isPermissionDraft, samePermissionKeys, type PermissionDraft } from './permission-draft'
import { applyPermissionSelection } from './permission-model'
import { fetchPermissionGuides, permissionChanges, type PermissionGuide, type PermissionResponse } from './permission-tree'

type PendingAction = { kind: 'role'; role: string } | { kind: 'reset' | 'reload' } | { kind: 'link'; href: string }
const permissionUrl = (role: string) => `/api/proxy/api/permission-management/permissions?providerName=R&providerKey=${encodeURIComponent(role)}`
const draftPolicy = authenticatedBrowserStoragePolicies.authenticatedFormDraft
const draftKey = (role: string) => `role-permissions:v1:${role}`

export function usePermissionEditor(initialRole: string) {
  const t = useTranslations('Account.permission')
  const router = useRouter()
  const browserStorage = useAuthenticatedBrowserStorage()
  const [role, setRole] = useState(initialRole)
  const [loadedRole, setLoadedRole] = useState('')
  const [data, setData] = useState<PermissionResponse | null>(null)
  const [guides, setGuides] = useState<Record<string, PermissionGuide>>({})
  const [baseline, setBaseline] = useState<Record<string, boolean>>({})
  const [granted, setGranted] = useState<Record<string, boolean>>({})
  const [loading, setLoading] = useState(!!initialRole)
  const [loadError, setLoadError] = useState('')
  const [saveError, setSaveError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [reload, setReload] = useState(0)
  const [pending, setPending] = useState<PendingAction | null>(null)
  const [recoverableDraft, setRecoverableDraft] = useState<PermissionDraft | null>(null)
  const [draftWarning, setDraftWarning] = useState('')
  const changes = useMemo(() => permissionChanges(granted, baseline), [granted, baseline])
  const dirty = changes.length > 0
  const admin = role.trim().toLowerCase() === 'admin'
  const readOnly = admin || saving || loading || !data || loadedRole !== role || !!recoverableDraft
  const draftStale = !!recoverableDraft && (!samePermissionKeys(recoverableDraft.baseline, baseline) || permissionChanges(recoverableDraft.baseline, baseline).length > 0)

  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      setLoading(!!role); setLoadError(''); setSaveError(''); setSaved(false)
      setLoadedRole(''); setData(null); setGranted({}); setBaseline({}); setGuides({})
      setRecoverableDraft(null); setDraftWarning('')
      if (!role) return
      try {
        const [response, metadata] = await Promise.all([
          fetch(permissionUrl(role), { signal: controller.signal }), fetchPermissionGuides(controller.signal),
        ])
        if (!response.ok) throw new Error(await response.text())
        const result = await response.json() as PermissionResponse
        if (controller.signal.aborted) return
        const initial = Object.fromEntries(result.groups.flatMap(group => group.permissions.map(item => [item.name, item.isGranted])))
        setData(result); setGuides(metadata); setGranted(initial); setBaseline(initial); setLoadedRole(role)
        const draft = browserStorage.read(draftPolicy, isPermissionDraft, draftKey(role))
        if (draft && permissionChanges(draft.granted, draft.baseline).length) setRecoverableDraft(draft)
      } catch (error) {
        if (!controller.signal.aborted) setLoadError(getApiErrorMessage(error, t('loadFailedTitle')))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }, 0)
    return () => { controller.abort(); window.clearTimeout(timer) }
  }, [role, reload, t, browserStorage])

  useEffect(() => {
    if (!dirty && !saving) return
    const onLeave = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = '' }
    const onLink = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const anchor = (event.target as Element | null)?.closest?.('a[href]') as HTMLAnchorElement | null
      if (!anchor || anchor.target === '_blank' || anchor.hasAttribute('download')) return
      const url = new URL(anchor.href, window.location.href)
      if (url.origin !== window.location.origin || (url.pathname === window.location.pathname && url.search === window.location.search)) return
      event.preventDefault(); event.stopPropagation()
      if (!saving) setPending({ kind: 'link', href: url.pathname + url.search + url.hash })
    }
    window.addEventListener('beforeunload', onLeave)
    document.addEventListener('click', onLink, true)
    return () => { window.removeEventListener('beforeunload', onLeave); document.removeEventListener('click', onLink, true) }
  }, [dirty, saving])

  function perform(action: PendingAction) {
    // 加载失败时尚未读取暂存草稿，普通重试不能删除未知草稿。
    if (dirty || recoverableDraft) browserStorage.remove(draftPolicy, draftKey(role))
    setRecoverableDraft(null); setDraftWarning('')
    setPending(null); setGranted({ ...baseline }); setSaved(false); setSaveError('')
    if (action.kind === 'role') setRole(action.role)
    else if (action.kind === 'reload') setReload(value => value + 1)
    else if (action.kind === 'link') router.push(action.href)
  }
  function request(action: PendingAction) {
    if (saving || (action.kind === 'role' && action.role === role)) return
    if (dirty || recoverableDraft) setPending(action)
    else perform(action)
  }
  function toggle(names: string[], value: boolean) {
    if (readOnly || !data) return
    const next = applyPermissionSelection(data.groups, granted, names, value)
    setGranted(next)
    if (permissionChanges(next, baseline).length) {
      const persisted = browserStorage.write(draftPolicy, { baseline, granted: next }, draftKey(role))
      setDraftWarning(persisted ? '' : t('draftUnavailable'))
    } else { browserStorage.remove(draftPolicy, draftKey(role)); setDraftWarning('') }
    setSaved(false); setSaveError('')
  }
  async function save() {
    if (readOnly || !dirty) return false
    setSaving(true); setSaveError('')
    try {
      // 写前核对已保存状态；发现其他管理员变更时保留草稿，要求重新加载核对。
      const latest = await fetch(permissionUrl(role))
      if (!latest.ok) throw new Error(await latest.text())
      const latestData = await latest.json() as PermissionResponse
      const latestGranted = Object.fromEntries(latestData.groups.flatMap(group => group.permissions.map(item => [item.name, item.isGranted])))
      if (Object.keys(latestGranted).length !== Object.keys(baseline).length || permissionChanges(latestGranted, baseline).length) {
        setSaveError(t('concurrentChangeHint'))
        return false
      }
      const response = await fetch(`/api/proxy/account-roles/${encodeURIComponent(role)}/permissions`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ permissions: changes } satisfies AccountPermissionUpdateDto),
      })
      if (!response.ok) throw new Error(await response.text())
      setBaseline({ ...granted }); setSaved(true)
      browserStorage.remove(draftPolicy, draftKey(role)); setDraftWarning('')
      toast({ variant: 'success', title: t('savedTitle'), description: t('reLoginHint') })
      return true
    } catch (error) {
      setSaveError(getApiErrorMessage(error, t('saveFailedFallback')))
      return false
    } finally { setSaving(false) }
  }

  return { role, data, guides, baseline, granted, loading, loadError, saveError, saving, saved, dirty, changes, admin, readOnly,
    recoverableDraft, draftStale, draftWarning,
    pendingChangeCount: recoverableDraft ? permissionChanges(recoverableDraft.granted, recoverableDraft.baseline).length : changes.length,
    restoreDraft: () => { if (recoverableDraft && !draftStale && !admin && !saving) { setGranted(recoverableDraft.granted); setRecoverableDraft(null) } },
    discardDraft: () => { if (!saving) { browserStorage.remove(draftPolicy, draftKey(role)); setRecoverableDraft(null) } },
    pending, cancelPending: () => setPending(null), confirmPending: () => { if (pending && !saving) perform(pending) },
    requestRole: (value: string) => request({ kind: 'role', role: value }), reset: () => request({ kind: 'reset' }),
    refresh: () => request({ kind: 'reload' }), toggle, save }
}
