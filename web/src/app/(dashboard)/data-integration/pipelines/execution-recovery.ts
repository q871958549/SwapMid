'use client'

import {
  authenticatedBrowserStoragePolicies,
  classifyBrowserStorageArea,
  type AuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'

export const MAX_ETL_EXECUTION_RECOVERY_ENTRIES = 32
export const ETL_EXECUTION_RECOVERY_POLICY =
  authenticatedBrowserStoragePolicies.etlExecutionRecovery

export type EtlExecutionRecoveryDisposition = 'pending' | 'outcome-unknown'
export type EtlExecutionVerification = 'terminal' | 'outcome-unknown' | 'retain'

export interface EtlExecutionRecoveryEntry {
  pipelineId: string
  runId: string
  disposition: EtlExecutionRecoveryDisposition
}

export type EtlExecutionRecoveryLockResult<T> =
  | { acquired: true; value: T }
  | { acquired: false }

interface EtlExecutionRunPayload {
  id: string
  pipelineId: string
  status: number
}

interface EtlExecutionRecoveryState {
  entries: EtlExecutionRecoveryEntry[]
}

export type EtlExecutionRecoveryReadResult =
  | { status: 'available'; entries: EtlExecutionRecoveryEntry[] }
  | { status: 'unavailable' }

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_UUID = '00000000-0000-0000-0000-000000000000'

export function isEtlExecutionUuid(value: unknown): value is string {
  return typeof value === 'string'
    && UUID_PATTERN.test(value)
    && value.toLowerCase() !== EMPTY_UUID
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === expected.length && keys.every(key => expected.includes(key))
}

export function isEtlExecutionRecoveryEntry(
  value: unknown,
): value is EtlExecutionRecoveryEntry {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const candidate = value as Record<string, unknown>
  return hasExactKeys(candidate, ['pipelineId', 'runId', 'disposition'])
    && isEtlExecutionUuid(candidate.pipelineId)
    && isEtlExecutionUuid(candidate.runId)
    && (candidate.disposition === 'pending' || candidate.disposition === 'outcome-unknown')
}

function normalizeEtlExecutionRecoveryEntry(
  entry: EtlExecutionRecoveryEntry,
): EtlExecutionRecoveryEntry {
  return {
    pipelineId: entry.pipelineId.toLowerCase(),
    runId: entry.runId.toLowerCase(),
    disposition: entry.disposition,
  }
}

export function isEtlExecutionRecoveryState(
  value: unknown,
): value is EtlExecutionRecoveryState {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const candidate = value as Record<string, unknown>
  if (!hasExactKeys(candidate, ['entries']) || !Array.isArray(candidate.entries)) return false
  if (candidate.entries.length > MAX_ETL_EXECUTION_RECOVERY_ENTRIES) return false

  const pipelineIds = new Set<string>()
  const runIds = new Set<string>()
  for (const entry of candidate.entries) {
    if (!isEtlExecutionRecoveryEntry(entry)) return false
    const normalizedPipelineId = entry.pipelineId.toLowerCase()
    const normalizedRunId = entry.runId.toLowerCase()
    if (pipelineIds.has(normalizedPipelineId) || runIds.has(normalizedRunId)) return false
    pipelineIds.add(normalizedPipelineId)
    runIds.add(normalizedRunId)
  }
  return true
}

/** @deprecated 仅供非安全裁决的 best-effort 兼容读取；执行保护必须使用 readEtlExecutionRecoveryResult。 */
export function readEtlExecutionRecovery(
  storage: AuthenticatedBrowserStorage,
): EtlExecutionRecoveryEntry[] {
  const result = readEtlExecutionRecoveryResult(storage)
  return result.status === 'available' ? result.entries : []
}

/** 安全读路径必须区分“确实没有恢复项”与 Storage/信封不可判定。 */
export function readEtlExecutionRecoveryResult(
  storage: AuthenticatedBrowserStorage,
): EtlExecutionRecoveryReadResult {
  const result = storage.readResult(
    ETL_EXECUTION_RECOVERY_POLICY,
    isEtlExecutionRecoveryState,
  )
  if (result.status === 'absent') return { status: 'available', entries: [] }
  if (result.status !== 'found') return { status: 'unavailable' }
  return {
    status: 'available',
    entries: result.value.entries.map(normalizeEtlExecutionRecoveryEntry),
  }
}

/** 写失败时 browser-storage-policy 不覆盖旧 envelope；调用方同时保留内存状态。 */
export function persistEtlExecutionRecovery(
  storage: AuthenticatedBrowserStorage,
  entries: readonly EtlExecutionRecoveryEntry[],
): boolean {
  if (!isEtlExecutionRecoveryState({ entries })) return false
  const state: EtlExecutionRecoveryState = {
    entries: entries.map(normalizeEtlExecutionRecoveryEntry),
  }
  return storage.write(ETL_EXECUTION_RECOVERY_POLICY, state)
}

function getLockManager(): LockManager | null {
  if (typeof navigator === 'undefined') return null
  try {
    return navigator.locks && typeof navigator.locks.request === 'function'
      ? navigator.locks
      : null
  } catch {
    return null
  }
}

/**
 * ETL RunId 是防重放正确性状态。同一 principal 的所有标签页必须在同一把
 * Web Lock 下重读 localStorage 后再做聚合更新；锁不可用时返回 acquired=false，
 * 调用方对“新执行”必须 fail closed，不得回落到可竞态的读改写。
 */
export async function withEtlExecutionRecoveryLock<T>(
  storage: AuthenticatedBrowserStorage,
  operation: () => T,
): Promise<EtlExecutionRecoveryLockResult<T>> {
  const locks = getLockManager()
  if (!locks) return { acquired: false }

  let callbackInvoked = false
  let value: T | undefined
  try {
    await locks.request(
      storage.key(ETL_EXECUTION_RECOVERY_POLICY),
      { mode: 'exclusive' },
      (lock) => {
        if (!lock) return
        callbackInvoked = true
        value = operation()
      },
    )
  } catch {
    return { acquired: false }
  }

  return callbackInvoked
    ? { acquired: true, value: value as T }
    : { acquired: false }
}

/** 原生 storage 事件只投递到其他文档；目标键写删与 localStorage.clear 都刷新当前 principal 快照。 */
export function subscribeEtlExecutionRecovery(
  storage: AuthenticatedBrowserStorage,
  callback: () => void,
): () => void {
  if (typeof window === 'undefined') return () => undefined
  const storageKey = storage.key(ETL_EXECUTION_RECOVERY_POLICY)
  const handleStorage = (event: StorageEvent) => {
    if (event.key !== null && event.key !== storageKey) return
    const areaMatch = classifyBrowserStorageArea(
      ETL_EXECUTION_RECOVERY_POLICY.area,
      event.storageArea,
    )
    if (areaMatch === 'mismatch') return
    // Storage getter 失败时仍通知严格读取，由调用方降为 unavailable 并保留 LKG。
    callback()
  }
  window.addEventListener('storage', handleStorage)
  return () => window.removeEventListener('storage', handleStorage)
}

/** 返回 null 表示达到条数上限或输入非法；安全策略是不启动新的 ETL。 */
export function upsertEtlExecutionRecovery(
  current: readonly EtlExecutionRecoveryEntry[],
  entry: EtlExecutionRecoveryEntry,
): EtlExecutionRecoveryEntry[] | null {
  if (
    !isEtlExecutionRecoveryEntry(entry)
    || !isEtlExecutionRecoveryState({ entries: current })
  ) return null

  const normalizedEntry = normalizeEtlExecutionRecoveryEntry(entry)
  const normalizedCurrent = current.map(normalizeEtlExecutionRecoveryEntry)
  const existingIndex = normalizedCurrent.findIndex(item =>
    item.pipelineId === normalizedEntry.pipelineId)
  if (
    existingIndex < 0
    && normalizedCurrent.length >= MAX_ETL_EXECUTION_RECOVERY_ENTRIES
  ) return null

  const next = normalizedCurrent.filter((_, index) => index !== existingIndex)
  next.push(normalizedEntry)
  next.sort((left, right) => {
    if (left.pipelineId < right.pipelineId) return -1
    if (left.pipelineId > right.pipelineId) return 1
    return 0
  })
  return isEtlExecutionRecoveryState({ entries: next }) ? next : null
}

export function removeEtlExecutionRecovery(
  current: readonly EtlExecutionRecoveryEntry[],
  pipelineId: string,
): EtlExecutionRecoveryEntry[] {
  if (
    !isEtlExecutionUuid(pipelineId)
    || !isEtlExecutionRecoveryState({ entries: current })
  ) return [...current]
  const normalizedPipelineId = pipelineId.toLowerCase()
  return current
    .map(normalizeEtlExecutionRecoveryEntry)
    .filter(entry => entry.pipelineId !== normalizedPipelineId)
}

export function isEtlExecutionRunPayload(
  pipelineId: string,
  runId: string,
  value: unknown,
): value is EtlExecutionRunPayload {
  if (!isEtlExecutionUuid(pipelineId) || !isEtlExecutionUuid(runId)) return false
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  const body = value as Record<string, unknown>
  return isEtlExecutionUuid(body.id)
    && isEtlExecutionUuid(body.pipelineId)
    && body.id.toLowerCase() === runId.toLowerCase()
    && body.pipelineId.toLowerCase() === pipelineId.toLowerCase()
    && typeof body.status === 'number'
    && Number.isInteger(body.status)
}

export function classifyEtlExecutionPayload(
  pipelineId: string,
  runId: string,
  value: unknown,
): EtlExecutionVerification {
  if (!isEtlExecutionRunPayload(pipelineId, runId, value)) return 'retain'
  if (value.status === 2 || value.status === 3 || value.status === 4) return 'terminal'
  if (value.status === 5) return 'outcome-unknown'
  return 'retain'
}

/**
 * 只读核验原 RunId，绝不调用 execute。404、网络错误、畸形响应、Pending/Running 均保守保留；
 * 只有明确的 Succeeded/Failed/Cancelled 才允许调用方清理恢复项。
 */
export async function verifyEtlExecutionRun(
  pipelineId: string,
  runId: string,
  signal?: AbortSignal,
): Promise<EtlExecutionVerification> {
  if (!isEtlExecutionUuid(pipelineId) || !isEtlExecutionUuid(runId)) return 'retain'
  const normalizedPipelineId = pipelineId.toLowerCase()
  const normalizedRunId = runId.toLowerCase()

  try {
    const response = await fetch(`/api/proxy/pipeline-executions/${normalizedRunId}`, {
      method: 'GET',
      cache: 'no-store',
      signal,
    })
    if (!response.ok) return 'retain'
    const body: unknown = await response.json()
    return classifyEtlExecutionPayload(normalizedPipelineId, normalizedRunId, body)
  } catch {
    return 'retain'
  }
}
