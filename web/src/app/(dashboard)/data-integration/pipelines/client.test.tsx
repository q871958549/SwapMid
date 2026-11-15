import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { DataPipelinesClient, type DataPipeline } from './client'
import type { EtlExecutionRecoveryClearResult } from './use-etl-execution-recovery'

type ActionColumn = {
  id?: string
  cell?: (context: { row: { original: DataPipeline } }) => ReactNode
}

const PIPELINE_ID = '11111111-1111-4111-8111-111111111111'
const RUN_ID = '22222222-2222-4222-8222-222222222222'

const mocks = vi.hoisted(() => ({
  recoveryStatus: 'ready' as 'loading' | 'ready' | 'unavailable',
  fetch: vi.fn(),
  reloadAndRefresh: vi.fn(),
  toast: vi.fn(),
  claimRunId: vi.fn(),
  retainRunId: vi.fn(),
  clearRunId: vi.fn(),
  blockPipeline: vi.fn(),
  verifyRun: vi.fn(),
}))

vi.mock('@/components/ui/use-toast', () => ({ toast: mocks.toast }))
vi.mock('@/lib/crud/use-crud-resource', () => ({
  useCrudResource: ({ initial }: { initial: { items: DataPipeline[]; totalCount: number } }) => ({
    rows: initial.items,
    total: initial.totalCount,
    reload: vi.fn(),
    reloadAndRefresh: mocks.reloadAndRefresh,
    tableProps: {},
  }),
}))
vi.mock('@/lib/crud/use-crud-form', () => ({
  useCrudForm: () => ({
    form: {
      name: '', description: '', sourceId: '', targetId: '',
      sourceQuery: '', targetTable: '', mode: 'append', transformConfigValid: true,
      scheduleCron: '', scheduleEnabled: false,
    },
    openEdit: vi.fn(),
    openCreate: vi.fn(),
    askDelete: vi.fn(),
    set: vi.fn(),
  }),
}))
vi.mock('./use-etl-execution-recovery', () => ({
  useEtlExecutionRecovery: () => ({
    status: mocks.recoveryStatus,
    ready: mocks.recoveryStatus === 'ready',
    pendingRunIds: {},
    blockedPipelines: {},
    isBlocked: () => false,
    claimRunId: mocks.claimRunId,
    retainRunId: mocks.retainRunId,
    clearRunId: mocks.clearRunId,
    blockPipeline: mocks.blockPipeline,
    verifyRun: mocks.verifyRun,
  }),
}))
vi.mock('@/components/data-table/column-factories', () => ({
  actionsColumn: ({ extraItems }: {
    extraItems: (row: DataPipeline) => ReactNode
  }) => ({
    id: 'actions',
    cell: ({ row }: { row: { original: DataPipeline } }) => extraItems(row.original),
  }),
  // 名称列改走 primaryColumn 后，列数组在渲染期就会调用它。本文件只断言操作列。
  primaryColumn: () => ({ id: 'name' }),
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({ columns, data }: { columns: ActionColumn[]; data: DataPipeline[] }) => {
    const actions = columns.find(column => column.id === 'actions')
    return <>{data.map(row => <div key={row.id}>{actions?.cell?.({ row: { original: row } })}</div>)}</>
  },
}))
vi.mock('@/components/data-table/data-table-column-header', () => ({
  DataTableColumnHeader: ({ title }: { title: ReactNode }) => <>{title}</>,
}))
vi.mock('@/components/data-table/page-header', () => ({ PageHeader: () => null }))
vi.mock('@/components/data-table/create-button', () => ({ CreateButton: () => null }))
vi.mock('@/components/data-table/crud-form-dialog', () => ({ CrudFormDialog: () => null }))
vi.mock('@/components/data-table/delete-confirm', () => ({ DeleteConfirm: () => null }))
vi.mock('@/components/ui/dropdown-menu', () => ({
  DropdownMenuItem: ({ children, onClick, disabled }: {
    children: ReactNode
    onClick?: () => void
    disabled?: boolean
  }) => <button type="button" onClick={onClick} disabled={disabled}>{children}</button>,
}))

