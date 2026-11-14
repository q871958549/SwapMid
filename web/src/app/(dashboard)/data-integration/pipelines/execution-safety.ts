export const OUTCOME_UNKNOWN_STATUS = 5

const CURRENT_RUN_OUTCOME_UNKNOWN_CODE = 'SwapMid:ETL:015'
const COORDINATION_STATE_UNKNOWN_CODE = 'SwapMid:ETL:014'
const DURABLE_TARGET_BLOCK_CODES = new Set(['SwapMid:ETL:016', 'SwapMid:ETL:019'])
const AMBIGUOUS_PROXY_STATUSES = new Set([408, 499])

export interface ExecutionApiError {
  code: string | null
  message: string | null
}

export type ExecutionHttpFailureDisposition =
  | 'outcome-unknown'
  | 'target-blocked'
  | 'retain-run-id'
  | 'clear-run-id'

export type ExecutionResultDisposition =
  | 'outcome-unknown'
  | 'succeeded'
  | 'failed'
  | 'in-progress'
  | 'ambiguous'

export function readExecutionApiError(text: string): ExecutionApiError {
  try {
    const body = JSON.parse(text) as {
      code?: string
      message?: string
      error?: { code?: string; message?: string }
    }
    return {
      code: body.error?.code ?? body.code ?? null,
      message: body.error?.message ?? body.message ?? null,
    }
  } catch {
    return { code: null, message: null }
  }
}

/**
 * 只有能证明“本 RunId 未执行”的响应才允许清除 ID。所有 5xx、代理超时/断连，以及
 * 同一 RunId 重放时的 Busy 都保守保留；下次只能重放同一运行，绝不生成第二个 ID。
 */
export function classifyExecutionHttpFailure(
  status: number,
  code: string | null,
  isReplay: boolean,
): ExecutionHttpFailureDisposition {
  if (code === CURRENT_RUN_OUTCOME_UNKNOWN_CODE) return 'outcome-unknown'
  // 014 表示 Running 或终态事务的 COMMIT 结果无法确认；即使这是浏览器首次请求，
  // 后端也可能已经创建执行记录或写完目标库，因此只能保留同一 RunId 查询/重放。
  if (code === COORDINATION_STATE_UNKNOWN_CODE) return 'retain-run-id'
  // 重放表示该 RunId 来自一次响应不明的既有尝试。当前 4xx 只能证明“本次重放”
  // 未执行（例如权限已被撤回），不能证明原尝试没有落库。016/019 在异常的
  // 多 durable-state 场景下也可能与本 RunId 并存，因此重放失败一律保留原 ID。
  if (isReplay) return 'retain-run-id'
  if (code && DURABLE_TARGET_BLOCK_CODES.has(code)) return 'target-blocked'
  if (status >= 500 || AMBIGUOUS_PROXY_STATUSES.has(status)) return 'retain-run-id'
  return 'clear-run-id'
}

export function classifyExecutionResult(status: number): ExecutionResultDisposition {
  if (status === OUTCOME_UNKNOWN_STATUS) return 'outcome-unknown'
  if (status === 2) return 'succeeded'
  if (status === 3 || status === 4) return 'failed'
  if (status === 0 || status === 1) return 'in-progress'
  return 'ambiguous'
}
