'use client'

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import {
  isEtlExecutionUuid,
  persistEtlExecutionRecovery,
  readEtlExecutionRecoveryResult,
  removeEtlExecutionRecovery,
  subscribeEtlExecutionRecovery,
  upsertEtlExecutionRecovery,
  verifyEtlExecutionRun,
  withEtlExecutionRecoveryLock,
  type EtlExecutionRecoveryDisposition,
  type EtlExecutionRecoveryEntry,
  type EtlExecutionVerification,
} from './execution-recovery'

export interface EtlExecutionRunClaim {
  runId: string
  disposition: EtlExecutionRecoveryDisposition
  isReplay: boolean
}

export type EtlExecutionRecoveryClearResult =
  | 'removed'
  | 'already-absent'
  | 'superseded'
  | 'unavailable'

export type EtlExecutionRecoveryStatus = 'loading' | 'ready' | 'unavailable'

interface LockedMutation<T> {
  entries: EtlExecutionRecoveryEntry[]
  value: T
}

export function useEtlExecutionRecovery() {
  const browserStorage = useAuthenticatedBrowserStorage()
  const [pendingRunIds, setPendingRunIds] = useState<Record<string, string>>({})
  const [blockedPipelines, setBlockedPipelines] = useState<Record<string, true>>({})
  const [readyStorage, setReadyStorage] = useState<typeof browserStorage | null>(null)
  const [unavailableStorage, setUnavailableStorage] = useState<typeof browserStorage | null>(null)
  const pendingRunIdsRef = useRef<Record<string, string>>({})
  const blockedPipelinesRef = useRef<Record<string, true>>({})
  const recoveryEntriesRef = useRef<EtlExecutionRecoveryEntry[]>([])
  const activeStorageRef = useRef(browserStorage)
  const readyStorageRef = useRef<typeof browserStorage | null>(null)
  const unavailableStorageRef = useRef<typeof browserStorage | null>(null)
  const verificationRequestsRef = useRef<Record<string, {
    runId: string
    storage: typeof browserStorage
    controller: AbortController
  }>>({})

  const abortVerification = useCallback((pipelineId: string) => {
    const pipelineKey = pipelineId.toLowerCase()
    const request = verificationRequestsRef.current[pipelineKey]
    if (!request) return
    delete verificationRequestsRef.current[pipelineKey]
    request.controller.abort()
  }, [])

  const markStorageUnavailable = useCallback((storage: typeof browserStorage) => {
    if (activeStorageRef.current !== storage) return
    if (readyStorageRef.current !== storage) return
    readyStorageRef.current = null
    unavailableStorageRef.current = storage
    setReadyStorage(current => current === storage ? null : current)
    setUnavailableStorage(storage)
    for (const [pipelineId, request] of Object.entries(verificationRequestsRef.current)) {
      if (request.storage !== storage) continue
      delete verificationRequestsRef.current[pipelineId]
      request.controller.abort()
    }
  }, [])

  const applyEntries = useCallback((
    entries: readonly EtlExecutionRecoveryEntry[],
    storage: typeof browserStorage,
  ) => {
    const pending: Record<string, string> = {}
    const blocked: Record<string, true> = {}
    for (const entry of entries) {
      pending[entry.pipelineId] = entry.runId
      if (entry.disposition === 'outcome-unknown') blocked[entry.pipelineId] = true
    }

    for (const [pipelineId, request] of Object.entries(verificationRequestsRef.current)) {
      if (request.storage === storage && pending[pipelineId] === request.runId) continue
      delete verificationRequestsRef.current[pipelineId]
      request.controller.abort()
    }

    recoveryEntriesRef.current = [...entries]
    pendingRunIdsRef.current = pending
    blockedPipelinesRef.current = blocked
    setPendingRunIds(pending)
    setBlockedPipelines(blocked)
  }, [])

  const markStorageReady = useCallback((
    storage: typeof browserStorage,
    entries: readonly EtlExecutionRecoveryEntry[],
  ) => {
    if (activeStorageRef.current !== storage) return
    applyEntries(entries, storage)
    readyStorageRef.current = storage
    unavailableStorageRef.current = null
    setUnavailableStorage(null)
    setReadyStorage(storage)
  }, [applyEntries])

  // React 会在新身份 commit 后才清理旧 passive storage listener。这里在 layout 阶段先切换
  // 活跃文档并关闭入口，使该窄窗口内到达的旧 principal 事件也无法写入新页面的 LKG。
  useLayoutEffect(() => {
    activeStorageRef.current = browserStorage
    readyStorageRef.current = null
    unavailableStorageRef.current = null
  }, [browserStorage])

  useEffect(() => {
    // 身份文档切换后先关闭入口；即使同 principal 可复用 localStorage，也必须等新 Provider
    // 完成一次严格读取，旧异步回调不得借旧 browserStorage 改写新文档的内存快照。
    readyStorageRef.current = null
    unavailableStorageRef.current = null
    const restoreTimer = setTimeout(() => {
      if (activeStorageRef.current !== browserStorage) return
      const result = readEtlExecutionRecoveryResult(browserStorage)
      if (result.status === 'unavailable') {
        unavailableStorageRef.current = browserStorage
        setUnavailableStorage(browserStorage)
        return
      }
      markStorageReady(browserStorage, result.entries)
    }, 0)
    return () => clearTimeout(restoreTimer)
  }, [browserStorage, markStorageReady])

  const runLockedMutation = useCallback(async <T,>(
    operation: (current: EtlExecutionRecoveryEntry[]) => LockedMutation<T> | null,
  ): Promise<T | null> => {
    const capturedStorage = browserStorage
    if (readyStorageRef.current !== capturedStorage) return null

    const locked = await withEtlExecutionRecoveryLock(capturedStorage, () => {
      if (readyStorageRef.current !== capturedStorage) return null
      // 必须在锁内重读 principal/localStorage；React ref 只是渲染快照，不能作为跨标签裁决源。
      const result = readEtlExecutionRecoveryResult(capturedStorage)
      if (result.status === 'unavailable') {
        markStorageUnavailable(capturedStorage)
        return null
      }
      return operation(result.entries)
    })
    if (!locked.acquired || locked.value === null) return null
    if (readyStorageRef.current !== capturedStorage) return null

    applyEntries(locked.value.entries, capturedStorage)
    return locked.value.value
  }, [applyEntries, browserStorage, markStorageUnavailable])

  const claimRunId = useCallback(async (
    pipelineId: string,
  ): Promise<EtlExecutionRunClaim | null> => {
    if (!isEtlExecutionUuid(pipelineId)) return null
    const pipelineKey = pipelineId.toLowerCase()

    const lockedClaim = await runLockedMutation<EtlExecutionRunClaim>((current) => {
      const existing = current.find(item => item.pipelineId === pipelineKey)
      if (existing) {
        return {
          entries: current,
          value: {
            runId: existing.runId,
            disposition: existing.disposition,
            isReplay: true,
          },
        }
      }

      let runId: string
      try {
        runId = crypto.randomUUID().toLowerCase()
      } catch {
        return null
      }
      if (!isEtlExecutionUuid(runId)) return null
      const next = upsertEtlExecutionRecovery(current, {
        pipelineId: pipelineKey,
        runId,
        disposition: 'pending',
      })
      if (!next || !persistEtlExecutionRecovery(browserStorage, next)) return null

      return {
        entries: next,
        value: { runId, disposition: 'pending', isReplay: false },
      }
    })
    if (lockedClaim) return lockedClaim

    // Web Locks 不可用时禁止生成新 ID，但允许从持久层重读并重放已有 ID：
    // 该分支不做读改写，两标签只会得到同一个 RunId。
    if (readyStorageRef.current !== browserStorage) return null
    const result = readEtlExecutionRecoveryResult(browserStorage)
    if (result.status === 'unavailable') {
      markStorageUnavailable(browserStorage)
      return null
    }
    const current = result.entries
    const existing = current.find(item => item.pipelineId === pipelineKey)
    if (!existing) return null
    applyEntries(current, browserStorage)
    return {
      runId: existing.runId,
      disposition: existing.disposition,
      isReplay: true,
    }
  }, [applyEntries, browserStorage, markStorageUnavailable, runLockedMutation])

  const retainRunId = useCallback(async (
    pipelineId: string,
    runId: string,
    disposition: EtlExecutionRecoveryDisposition = 'pending',
  ): Promise<boolean> => {
    if (!isEtlExecutionUuid(pipelineId) || !isEtlExecutionUuid(runId)) return false
    const pipelineKey = pipelineId.toLowerCase()
    const normalizedRunId = runId.toLowerCase()

    return (await runLockedMutation((current) => {
      const existing = current.find(item => item.pipelineId === pipelineKey)
      // 不允许迟到回调覆盖另一标签已经裁定的 RunId，也不允许把 OutcomeUnknown 降级解封。
      if (
        (existing && existing.runId !== normalizedRunId)
        || (existing?.disposition === 'outcome-unknown' && disposition === 'pending')
      ) {
        return { entries: current, value: false }
      }
      if (existing?.runId === normalizedRunId && existing.disposition === disposition) {
        return { entries: current, value: true }
      }

      const next = upsertEtlExecutionRecovery(current, {
        pipelineId: pipelineKey,
        runId: normalizedRunId,
        disposition,
      })
      if (!next) return { entries: current, value: false }
      const persisted = persistEtlExecutionRecovery(browserStorage, next)
      // 写失败时不覆盖 localStorage 的 last-known-good；当前页面仍以内存 next fail closed。
      return { entries: next, value: persisted }
    })) ?? false
  }, [browserStorage, runLockedMutation])

  const clearRunId = useCallback(async (
    pipelineId: string,
    expectedRunId: string,
  ): Promise<EtlExecutionRecoveryClearResult> => {
    if (
      !isEtlExecutionUuid(pipelineId)
      || !isEtlExecutionUuid(expectedRunId)
    ) return 'unavailable'
    const pipelineKey = pipelineId.toLowerCase()
    const normalizedExpected = expectedRunId.toLowerCase()

    return (await runLockedMutation((current) => {
      const existing = current.find(item => item.pipelineId === pipelineKey)
      if (!existing) {
        return { entries: current, value: 'already-absent' as const }
      }
      if (existing.runId !== normalizedExpected) {
        return { entries: current, value: 'superseded' as const }
      }

      const next = removeEtlExecutionRecovery(current, pipelineKey)
      if (!persistEtlExecutionRecovery(browserStorage, next)) {
        return { entries: current, value: 'unavailable' as const }
      }
      return { entries: next, value: 'removed' as const }
    })) ?? 'unavailable'
  }, [browserStorage, runLockedMutation])

  const blockPipeline = useCallback((pipelineId: string, runId: string) => {
    return retainRunId(pipelineId, runId, 'outcome-unknown')
  }, [retainRunId])

  const verifyRun = useCallback(async (
    pipelineId: string,
    runId: string,
  ): Promise<EtlExecutionVerification> => {
    if (readyStorageRef.current !== browserStorage) return 'retain'
    const pipelineKey = pipelineId.toLowerCase()
    const normalizedRunId = runId.toLowerCase()
    const current = recoveryEntriesRef.current.find(item => item.pipelineId === pipelineKey)
    if (current?.runId !== normalizedRunId) return 'retain'

    abortVerification(pipelineKey)
    const request = {
      runId: normalizedRunId,
      storage: browserStorage,
      controller: new AbortController(),
    }
    verificationRequestsRef.current[pipelineKey] = request
    const result = await verifyEtlExecutionRun(
      pipelineKey,
      normalizedRunId,
      request.controller.signal,
    )

    const activeRequest = verificationRequestsRef.current[pipelineKey]
    const latest = recoveryEntriesRef.current.find(item => item.pipelineId === pipelineKey)
    if (
      request.controller.signal.aborted
      || activeRequest !== request
      || readyStorageRef.current !== request.storage
      || request.storage !== browserStorage
      || latest?.runId !== normalizedRunId
    ) {
      return 'retain'
    }

    delete verificationRequestsRef.current[pipelineKey]
    if (result === 'terminal') await clearRunId(pipelineKey, normalizedRunId)
    if (result === 'outcome-unknown') await blockPipeline(pipelineKey, normalizedRunId)
    return result
  }, [abortVerification, blockPipeline, browserStorage, clearRunId])

  // 自动恢复只需要保证同一 storage/pipeline/RunId 至多一个在途 GET；显式 UI 补验仍直接
  // 调用 verifyRun，以便用户动作可以用更新鲜的服务端时点替换旧的自动核验。
  const verifyRunIfIdle = useCallback((pipelineId: string, runId: string) => {
    const pipelineKey = pipelineId.toLowerCase()
    const normalizedRunId = runId.toLowerCase()
    const request = verificationRequestsRef.current[pipelineKey]
    if (request?.storage === browserStorage && request.runId === normalizedRunId) return
    void verifyRun(pipelineKey, normalizedRunId)
  }, [browserStorage, verifyRun])

  // 首次恢复后只读核验每个原 RunId；不轮询、不调用 execute，也不因 404/网络错误清理。
  useEffect(() => {
    if (readyStorage !== browserStorage) return
    const verificationRequests = verificationRequestsRef.current
    for (const entry of recoveryEntriesRef.current) {
      verifyRunIfIdle(entry.pipelineId, entry.runId)
    }

    const capturedStorage = browserStorage
    return () => {
      for (const [pipelineId, request] of Object.entries(verificationRequests)) {
        if (request.storage !== capturedStorage) continue
        delete verificationRequests[pipelineId]
        request.controller.abort()
      }
    }
  }, [browserStorage, readyStorage, verifyRunIfIdle])

  // localStorage 的原生事件负责把另一标签的原子提交刷新进本标签；即使本标签因损坏信封
  // 已 fail closed，也保留当前 principal 的监听，以便合法写入/删除后自动恢复。恢复到 ready
  // 时复用上方 effect 启动一次只读核验，避免在事件回调与 ready effect 中重复 GET。
  useEffect(() => {
    return subscribeEtlExecutionRecovery(browserStorage, () => {
      if (activeStorageRef.current !== browserStorage) return
      if (
        readyStorageRef.current !== browserStorage
        && unavailableStorageRef.current !== browserStorage
      ) return
      const result = readEtlExecutionRecoveryResult(browserStorage)
      if (result.status === 'unavailable') {
        markStorageUnavailable(browserStorage)
        return
      }
      const previousRunIds = readyStorageRef.current === browserStorage
        ? new Map(recoveryEntriesRef.current.map(entry => [entry.pipelineId, entry.runId]))
        : null
      markStorageReady(browserStorage, result.entries)
      // unavailable -> ready 交给上方 ready effect 全量核验一次；ready -> ready 则只补
      // 新增/换 RunId。applyEntries 已负责中止删除或换代项，未变项及 disposition-only
      // 更新保持原在途 GET，重复事件不会制造新请求。
      if (!previousRunIds) return
      for (const entry of result.entries) {
        if (previousRunIds.get(entry.pipelineId) !== entry.runId) {
          verifyRunIfIdle(entry.pipelineId, entry.runId)
        }
      }
    })
  }, [
    browserStorage,
    markStorageReady,
    markStorageUnavailable,
    verifyRunIfIdle,
  ])

  const status: EtlExecutionRecoveryStatus = readyStorage === browserStorage
    ? 'ready'
    : unavailableStorage === browserStorage
      ? 'unavailable'
      : 'loading'

  return {
    status,
    ready: status === 'ready',
    pendingRunIds,
    blockedPipelines,
    getRunId: (pipelineId: string) => pendingRunIdsRef.current[pipelineId.toLowerCase()],
    isBlocked: (pipelineId: string) =>
      blockedPipelinesRef.current[pipelineId.toLowerCase()] === true,
    claimRunId,
    retainRunId,
    clearRunId,
    blockPipeline,
    verifyRun,
  }
}
