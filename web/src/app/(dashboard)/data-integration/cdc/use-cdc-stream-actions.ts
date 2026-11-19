'use client'

import { useCallback, useRef, useState } from 'react'
import { useTranslations } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import { parseOptionalJsonResponse, readApiErrorMessage } from '@/lib/api-error'
import type {
  CdcAcceptSchemaResultDto,
  CdcCaptureResultDto,
  CdcCatalogSyncResultDto,
  CdcDropSlotResultDto,
  CdcLakehouseCompactionResultDto,
  CdcLakehouseHistoryDto,
  CdcPreflightResultDto,
  CdcSinkTemplateDto,
  CdcSnapshotResultDto,
  CdcStreamDto,
} from '@/lib/api/types'
import type { CdcPreflightState, CdcSinkTemplateState } from './cdc-dialogs'
import type { CdcLakehouseHistoryState } from './cdc-lakehouse-history-dialog'

export function useCdcStreamActions(reloadAndRefresh: () => Promise<void>) {
  const t = useTranslations('DataIntegration.cdc')

  const [busyActions, setBusyActions] = useState<Record<string, string>>({})
  const busyActionsRef = useRef<Record<string, string>>({})
  const [preflightState, setPreflightState] = useState<CdcPreflightState | null>(null)
  const [checkpointTarget, setCheckpointTarget] = useState<CdcStreamDto | null>(null)
  const [checkpointInput, setCheckpointInput] = useState('')
  const [checkpointSubmitting, setCheckpointSubmitting] = useState(false)
  const [checkpointError, setCheckpointError] = useState<string | null>(null)
  const [dropSlotTarget, setDropSlotTarget] = useState<CdcStreamDto | null>(null)
  const [dropSlotSubmitting, setDropSlotSubmitting] = useState(false)
  const [dropSlotError, setDropSlotError] = useState<string | null>(null)
  const [sinkTemplateState, setSinkTemplateState] = useState<CdcSinkTemplateState | null>(null)
  const [historyState, setHistoryState] = useState<CdcLakehouseHistoryState | null>(null)
  const [historyLoadingVersion, setHistoryLoadingVersion] = useState<number | null>(null)
  const [provisionSubmitting, setProvisionSubmitting] = useState(false)
  const [compactSubmitting, setCompactSubmitting] = useState(false)

  const setBusy = useCallback((id: string, kind: string | null) => {
    if (kind === null) {
      delete busyActionsRef.current[id]
      setBusyActions((prev) => {
        const next = { ...prev }
        delete next[id]
        return next
      })
      return
    }
    busyActionsRef.current[id] = kind
    setBusyActions(prev => ({ ...prev, [id]: kind }))
  }, [])

  const runStreamAction = useCallback(async <T,>(
    row: CdcStreamDto,
    kind: string,
    url: string,
    method: 'GET' | 'POST' | 'PUT',
    failTitle: string,
    failFallback: string,
    onOk: (data: T) => void | Promise<void>,
  ) => {
    if (busyActionsRef.current[row.id]) return
    setBusy(row.id, kind)
    try {
      const res = method === 'GET' ? await fetch(url) : await fetch(url, { method })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: failTitle,
          description: await readApiErrorMessage(res, failFallback),
        })
        // 捕获/快照/生命周期失败可能已由后端触发 Schema 漂移自动暂停或状态变更，刷新列表以同步徽章
        if (method !== 'GET') await Promise.resolve(reloadAndRefresh()).catch(() => {})
        return
      }
      await onOk(await parseOptionalJsonResponse<T>(res))
    } finally {
      setBusy(row.id, null)
    }
  }, [reloadAndRefresh, setBusy])

  const handlePreflight = useCallback((row: CdcStreamDto) => runStreamAction<CdcPreflightResultDto>(
    row, 'preflight', `/api/proxy/cdc-streams/${row.id}/preflight`, 'POST',
    t('preflightFailedTitle'), t('preflightFailedFallback'),
    result => setPreflightState({ stream: row, result }),
  ), [runStreamAction, t])

  const handleLifecycle = useCallback((row: CdcStreamDto, action: 'activate' | 'pause') => runStreamAction<void>(
    row, action, `/api/proxy/cdc-streams/${row.id}/${action}`, 'PUT',
    action === 'activate' ? t('activateFailedTitle') : t('pauseFailedTitle'), t('lifecycleFailedFallback'),
    async () => {
      toast({
        variant: 'success',
        title: action === 'activate' ? t('activateSuccessTitle') : t('pauseSuccessTitle'),
        description: row.name,
      })
      await reloadAndRefresh()
    },
  ), [reloadAndRefresh, runStreamAction, t])

  const handleCaptureNow = useCallback((row: CdcStreamDto) => runStreamAction<CdcCaptureResultDto>(
    row, 'capture', `/api/proxy/cdc-streams/${row.id}/capture`, 'POST',
    t('captureFailedTitle'), t('captureFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('captureSuccessTitle'),
        description: t('captureSuccessDesc', {
          captured: result.capturedEventCount ?? 0,
          delivered: result.deliveredCount ?? 0,
          lsn: result.newCheckpointLsn || '-',
        }),
      })
      await reloadAndRefresh()
    },
  ), [reloadAndRefresh, runStreamAction, t])

  const openCheckpointDialog = useCallback((row: CdcStreamDto) => {
    setCheckpointTarget(row)
    setCheckpointInput(row.checkpointLsn ?? '')
    setCheckpointError(null)
  }, [])

  const submitCheckpointReset = useCallback(async () => {
    if (!checkpointTarget) return
    setCheckpointSubmitting(true)
    setCheckpointError(null)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${checkpointTarget.id}/checkpoint`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ checkpointLsn: checkpointInput.trim() || null }),
      })
      if (!res.ok) {
        setCheckpointError(await readApiErrorMessage(res, t('checkpointFailedFallback')))
        return
      }
      toast({
        variant: 'success',
        title: t('checkpointSuccessTitle'),
        description: checkpointTarget.name,
      })
      setCheckpointTarget(null)
      await reloadAndRefresh()
    } finally {
      setCheckpointSubmitting(false)
    }
  }, [checkpointInput, checkpointTarget, reloadAndRefresh, t])

  const openDropSlotDialog = useCallback((row: CdcStreamDto) => {
    setDropSlotTarget(row)
    setDropSlotError(null)
  }, [])

  const submitDropSlot = useCallback(async () => {
    if (!dropSlotTarget) return
    setDropSlotSubmitting(true)
    setDropSlotError(null)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${dropSlotTarget.id}/drop-slot`, { method: 'POST' })
      if (!res.ok) {
        setDropSlotError(await readApiErrorMessage(res, t('dropSlotFailedFallback')))
        return
      }
      const result = await res.json() as CdcDropSlotResultDto
      toast({
        variant: 'success',
        title: t('dropSlotSuccessTitle'),
        description: result.message || result.slotName,
      })
      setDropSlotTarget(null)
      await reloadAndRefresh()
    } finally {
      setDropSlotSubmitting(false)
    }
  }, [dropSlotTarget, reloadAndRefresh, t])

  const handleSinkTemplate = useCallback((row: CdcStreamDto) => runStreamAction<CdcSinkTemplateDto>(
    row, 'sink-template', `/api/proxy/cdc-streams/${row.id}/sink-template`, 'GET',
    t('sinkTemplateFailedTitle'), t('sinkTemplateFailedFallback'),
    template => setSinkTemplateState({ stream: row, template }),
  ), [runStreamAction, t])

  const handleSnapshot = useCallback((row: CdcStreamDto) => runStreamAction<CdcSnapshotResultDto>(
    row, 'snapshot', `/api/proxy/cdc-streams/${row.id}/snapshot`, 'POST',
    t('snapshotFailedTitle'), t('snapshotFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('snapshotSuccessTitle'),
        description: t('snapshotSuccessDesc', {
          rows: result.snapshotRowCount ?? 0,
          delivered: result.deliveredCount ?? 0,
          lsn: result.consistentPointLsn || '-',
        }),
      })
      await reloadAndRefresh()
    },
  ), [reloadAndRefresh, runStreamAction, t])

  const handleAcceptSchema = useCallback((row: CdcStreamDto) => runStreamAction<CdcAcceptSchemaResultDto>(
    row, 'accept-schema', `/api/proxy/cdc-streams/${row.id}/accept-schema`, 'POST',
    t('acceptSchemaFailedTitle'), t('acceptSchemaFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('acceptSchemaSuccessTitle'),
        description: result.message || row.name,
      })
      await reloadAndRefresh()
    },
  ), [reloadAndRefresh, runStreamAction, t])

  const handleProvisionSink = useCallback(async () => {
    if (!sinkTemplateState) return
    setProvisionSubmitting(true)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${sinkTemplateState.stream.id}/provision-sink`, {
        method: 'POST',
      })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: t('provisionClickHouseFailedTitle'),
          description: await readApiErrorMessage(res, t('provisionClickHouseFailedFallback')),
        })
        return
      }
      const updated = await res.json() as CdcSinkTemplateDto
      setSinkTemplateState({ stream: sinkTemplateState.stream, template: updated })
      toast({
        variant: 'success',
        title: t('provisionClickHouseSuccessTitle'),
        description: updated.destination,
      })
    } finally {
      setProvisionSubmitting(false)
    }
  }, [sinkTemplateState, t])

  const handleCompactLakehouse = useCallback(async () => {
    if (!sinkTemplateState) return
    const stream = sinkTemplateState.stream
    setCompactSubmitting(true)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${stream.id}/compact-lakehouse`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ minFiles: 2, deleteCompactedFiles: true }),
      })
      if (!res.ok) {
        toast({
          variant: 'destructive',
          title: t('compactLakehouseFailedTitle'),
          description: await readApiErrorMessage(res, t('compactLakehouseFailedFallback')),
        })
        return
      }
      const result = await res.json() as CdcLakehouseCompactionResultDto
      toast({
        variant: 'success',
        title: result.compacted ? t('compactLakehouseSuccessTitle') : t('compactLakehouseSkippedTitle'),
        description: result.message || stream.name,
      })
      const tplRes = await fetch(`/api/proxy/cdc-streams/${stream.id}/sink-template`)
      if (tplRes.ok) {
        const updatedTemplate = await tplRes.json() as CdcSinkTemplateDto
        setSinkTemplateState({ stream, template: updatedTemplate })
      }
      await reloadAndRefresh()
    } finally {
      setCompactSubmitting(false)
    }
  }, [reloadAndRefresh, sinkTemplateState, t])

  const handleOpenHistory = useCallback(async (row: CdcStreamDto, version?: number) => {
    const qs = version !== undefined ? `?version=${version}&maxCommits=32` : '?maxCommits=32'
    if (version !== undefined) {
      setHistoryLoadingVersion(version)
      try {
        const res = await fetch(`/api/proxy/cdc-streams/${row.id}/lakehouse-history${qs}`)
        if (!res.ok) {
          toast({
            variant: 'destructive',
            title: t('lakehouseHistoryFailedTitle'),
            description: await readApiErrorMessage(res, t('lakehouseHistoryFailedFallback')),
          })
          return
        }
        const history = await parseOptionalJsonResponse<CdcLakehouseHistoryDto>(res)
        setHistoryState({ stream: row, history })
      } finally {
        setHistoryLoadingVersion(null)
      }
      return
    }

    await runStreamAction<CdcLakehouseHistoryDto>(
      row, 'history', `/api/proxy/cdc-streams/${row.id}/lakehouse-history${qs}`, 'GET',
      t('lakehouseHistoryFailedTitle'), t('lakehouseHistoryFailedFallback'),
      history => setHistoryState({ stream: row, history }),
    )
  }, [runStreamAction, t])

  const handleSyncCatalog = useCallback((row: CdcStreamDto) => runStreamAction<CdcCatalogSyncResultDto>(
    row, 'sync-catalog', `/api/proxy/cdc-streams/${row.id}/sync-catalog`, 'POST',
    t('syncCatalogFailedTitle'), t('syncCatalogFailedFallback'),
    async (result) => {
      toast({
        variant: 'success',
        title: t('syncCatalogSuccessTitle'),
        description: result.message || row.name,
      })
      await reloadAndRefresh()
    },
  ), [reloadAndRefresh, runStreamAction, t])

  return {
    busyActions,
    preflightState,
    setPreflightState,
    checkpointTarget,
    setCheckpointTarget,
    checkpointInput,
    setCheckpointInput,
    checkpointSubmitting,
    checkpointError,
    dropSlotTarget,
    setDropSlotTarget,
    dropSlotSubmitting,
    dropSlotError,
    sinkTemplateState,
    setSinkTemplateState,
    historyState,
    setHistoryState,
    historyLoadingVersion,
    provisionSubmitting,
    compactSubmitting,
    handlePreflight,
    handleLifecycle,
    handleCaptureNow,
    openCheckpointDialog,
    submitCheckpointReset,
    openDropSlotDialog,
    submitDropSlot,
    handleSinkTemplate,
    handleOpenHistory,
    handleSyncCatalog,
    handleSnapshot,
    handleAcceptSchema,
    handleProvisionSink,
    handleCompactLakehouse,
  }
}
