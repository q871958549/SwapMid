import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import type { PipelineExecutionDto } from '@/lib/api/types'
import { PipelineExecutionsClient } from './client'

type ActionColumn = {
  id?: string
  cell?: (context: { row: { original: PipelineExecutionDto } }) => ReactNode
}

const mocks = vi.hoisted(() => ({
  fetch: vi.fn(),
  reloadAndRefresh: vi.fn(),
  toast: vi.fn(),
  routerPush: vi.fn(),
  verifyRun: vi.fn(),
  retainRunId: vi.fn(),
  clearRunId: vi.fn(),
  recoveryReady: true,
  recoveryStatus: 'ready' as 'loading' | 'ready' | 'unavailable',
  renderActualOutcomeDialog: false,
  renderDoubleSubmit: false,
}))
const RESOLVE_RESPONSE_UNKNOWN_DESC =
  zhHansMessages.DataIntegration.execution.resolveResponseUnknownDesc
const RECOVERY_CLEANUP_UNAVAILABLE_TITLE =
  zhHansMessages.DataIntegration.common.recoveryCleanupUnavailableTitle
const RECOVERY_STORAGE_UNAVAILABLE_TITLE =
  zhHansMessages.DataIntegration.common.recoveryStorageUnavailableTitle
const RECOVERY_STORAGE_UNAVAILABLE_DESC =
  zhHansMessages.DataIntegration.common.recoveryStorageUnavailableDesc

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mocks.routerPush, refresh: vi.fn() }),
}))
vi.mock('@/components/ui/use-toast', () => ({ toast: mocks.toast }))
vi.mock('@/lib/crud/use-url-list-state', () => ({
  useUrlListState: (initial: Record<string, string>) => [initial, vi.fn()],
}))
vi.mock('@/lib/crud/use-crud-resource', () => ({
  useCrudResource: ({ initial }: { initial: { items: PipelineExecutionDto[]; totalCount: number } }) => ({
    rows: initial.items,
    total: initial.totalCount,
    setPage: vi.fn(),
    reloadAndRefresh: mocks.reloadAndRefresh,
    tableProps: {},
  }),
}))
vi.mock('../pipelines/use-etl-execution-recovery', () => ({
  useEtlExecutionRecovery: () => ({
    ready: mocks.recoveryReady,
    status: mocks.recoveryStatus,
    retainRunId: mocks.retainRunId,
    clearRunId: mocks.clearRunId,
    verifyRun: mocks.verifyRun,
  }),
}))
vi.mock('@/components/data-table/page-header', () => ({
  PageHeader: ({ title }: { title: ReactNode }) => <h1>{title}</h1>,
}))
vi.mock('@/components/data-table/data-table-column-header', () => ({
  DataTableColumnHeader: ({ title }: { title: ReactNode }) => <span>{title}</span>,
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({ columns, data }: { columns: ActionColumn[]; data: PipelineExecutionDto[] }) => {
    const actions = columns.find(column => column.id === 'actions')
    return <div>{data.map(row => <div key={row.id}>{actions?.cell?.({ row: { original: row } })}</div>)}</div>
  },
}))
vi.mock('@/components/ui/dropdown-menu', () => ({
  DropdownMenu: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuTrigger: ({ children }: { children: ReactNode }) => <>{children}</>,
  DropdownMenuContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuItem: ({ children, onClick }: { children: ReactNode; onClick?: () => void }) => (
    <button type="button" onClick={onClick}>{children}</button>
  ),
}))
vi.mock('@/components/ui/dialog', () => ({
  Dialog: ({ open, children }: { open: boolean; children: ReactNode }) => open ? <div>{children}</div> : null,
}))
vi.mock('@/components/ui/scrollable-dialog', () => ({
  ScrollableDialogContent: ({ title, description, children, footer }: {
    title: ReactNode
    description?: ReactNode
    children: ReactNode
    footer?: ReactNode
  }) => <section><h2>{title}</h2>{description}<div>{children}</div>{footer}</section>,
}))
vi.mock('@/components/ui/date-picker', () => ({ DatePicker: () => null }))
vi.mock('@/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}))
vi.mock('./outcome-resolution-dialog', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./outcome-resolution-dialog')>()
  const ActualOutcomeResolutionDialog = actual.OutcomeResolutionDialog
  return {
    ...actual,
    OutcomeResolutionDialog: (props: Parameters<typeof ActualOutcomeResolutionDialog>[0]) => {
      if (mocks.renderActualOutcomeDialog) return <ActualOutcomeResolutionDialog {...props} />
      const { allowConfirmedApplied, onSubmit } = props
      return (
        <div>
          {mocks.renderDoubleSubmit && (
            <button
              type="button"
              onClick={() => {
                const submission = {
                  resolution: '1' as const,
                  processedRows: null,
                  evidenceReference: 'incident:MANUAL-RESOLUTION-0001',
                }
                void onSubmit(submission)
                void onSubmit(submission)
              }}
            >
              同步双提交
            </button>
          )}
          {allowConfirmedApplied && (
            <button
              type="button"
              onClick={() => onSubmit({
                resolution: '0',
                processedRows: 18,
                evidenceReference: 'incident:MANUAL-RESOLUTION-0001',
              })}
            >
              确认已应用
            </button>
          )}
          <button
            type="button"
            onClick={() => onSubmit({
              resolution: '1',
              processedRows: null,
              evidenceReference: 'incident:MANUAL-RESOLUTION-0001',
            })}
          >
            确认未应用
          </button>
        </div>
      )
    },
  }
})