const pipeline: DataPipeline = {
  id: PIPELINE_ID,
  name: '回执管道',
  description: null,
  sourceId: '33333333-3333-4333-8333-333333333333',
  sourceName: '源库',
  targetId: '44444444-4444-4444-8444-444444444444',
  targetName: '目标库',
  transformConfig: null,
  scheduleCron: null,
  scheduleEnabled: false,
  status: 1,
  isEnabled: true,
  lastRunAt: null,
  lastRunStatus: null,
  creationTime: '2026-08-13T00:00:00Z',
}

function renderClient(overrides?: Partial<DataPipeline>, props?: { canExecute?: boolean; canUpdate?: boolean }) {
  const row = { ...pipeline, ...overrides }
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <DataPipelinesClient
        data={{ items: [row], totalCount: 1 }}
        sources={[]}
        canExecute={props?.canExecute ?? true}
        canUpdate={props?.canUpdate ?? false}
      />
    </NextIntlClientProvider>,
  )
}

function terminalResponse(status: 2 | 3) {
  return new Response(JSON.stringify({
    id: RUN_ID,
    pipelineId: PIPELINE_ID,
    status,
    processedRows: status === 2 ? 12 : null,
    errorMessage: status === 3 ? '目标写入失败' : null,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const executeUrl = `/api/proxy/data-pipelines/${PIPELINE_ID}/execute?runId=${RUN_ID}`
const pipelineMessages = zhHansMessages.DataIntegration.pipeline
const cleanupMessages = zhHansMessages.DataIntegration.common
const safeClearResults = [
  'removed',
  'already-absent',
  'superseded',
] as const satisfies readonly EtlExecutionRecoveryClearResult[]

const outcomeCases = [
  {
    name: '执行成功',
    response: () => terminalResponse(2),
    outcomeSummary: pipelineMessages.executeSuccessDesc.replace('{count}', '12'),
    expectedToast: {
      variant: 'success',
      title: pipelineMessages.executeSuccessDesc.replace('{count}', '12'),
      description: undefined,
    },
  },
  {
    name: '执行明确失败',
    response: () => terminalResponse(3),
    outcomeSummary: pipelineMessages.executeFailedDesc.replace('{message}', '目标写入失败'),
    expectedToast: {
      variant: 'destructive',
      title: pipelineMessages.executeFailedDesc.replace('{message}', '目标写入失败'),
      description: undefined,
    },
  },
  {
    name: '确定性 HTTP 失败',
    response: () => new Response(JSON.stringify({
      error: { code: 'SwapMid:ETL:001', message: '请求无效' },
    }), { status: 400 }),
    outcomeSummary: '请求无效',
    expectedToast: {
      variant: 'destructive',
      title: pipelineMessages.executeFailedFallback,
      description: '请求无效',
    },
  },
  {
    name: '目标已有未终结执行',
    response: () => new Response(JSON.stringify({
      error: { code: 'SwapMid:ETL:016', message: '目标被占用' },
    }), { status: 409 }),
    outcomeSummary: pipelineMessages.executeTargetBlockedDesc,
    expectedToast: {
      variant: 'destructive',
      title: pipelineMessages.executeTargetBlockedTitle,
      description: pipelineMessages.executeTargetBlockedDesc,
    },
  },
] as const

beforeEach(() => {
  mocks.recoveryStatus = 'ready'
  vi.stubGlobal('fetch', mocks.fetch)
  mocks.fetch.mockReset()
  mocks.reloadAndRefresh.mockReset().mockResolvedValue(undefined)
  mocks.toast.mockReset()
  mocks.claimRunId.mockReset().mockResolvedValue({
    runId: RUN_ID,
    disposition: 'pending',
    isReplay: false,
  })
  mocks.retainRunId.mockReset().mockResolvedValue(true)
  mocks.clearRunId.mockReset().mockResolvedValue('removed')
  mocks.blockPipeline.mockReset().mockResolvedValue(true)
  mocks.verifyRun.mockReset().mockResolvedValue('retain')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DataPipelinesClient · 恢复保护清理结果', () => {
  it('恢复存储 unavailable 时明确显示失败关闭状态，且不会调用执行 POST', () => {
    mocks.recoveryStatus = 'unavailable'
    renderClient()

    expect(screen.getByRole('button', {
      name: cleanupMessages.recoveryStorageUnavailableTitle,
    })).toBeDisabled()
    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(mocks.claimRunId).not.toHaveBeenCalled()
  })

  it.each(outcomeCases)(
    '$name 后本机清理 unavailable 时只警告并只读核验，不显示普通业务反馈',
    async ({ response, outcomeSummary }) => {
      mocks.fetch.mockResolvedValue(response())
      mocks.clearRunId.mockResolvedValue('unavailable')
      mocks.verifyRun.mockRejectedValue(new Error('read-only verification unavailable'))
      renderClient()

      fireEvent.click(screen.getByRole('button', { name: '执行' }))

      await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())
      expect(mocks.fetch).toHaveBeenCalledOnce()
      expect(mocks.fetch).toHaveBeenCalledWith(executeUrl, { method: 'POST' })
      expect(mocks.clearRunId).toHaveBeenCalledOnce()
      expect(mocks.clearRunId).toHaveBeenCalledWith(PIPELINE_ID, RUN_ID)
      expect(mocks.verifyRun).toHaveBeenCalledOnce()
      expect(mocks.verifyRun).toHaveBeenCalledWith(PIPELINE_ID, RUN_ID)
      expect(mocks.toast).toHaveBeenCalledOnce()
      expect(mocks.toast).toHaveBeenCalledWith({
        variant: 'warning',
        title: cleanupMessages.recoveryCleanupUnavailableTitle,
        description: `${outcomeSummary} ${cleanupMessages.recoveryCleanupUnavailableDesc}`,
      })
    },
  )

  it.each(outcomeCases.flatMap(outcome => (
    safeClearResults.map(clearResult => ({
      ...outcome,
      clearResult,
    }))
  )))('$name + $clearResult 视为安全清理结果并保留对应业务反馈', async ({
    response,
    clearResult,
    expectedToast,
  }) => {
    mocks.fetch.mockResolvedValue(response())
    mocks.clearRunId.mockResolvedValue(clearResult)
    renderClient()

    fireEvent.click(screen.getByRole('button', { name: '执行' }))

    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())
    expect(mocks.fetch).toHaveBeenCalledOnce()
    expect(mocks.fetch).toHaveBeenCalledWith(executeUrl, { method: 'POST' })
    expect(mocks.clearRunId).toHaveBeenCalledOnce()
    expect(mocks.clearRunId).toHaveBeenCalledWith(PIPELINE_ID, RUN_ID)
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.toast).toHaveBeenCalledOnce()
    expect(mocks.toast).toHaveBeenCalledWith(expectedToast)
  })
})

describe('DataPipelinesClient · 生命周期激活与暂停', () => {
  it('草稿管道显示激活操作且在激活前禁用执行', async () => {
    mocks.fetch.mockResolvedValue(new Response(null, { status: 204 }))
    renderClient({ status: 0, isEnabled: false }, { canExecute: true, canUpdate: true })

    expect(screen.getByRole('button', { name: '激活后可执行' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: '激活' }))

    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())
    expect(mocks.fetch).toHaveBeenCalledWith(
      `/api/proxy/data-pipelines/${PIPELINE_ID}/activate`,
      { method: 'PUT' },
    )
    expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'success',
      title: pipelineMessages.activateSuccessTitle,
      description: pipeline.name,
    })
  })

  it('启用管道支持一键暂停并刷新列表', async () => {
    mocks.fetch.mockResolvedValue(new Response(null, { status: 204 }))
    renderClient({ status: 1 }, { canExecute: true, canUpdate: true })

    fireEvent.click(screen.getByRole('button', { name: '暂停' }))

    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())
    expect(mocks.fetch).toHaveBeenCalledWith(
      `/api/proxy/data-pipelines/${PIPELINE_ID}/pause`,
      { method: 'PUT' },
    )
    expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'success',
      title: pipelineMessages.pauseSuccessTitle,
      description: pipeline.name,
    })
  })
})
