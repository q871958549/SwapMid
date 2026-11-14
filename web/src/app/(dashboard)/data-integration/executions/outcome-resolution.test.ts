import { describe, expect, it, vi } from 'vitest'
import {
  EMPTY_OUTCOME_RESOLUTION,
  canSubmitOutcomeResolution,
  clearSettledEtlExecutionRecovery,
  isExplicitOutcomeResolution,
  isExpectedOutcomeResolutionPayload,
  isStructuredEvidenceReference,
} from './outcome-resolution'

const VALID_EVIDENCE_REFERENCE = 'incident:ETL_20260812'
const PIPELINE_ID = '11111111-1111-4111-8111-111111111111'
const RUN_ID = '22222222-2222-4222-8222-222222222222'

describe('执行结果人工对账表单', () => {
  it('默认不选择任何结论且禁止提交', () => {
    expect(EMPTY_OUTCOME_RESOLUTION).toBe('')
    expect(isExplicitOutcomeResolution(EMPTY_OUTCOME_RESOLUTION)).toBe(false)
    expect(canSubmitOutcomeResolution('', VALID_EVIDENCE_REFERENCE, true)).toBe(false)
  })

  it('显式选择确认已应用时仍要求处理行数合法', () => {
    expect(canSubmitOutcomeResolution('0', VALID_EVIDENCE_REFERENCE, true)).toBe(true)
    expect(canSubmitOutcomeResolution('0', VALID_EVIDENCE_REFERENCE, false)).toBe(false)
  })

  it('显式选择确认未应用时不校验不会提交的处理行数', () => {
    expect(canSubmitOutcomeResolution('1', VALID_EVIDENCE_REFERENCE, false)).toBe(true)
  })

  it.each([
    'incident:ABCD1234',
    `incident:${'A'.repeat(128)}`,
    'change:change_20260812',
    'work-order:WO-20260812',
    'dba-evidence:DBA_20260812',
  ])('只接受白名单前缀加 ASCII 不透明编号：%s', (reference) => {
    expect(isStructuredEvidenceReference(reference)).toBe(true)
    expect(canSubmitOutcomeResolution('1', reference, true)).toBe(true)
  })

  it.each([
    '已查询目标库确认未写入',
    'https://incident.example/ETL-20260812',
    'INCIDENT:ABCD1234',
    ' incident:ABCD1234',
    'incident:ABCD1234 ',
    'incident:ABC1234',
    'incident:-ABCD1234',
    'incident:ABCD1234-',
    'incident:ABCD:1234',
    'incident:事故编号12345678',
    `incident:${'A'.repeat(129)}`,
  ])('自由文本、URL、空白、非 ASCII 或越界编号必须 fail-closed：%s', (reference) => {
    expect(isStructuredEvidenceReference(reference)).toBe(false)
    expect(canSubmitOutcomeResolution('1', reference, true)).toBe(false)
  })

  it.each([
    ['0', 2],
    ['1', 3],
  ] as const)('结论 %s 只接受对应的精确终态 %s', (resolution, status) => {
    expect(isExpectedOutcomeResolutionPayload(PIPELINE_ID, RUN_ID, resolution, {
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status,
    })).toBe(true)
  })

  it.each([
    ['0', 3],
    ['1', 2],
    ['0', 4],
    ['1', 4],
  ] as const)('结论 %s 拒绝相反或取消终态 %s', (resolution, status) => {
    expect(isExpectedOutcomeResolutionPayload(PIPELINE_ID, RUN_ID, resolution, {
      id: RUN_ID,
      pipelineId: PIPELINE_ID,
      status,
    })).toBe(false)
  })

  it.each([
    null,
    { id: RUN_ID, pipelineId: PIPELINE_ID, status: '2' },
    { id: '33333333-3333-4333-8333-333333333333', pipelineId: PIPELINE_ID, status: 2 },
    { id: RUN_ID, pipelineId: '33333333-3333-4333-8333-333333333333', status: 2 },
  ])('畸形或错配 2xx payload 一律不确认：%j', (payload) => {
    expect(isExpectedOutcomeResolutionPayload(PIPELINE_ID, RUN_ID, '0', payload)).toBe(false)
  })

  it.each(['removed', 'already-absent', 'superseded'] as const)(
    '本机保护清理结果 %s 可安全继续，且不追加只读核验',
    async (result) => {
      const clearRunId = vi.fn().mockResolvedValue(result)
      const verifyRun = vi.fn()

      await expect(clearSettledEtlExecutionRecovery(
        PIPELINE_ID,
        RUN_ID,
        clearRunId,
        verifyRun,
      )).resolves.toBe(result)
      expect(clearRunId).toHaveBeenCalledWith(PIPELINE_ID, RUN_ID)
      expect(verifyRun).not.toHaveBeenCalled()
    },
  )

  it('本机保护清理不可用时仅追加一次原 RunId 只读核验', async () => {
    const clearRunId = vi.fn().mockResolvedValue('unavailable')
    const verifyRun = vi.fn()

    await expect(clearSettledEtlExecutionRecovery(
      PIPELINE_ID,
      RUN_ID,
      clearRunId,
      verifyRun,
    )).resolves.toBe('unavailable')
    expect(verifyRun).toHaveBeenCalledOnce()
    expect(verifyRun).toHaveBeenCalledWith(PIPELINE_ID, RUN_ID)
  })

  it('只读核验异常时仍返回 unavailable，且不重复核验或伪装成服务端失败', async () => {
    const clearRunId = vi.fn().mockResolvedValue('unavailable')
    const verifyRun = vi.fn().mockRejectedValue(new Error('read-only verification unavailable'))

    await expect(clearSettledEtlExecutionRecovery(
      PIPELINE_ID,
      RUN_ID,
      clearRunId,
      verifyRun,
    )).resolves.toBe('unavailable')
    expect(verifyRun).toHaveBeenCalledOnce()
  })

  it('只读核验长期未返回时不阻塞 unavailable 结果与后续 UI 收口', async () => {
    const clearRunId = vi.fn().mockResolvedValue('unavailable')
    const verifyRun = vi.fn().mockReturnValue(new Promise(() => undefined))

    await expect(clearSettledEtlExecutionRecovery(
      PIPELINE_ID,
      RUN_ID,
      clearRunId,
      verifyRun,
    )).resolves.toBe('unavailable')
    expect(verifyRun).toHaveBeenCalledOnce()
  })
})