const receiptExecution: PipelineExecutionDto = {
  id: '22222222-2222-4222-8222-222222222222',
  pipelineId: '11111111-1111-4111-8111-111111111111',
  pipelineName: '回执管道',
  status: 5,
  triggerType: 0,
  startedAt: '2026-08-12T08:00:00Z',
  finishedAt: null,
  durationSeconds: null,
  processedRows: null,
  errorCode: 'SwapMid:ETL:015',
  errorMessage: '结果未知',
  correlationId: 'correlation-1',
  exceptionType: null,
  reconciliationStrategy: 'target-receipt-v1',
  receiptVerificationEligible: true,
  creationTime: '2026-08-12T08:00:00Z',
}

const manualExecution: PipelineExecutionDto = {
  ...receiptExecution,
  pipelineName: '人工对账管道',
  reconciliationStrategy: 'manual-v0',
  receiptVerificationEligible: false,
}

function renderClient({
  execution = receiptExecution,
  canResolveOutcome = true,
  canVerifyTargetReceipt = true,
}: {
  execution?: PipelineExecutionDto
  canResolveOutcome?: boolean
  canVerifyTargetReceipt?: boolean
} = {}) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <PipelineExecutionsClient
        initial={{ items: [execution], totalCount: 1 }}
        canResolveOutcome={canResolveOutcome}
        canVerifyTargetReceipt={canVerifyTargetReceipt}
      />
    </NextIntlClientProvider>,
  )
}

function getMachineVerifyEntry() {
  return screen.queryByRole('button', { name: /机器核验.*回执/ })
}

async function submitManualResolution(label: '确认已应用' | '确认未应用') {
  renderClient({ execution: manualExecution })
  fireEvent.click(screen.getByRole('button', { name: /提交对账结论/ }))
  fireEvent.click(screen.getByRole('button', { name: label }))
  await waitFor(() => expect(mocks.fetch).toHaveBeenCalledOnce())
}

