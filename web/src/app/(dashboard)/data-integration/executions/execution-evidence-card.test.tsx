import { NextIntlClientProvider } from 'next-intl'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { type PipelineExecutionDto } from '@/lib/api/types'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import {
  ExecutionEvidenceCard,
  getExecutionEvidenceCompleteness,
} from './execution-evidence-card'

const completeExecution: PipelineExecutionDto = {
  id: '22222222-2222-4222-8222-222222222222',
  pipelineId: '11111111-1111-4111-8111-111111111111',
  pipelineName: '同步管道',
  status: 5,
  triggerType: 0,
  startedAt: '2026-08-05T10:00:00Z',
  finishedAt: null,
  durationSeconds: null,
  processedRows: null,
  errorCode: 'SwapMid:ETL:015',
  errorMessage: '结果未知',
  correlationId: 'correlation-1',
  exceptionType: null,
  reconciliationStrategy: 'manual-v0',
  receiptVerificationEligible: false,
  targetDataSourceId: '33333333-3333-4333-8333-333333333333',
  targetDataSourceName: '目标库',
  targetProvider: 'sqlserver',
  targetEndpoint: 'db-primary.internal:1433',
  targetDatabase: 'factory',
  targetTable: 'dbo.output',
  targetConfigVersion: 'target-v7',
  targetResourceGroup: 'factory-primary',
  sourceDataSourceId: '44444444-4444-4444-8444-444444444444',
  sourceDataSourceName: '源库',
  sourceConfigVersion: 'source-v3',
  writeMode: 'truncate',
  sourceQuerySha256: 'a'.repeat(64),
  creationTime: '2026-08-05T10:00:00Z',
}

function withoutEvidence(execution: PipelineExecutionDto): PipelineExecutionDto {
  return {
    ...execution,
    targetDataSourceId: null,
    targetDataSourceName: null,
    targetProvider: null,
    targetEndpoint: null,
    targetDatabase: null,
    targetTable: null,
    targetConfigVersion: null,
    targetResourceGroup: null,
    sourceDataSourceId: null,
    sourceDataSourceName: null,
    sourceConfigVersion: null,
    writeMode: null,
    sourceQuerySha256: null,
  }
}

function renderCard(execution: PipelineExecutionDto) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <ExecutionEvidenceCard execution={execution} />
    </NextIntlClientProvider>,
  )
}

describe('OutcomeUnknown 执行证据卡', () => {
  it('完整快照按目标、源配置与写入查询证据分组展示', () => {
    expect(getExecutionEvidenceCompleteness(completeExecution)).toBe('complete')
    renderCard(completeExecution)

    expect(screen.getByText('目标身份')).toBeInTheDocument()
    expect(screen.getByText('源配置证据')).toBeInTheDocument()
    expect(screen.getByText('写入与查询证据')).toBeInTheDocument()
    expect(screen.getByText('factory-primary')).toBeInTheDocument()
    expect(screen.getByText('清空后写入（truncate）')).toBeInTheDocument()
    expect(screen.getByText('a'.repeat(64))).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('部分字段缺失时显式 fail-closed，且逐字段禁止用当前配置补齐', () => {
    const incomplete = { ...completeExecution, sourceQuerySha256: null }
    expect(getExecutionEvidenceCompleteness(incomplete)).toBe('incomplete')
    renderCard(incomplete)

    expect(screen.getByRole('alert')).toHaveTextContent('历史目标快照不完整')
    expect(screen.getByText('缺失（禁止用当前配置补齐）')).toBeInTheDocument()
  })

  it('历史记录完全无证据快照时只提示查日志或配置备份', () => {
    const missing = withoutEvidence(completeExecution)
    expect(getExecutionEvidenceCompleteness(missing)).toBe('missing')
    renderCard(missing)

    expect(screen.getByRole('alert')).toHaveTextContent('历史记录无快照，先查运行日志/配置备份')
    expect(screen.queryByText('目标身份')).not.toBeInTheDocument()
  })
})
