import { isEtlExecutionRunPayload } from '../pipelines/execution-recovery'
import type { EtlExecutionRecoveryClearResult } from '../pipelines/use-etl-execution-recovery'

export type OutcomeResolution = '' | '0' | '1'

export const EMPTY_OUTCOME_RESOLUTION: OutcomeResolution = ''
export const MAX_EVIDENCE_REFERENCE_LENGTH = 141
const MIN_EVIDENCE_OPAQUE_ID_LENGTH = 8
const MAX_EVIDENCE_OPAQUE_ID_LENGTH = 128
const EVIDENCE_PREFIXES = [
  'incident:',
  'change:',
  'work-order:',
  'dba-evidence:',
] as const

export function isExplicitOutcomeResolution(
  resolution: OutcomeResolution,
): resolution is Exclude<OutcomeResolution, ''> {
  return resolution === '0' || resolution === '1'
}

/** 与后端 NormalizeEvidenceReference 保持同构，避免把自由文本或秘密提交到审计边界。 */
export function isStructuredEvidenceReference(value: string): boolean {
  const prefix = EVIDENCE_PREFIXES.find(candidate => value.startsWith(candidate))
  if (!prefix) return false

  const opaqueId = value.slice(prefix.length)
  if (
    opaqueId.length < MIN_EVIDENCE_OPAQUE_ID_LENGTH
    || opaqueId.length > MAX_EVIDENCE_OPAQUE_ID_LENGTH
  ) return false

  return /^[A-Za-z0-9][A-Za-z0-9_-]*[A-Za-z0-9]$/.test(opaqueId)
}

export function canSubmitOutcomeResolution(
  resolution: OutcomeResolution,
  evidenceReference: string,
  processedRowsValid: boolean,
): boolean {
  return isExplicitOutcomeResolution(resolution)
    && isStructuredEvidenceReference(evidenceReference)
    && (resolution === '1' || processedRowsValid)
}

/**
 * 人工对账的 2xx 不能只证明“已经终结”，还必须证明终态与提交结论一致：
 * ConfirmedApplied(0) 只能收敛为 Succeeded(2)，ConfirmedNotApplied(1) 只能收敛为 Failed(3)。
 */
export function isExpectedOutcomeResolutionPayload(
  pipelineId: string,
  runId: string,
  resolution: Exclude<OutcomeResolution, ''>,
  value: unknown,
): boolean {
  if (!isEtlExecutionRunPayload(pipelineId, runId, value)) return false
  return value.status === (resolution === '0' ? 2 : 3)
}

/**
 * 服务端已收敛后清理本机执行保护；清理不可用时只读核验原 RunId，绝不重放写请求。
 */
export async function clearSettledEtlExecutionRecovery(
  pipelineId: string,
  runId: string,
  clearRunId: (pipelineId: string, expectedRunId: string) => Promise<EtlExecutionRecoveryClearResult>,
  verifyRun: (pipelineId: string, runId: string) => Promise<unknown>,
): Promise<EtlExecutionRecoveryClearResult> {
  const result = await clearRunId(pipelineId, runId)
  if (result === 'unavailable') {
    try {
      void verifyRun(pipelineId, runId).catch(() => undefined)
    } catch {
      // 服务端终态已经由写响应确认；只读补验异常不能把它改写成“提交失败”，
      // 本机恢复项仍保留并保持 fail closed，用户会收到专用清理告警。
    }
  }
  return result
}