beforeEach(() => {
  vi.stubGlobal('fetch', mocks.fetch)
  mocks.fetch.mockReset()
  mocks.reloadAndRefresh.mockReset()
  mocks.toast.mockReset()
  mocks.verifyRun.mockReset()
  mocks.retainRunId.mockReset().mockResolvedValue(true)
  mocks.clearRunId.mockReset().mockResolvedValue('removed')
  mocks.recoveryReady = true
  mocks.recoveryStatus = 'ready'
  mocks.renderActualOutcomeDialog = false
  mocks.renderDoubleSubmit = false
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('PipelineExecutionsClient · Target Receipt v1 对账入口', () => {
  // 这个用例是本文件第一次渲染整页。CI 把 Vitest 限在 2 个 worker，和并行的构建抢 CPU 时，
  // 默认 5 秒会先被吃掉（这次跑了约 8.8 秒才被掐断）。断言本身是同步的。
  it('只有执行具备完整回执身份且当前人有独立权限时显示机器核验', () => {
    renderClient()
    expect(getMachineVerifyEntry()).toBeInTheDocument()
  }, 20_000)

  it('恢复存储初始不可用时机器核验不发 POST，并显示明确失败关闭提示', async () => {
    mocks.recoveryReady = false
    mocks.recoveryStatus = 'unavailable'
    renderClient()

    fireEvent.click(getMachineVerifyEntry()!)

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'destructive',
      title: RECOVERY_STORAGE_UNAVAILABLE_TITLE,
      description: RECOVERY_STORAGE_UNAVAILABLE_DESC,
    }))
    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(mocks.retainRunId).not.toHaveBeenCalled()
    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.reloadAndRefresh).not.toHaveBeenCalled()
  })

  it('恢复存储初始不可用时人工对账持续告警、显示不可用按钮且不提交', () => {
    mocks.recoveryReady = false
    mocks.recoveryStatus = 'unavailable'
    mocks.renderActualOutcomeDialog = true
    renderClient({ execution: manualExecution })

    fireEvent.click(screen.getByRole('button', { name: /提交对账结论/ }))

    expect(screen.getByText(RECOVERY_STORAGE_UNAVAILABLE_DESC)).toBeInTheDocument()
    expect(screen.getAllByText(RECOVERY_STORAGE_UNAVAILABLE_TITLE)).toHaveLength(2)
    const unavailableButton = screen.getByRole('button', { name: RECOVERY_STORAGE_UNAVAILABLE_TITLE })
    expect(unavailableButton).toBeDisabled()
    fireEvent.click(unavailableButton)
    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(mocks.retainRunId).not.toHaveBeenCalled()
  })

  it('恢复存储初始不可用时人工对账 handler 即使被直接触发也保持零副作用', () => {
    mocks.recoveryReady = false
    mocks.recoveryStatus = 'unavailable'
    renderClient({ execution: manualExecution })

    fireEvent.click(screen.getByRole('button', { name: /提交对账结论/ }))
    fireEvent.click(screen.getByRole('button', { name: '确认未应用' }))

    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(mocks.retainRunId).not.toHaveBeenCalled()
    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.reloadAndRefresh).not.toHaveBeenCalled()
  })

  it('机器核验无法建立本机执行保护时不提交请求且准确提示', async () => {
    mocks.retainRunId.mockResolvedValue(false)
    renderClient()

    fireEvent.click(getMachineVerifyEntry()!)

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'destructive',
      title: RECOVERY_STORAGE_UNAVAILABLE_TITLE,
      description: RECOVERY_STORAGE_UNAVAILABLE_DESC,
    }))
    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.reloadAndRefresh).not.toHaveBeenCalled()
  })

  it('人工对账无法建立本机执行保护时不提交请求且准确提示', async () => {
    mocks.retainRunId.mockResolvedValue(false)
    renderClient({ execution: manualExecution })

    fireEvent.click(screen.getByRole('button', { name: /提交对账结论/ }))
    fireEvent.click(screen.getByRole('button', { name: '确认未应用' }))

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'destructive',
      title: RECOVERY_STORAGE_UNAVAILABLE_TITLE,
      description: RECOVERY_STORAGE_UNAVAILABLE_DESC,
    }))
    expect(mocks.fetch).not.toHaveBeenCalled()
    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.reloadAndRefresh).not.toHaveBeenCalled()
  })

  it('人工裁决同一调用栈双提交只执行一次，失败释放锁后允许再次提交', async () => {
    const firstRetain = createDeferred<boolean>()
    const firstRequest = createDeferred<Response>()
    mocks.retainRunId.mockReturnValueOnce(firstRetain.promise).mockResolvedValue(true)
    mocks.fetch
      .mockReturnValueOnce(firstRequest.promise)
      .mockResolvedValueOnce(new Response(JSON.stringify({
        ...manualExecution,
        status: 3,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    mocks.renderDoubleSubmit = true
    renderClient({ execution: manualExecution })

    fireEvent.click(screen.getByRole('button', { name: /提交对账结论/ }))
    fireEvent.click(screen.getByRole('button', { name: '同步双提交' }))
    expect(mocks.retainRunId).toHaveBeenCalledOnce()
    expect(mocks.fetch).not.toHaveBeenCalled()

    firstRetain.resolve(true)
    await waitFor(() => expect(mocks.fetch).toHaveBeenCalledOnce())
    firstRequest.reject(new TypeError('network disconnected'))
    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'destructive',
    })))
    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())

    fireEvent.click(screen.getByRole('button', { name: '确认未应用' }))
    await waitFor(() => expect(mocks.retainRunId).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(mocks.fetch).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledTimes(2))

    expect(mocks.verifyRun).toHaveBeenCalledOnce()
    expect(mocks.clearRunId).toHaveBeenCalledOnce()
    expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }))
  })

  it.each([
    ['无 VerifyTargetReceipt 权限', { canVerifyTargetReceipt: false }],
    ['执行缺少完整稳定身份', { execution: { ...receiptExecution, receiptVerificationEligible: false } }],
  ])('%s 时 fail-closed 隐藏机器核验', (_name, props) => {
    renderClient(props)
    expect(getMachineVerifyEntry()).not.toBeInTheDocument()
  })

  it('receipt-v1 已收敛为终态时不误报核验身份缺失', () => {
    renderClient({
      execution: {
        ...receiptExecution,
        status: 2,
        receiptVerificationEligible: false,
        receiptVerifiedAt: '2026-08-12T08:05:00Z',
      },
    })

    fireEvent.click(screen.getByRole('button', { name: /查看详情/ }))

    expect(screen.queryByText(/缺少完整稳定核验身份/)).not.toBeInTheDocument()
  })

  it('receipt-v1 人工对账不提供 ConfirmedApplied，仅保留确认未应用 break-glass', () => {
    renderClient()

    fireEvent.click(screen.getByRole('button', { name: /提交对账结论/ }))

    expect(screen.queryByText('确认已应用')).not.toBeInTheDocument()
    expect(screen.getByText('确认未应用')).toBeInTheDocument()
  })

  it('机器核验 POST 成功后刷新列表与 Router Cache', async () => {
    mocks.fetch.mockResolvedValue(new Response(JSON.stringify({
      ...receiptExecution,
      status: 2,
      processedRows: 18,
      receiptVerifiedAt: '2026-08-12T08:05:00Z',
      receiptVerificationSource: 'target-receipt-v1',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    renderClient()

    fireEvent.click(getMachineVerifyEntry()!)

    await waitFor(() => expect(mocks.fetch).toHaveBeenCalledWith(
      `/api/proxy/pipeline-executions/${receiptExecution.id}/verify-target-receipt`,
      expect.objectContaining({ method: 'POST' }),
    ))
    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())
    expect(mocks.retainRunId).toHaveBeenCalledWith(
      receiptExecution.pipelineId,
      receiptExecution.id,
      'outcome-unknown',
    )
    expect(mocks.clearRunId).toHaveBeenCalledWith(
      receiptExecution.pipelineId,
      receiptExecution.id,
    )
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }))
  })

  it('机器核验已收敛但本机保护清理不可用时关闭旧详情、只读核验并警告', async () => {
    mocks.clearRunId.mockResolvedValue('unavailable')
    mocks.verifyRun.mockReturnValue(new Promise(() => undefined))
    mocks.fetch.mockResolvedValue(new Response(JSON.stringify({
      ...receiptExecution,
      status: 2,
      receiptVerifiedAt: '2026-08-12T08:05:00Z',
      receiptVerificationSource: 'target-receipt-v1',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    renderClient()
    fireEvent.click(screen.getByRole('button', { name: /查看详情/ }))
    const detail = screen.getByRole('heading', { name: /执行详情/ }).closest('section')!

    fireEvent.click(within(detail).getByRole('button', { name: /机器核验.*回执/ }))

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'warning',
      title: RECOVERY_CLEANUP_UNAVAILABLE_TITLE,
    })))
    expect(mocks.fetch).toHaveBeenCalledOnce()
    expect(mocks.clearRunId).toHaveBeenCalledOnce()
    expect(mocks.verifyRun).toHaveBeenCalledOnce()
    expect(mocks.verifyRun).toHaveBeenCalledWith(receiptExecution.pipelineId, receiptExecution.id)
    expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce()
    expect(screen.queryByRole('heading', { name: /执行详情/ })).not.toBeInTheDocument()
    expect(mocks.toast).not.toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }))
    expect(mocks.toast).not.toHaveBeenCalledWith(expect.objectContaining({ variant: 'destructive' }))
  })

  it('机器核验失败保持 OutcomeUnknown，且给出显式错误提示', async () => {
    mocks.fetch.mockResolvedValue(new Response('target receipt unavailable', { status: 503 }))
    renderClient()

    fireEvent.click(getMachineVerifyEntry()!)

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'destructive',
    })))
    expect(mocks.reloadAndRefresh).not.toHaveBeenCalled()
    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).toHaveBeenCalledWith(
      receiptExecution.pipelineId,
      receiptExecution.id,
    )
  })

  it.each([
    ['确认已应用', 2],
    ['确认未应用', 3],
  ] as const)('%s 只在服务端返回对应终态 %s 时清理 RunId', async (label, status) => {
    mocks.fetch.mockResolvedValue(new Response(JSON.stringify({
      ...manualExecution,
      status,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))

    await submitManualResolution(label)

    expect(mocks.retainRunId).toHaveBeenCalledWith(
      manualExecution.pipelineId,
      manualExecution.id,
      'outcome-unknown',
    )
    expect(mocks.clearRunId).toHaveBeenCalledWith(
      manualExecution.pipelineId,
      manualExecution.id,
    )
    expect(mocks.verifyRun).not.toHaveBeenCalled()
    expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }))
  })

  it('人工裁决已收敛但本机保护清理不可用时关闭旧对账、只读核验并警告', async () => {
    mocks.clearRunId.mockResolvedValue('unavailable')
    mocks.verifyRun.mockRejectedValue(new Error('read-only verification unavailable'))
    mocks.fetch.mockResolvedValue(new Response(JSON.stringify({
      ...manualExecution,
      status: 2,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))

    await submitManualResolution('确认已应用')

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'warning',
      title: RECOVERY_CLEANUP_UNAVAILABLE_TITLE,
    })))
    expect(mocks.fetch).toHaveBeenCalledOnce()
    expect(mocks.clearRunId).toHaveBeenCalledOnce()
    expect(mocks.verifyRun).toHaveBeenCalledOnce()
    expect(mocks.verifyRun).toHaveBeenCalledWith(manualExecution.pipelineId, manualExecution.id)
    await waitFor(() => expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce())
    expect(screen.queryByRole('button', { name: '确认未应用' })).not.toBeInTheDocument()
    expect(mocks.toast).not.toHaveBeenCalledWith(expect.objectContaining({ variant: 'success' }))
    expect(mocks.toast).not.toHaveBeenCalledWith(expect.objectContaining({ variant: 'destructive' }))
  })

  it.each([
    ['确认已应用', 3],
    ['确认未应用', 2],
    ['确认已应用', 4],
    ['确认未应用', 4],
  ] as const)('%s 遇到相反或取消终态 %s 时保留 RunId 并只读核验', async (label, status) => {
    mocks.fetch.mockResolvedValue(new Response(JSON.stringify({
      ...manualExecution,
      status,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))

    await submitManualResolution(label)

    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).toHaveBeenCalledWith(
      manualExecution.pipelineId,
      manualExecution.id,
    )
    expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'destructive',
      description: RESOLVE_RESPONSE_UNKNOWN_DESC,
    }))
  })

  it('人工对账收到畸形 2xx 时保留 RunId 并只读核验', async () => {
    mocks.fetch.mockResolvedValue(new Response('{bad json', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))

    await submitManualResolution('确认已应用')

    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).toHaveBeenCalledWith(
      manualExecution.pipelineId,
      manualExecution.id,
    )
    expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'destructive',
      description: RESOLVE_RESPONSE_UNKNOWN_DESC,
    }))
  })

  it('人工对账网络断线时保留 RunId 并只读核验', async () => {
    mocks.fetch.mockRejectedValue(new TypeError('network disconnected'))

    await submitManualResolution('确认未应用')

    expect(mocks.clearRunId).not.toHaveBeenCalled()
    expect(mocks.verifyRun).toHaveBeenCalledWith(
      manualExecution.pipelineId,
      manualExecution.id,
    )
    expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'destructive',
      description: RESOLVE_RESPONSE_UNKNOWN_DESC,
    }))
  })
})
