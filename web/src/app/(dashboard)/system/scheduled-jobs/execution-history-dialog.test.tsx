import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render as rtlRender, screen, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import {
  ExecutionHistoryButton,
  buildDurationBaselineParams,
  buildExecutionHistoryParams,
} from './execution-history-dialog'
import type { JobExecution } from './execution-shared'

vi.mock('@/components/data-table/data-table-audit', () => ({
  loadAuditUsers: vi.fn(async () => ({
    'user-1': { id: 'user-1', name: '张三（跨部门临时代理人）', userName: 'zhangsan' },
  })),
}))

afterEach(() => {
  vi.useRealTimers()
})

function render(ui: React.ReactElement) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <SessionIdentityProvider identityVersion="test-identity" storagePrincipalId="test-user">
        {ui}
      </SessionIdentityProvider>
    </NextIntlClientProvider>,
  )
}

const longSummary = '已同步组织、部门、岗位和人员主数据，共处理 128 条记录，未发现异常。'
const longError = 'OA 接口返回超时，已按执行策略终止本次同步，请检查上游服务状态后重试。'

const succeeded: JobExecution = {
  id: 'success-1',
  jobKey: 'oa-base-info-sync',
  jobType: 'OaBaseInfoSyncJob',
  jobMethod: 'ExecuteAsync',
  triggerKind: 1,
  status: 1,
  startedAt: '2026-07-22T08:00:00',
  finishedAt: '2026-07-22T08:00:01',
  durationMs: 1000,
  summary: longSummary,
  error: null,
  isDryRun: true,
  argsJson: '{"scope":"all"}',
  creatorId: 'user-1',
}

const failed: JobExecution = {
  ...succeeded,
  id: 'failed-1',
  triggerKind: 0,
  status: 2,
  durationMs: 5000,
  summary: null,
  error: longError,
  isDryRun: false,
  argsJson: null,
  creatorId: null,
}

const skipped: JobExecution = {
  ...succeeded,
  id: 'skipped-1',
  status: 4,
  durationMs: 20,
  summary: '已有同一任务正在执行，本次跳过',
  isDryRun: false,
  argsJson: null,
}

const successBaseline = [
  succeeded,
  { ...succeeded, id: 'success-dry-2', durationMs: 900, summary: null, argsJson: null },
  { ...succeeded, id: 'success-dry-3', durationMs: 1100, summary: null, argsJson: null },
  { ...succeeded, id: 'success-2', durationMs: 800, summary: null, isDryRun: false, argsJson: null },
  { ...succeeded, id: 'success-3', durationMs: 1200, summary: null, isDryRun: false, argsJson: null },
  { ...succeeded, id: 'success-4', durationMs: 1000, summary: null, isDryRun: false, argsJson: null },
]

function defaultRows(url: string) {
  const query = new URL(url, 'http://localhost').searchParams
  if (query.get('Status') === '1') {
    const isDryRun = query.get('IsDryRun') === 'true'
    return successBaseline.filter((row) => row.isDryRun === isDryRun)
  }
  return [succeeded, failed]
}

function stubFetch(rowsFor: (url: string) => JobExecution[] = defaultRows) {
  const fetchMock = vi.fn<typeof fetch>(async (input) => ({
    ok: true,
    json: async () => ({ items: rowsFor(String(input)) }),
    text: async () => '',
  }) as unknown as Response)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('buildExecutionHistoryParams', () => {
  it('把快捷筛选映射为后端 Status / IsDryRun 参数', () => {
    expect(buildExecutionHistoryParams('job-a', 'all').get('Status')).toBeNull()
    expect(buildExecutionHistoryParams('job-a', 'succeeded').get('Status')).toBe('1')
    expect(buildExecutionHistoryParams('job-a', 'failed').get('Status')).toBe('2')
    expect(buildExecutionHistoryParams('job-a', 'skipped').get('Status')).toBe('4')
    expect(buildExecutionHistoryParams('job-a', 'dryRun').get('IsDryRun')).toBe('true')
  })

  it('正式与演练成功基线分别请求，避免混合 top30 互相挤占', () => {
    expect(buildDurationBaselineParams('job-a', false).get('IsDryRun')).toBe('false')
    expect(buildDurationBaselineParams('job-a', true).get('IsDryRun')).toBe('true')
  })
})

describe('ExecutionHistoryButton', () => {
  it('收敛摘要、合并手动操作人、打开共享详情，并提供完整历史出口', async () => {
    stubFetch()
    render(<ExecutionHistoryButton jobKey="oa-base-info-sync" jobName="OA 基础信息同步" />)

    fireEvent.click(screen.getByRole('button', { name: '历史' }))
    const summaryButton = await screen.findByRole('button', { name: longSummary })
    expect(summaryButton).toHaveClass('truncate')
    expect(summaryButton.closest('td')).toHaveClass('max-w-0')

    const summaryRow = summaryButton.closest('tr')
    expect(summaryRow).not.toBeNull()
    // 历史行先落地，操作人再按当前结果集异步批量解析；不能把缓存响应速度写成同步契约。
    const operator = await within(summaryRow!).findByTitle('张三（跨部门临时代理人）')
    expect(operator).toHaveClass('min-w-0', 'flex-1', 'truncate')
    expect(operator.closest('td')).toHaveClass('overflow-hidden')
    expect(screen.queryByRole('columnheader', { name: '操作人' })).not.toBeInTheDocument()

    const anomalousDuration = screen.getByText('5.0s')
    await waitFor(() => expect(anomalousDuration).toHaveClass('text-warning', 'tabular-nums', 'text-right'))
    expect(anomalousDuration).toHaveAttribute(
      'title',
      '耗时超过同任务同执行模式近期成功记录中位数的 2 倍（至少 3 条成功样本）',
    )

    const fullHistory = screen.getByRole('link', { name: /查看该任务完整历史/ })
    expect(fullHistory).toHaveAttribute('href', '/system/scheduled-jobs/executions?jobKey=oa-base-info-sync')

    fireEvent.click(summaryButton)
    expect(await screen.findByRole('heading', { name: '执行详情 · OA 基础信息同步' })).toBeInTheDocument()
    expect(screen.getByText('执行 ID：success-1')).toBeInTheDocument()
    expect(document.querySelectorAll('[role="dialog"]')).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: '关闭' }))
    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: '执行详情 · OA 基础信息同步' })).not.toBeInTheDocument()
    })
    expect(screen.getByRole('heading', { name: '执行历史 · OA 基础信息同步' })).toBeInTheDocument()
    expect(document.querySelectorAll('[role="dialog"]')).toHaveLength(1)
    await waitFor(() => expect(summaryButton).toHaveFocus())
  })

  it.each([
    ['右上角 X', () => fireEvent.click(screen.getByRole('button', { name: 'Close' }))],
    ['Escape', () => fireEvent.keyDown(document, { key: 'Escape' })],
    ['外点', () => {
      const overlay = document.querySelector('[data-slot="dialog-overlay"]')!
      fireEvent.pointerDown(overlay, { button: 0 })
      fireEvent.click(overlay)
    }],
  ])('父弹窗通过%s关闭后焦点回到历史按钮', async (_closeMethod, close) => {
    stubFetch()
    render(<ExecutionHistoryButton jobKey="oa-base-info-sync" jobName="OA 基础信息同步" />)
    const historyButton = screen.getByRole('button', { name: '历史' })

    fireEvent.click(historyButton)
    expect(await screen.findByRole('heading', { name: '执行历史 · OA 基础信息同步' })).toBeInTheDocument()

    // Radix 为避免把“打开弹窗的同一次 pointerdown”误判为外点，会在下一宏任务才挂载 outside 监听。
    await new Promise((resolve) => setTimeout(resolve, 0))
    close()
    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: '执行历史 · OA 基础信息同步' })).not.toBeInTheDocument()
    })
    await waitFor(() => expect(historyButton).toHaveFocus())
  })

  it('快捷筛选由服务端重取失败与演练记录', async () => {
    const fetchMock = stubFetch((url) => {
      const query = new URL(url, 'http://localhost').searchParams
      if (query.get('Status') === '1') {
        const isDryRun = query.get('IsDryRun') === 'true'
        return successBaseline.filter((row) => row.isDryRun === isDryRun)
      }
      if (query.get('Status') === '2') return [failed]
      if (query.get('Status') === '4') return [skipped]
      if (query.get('IsDryRun') === 'true') return [succeeded]
      return [succeeded, failed]
    })
    render(<ExecutionHistoryButton jobKey="oa-base-info-sync" jobName="OA 基础信息同步" />)
    fireEvent.click(screen.getByRole('button', { name: '历史' }))
    await screen.findByText(longSummary)

    fireEvent.click(screen.getByRole('radio', { name: '失败' }))
    await screen.findByText(longError)
    const failureOnlyDuration = screen.getByText('5.0s')
    await waitFor(() => expect(failureOnlyDuration).toHaveClass('text-warning'))
    expect(failureOnlyDuration).toHaveAttribute(
      'title',
      '耗时超过同任务同执行模式近期成功记录中位数的 2 倍（至少 3 条成功样本）',
    )
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes('Status=2'))).toBe(true))
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('Status=1') && String(url).includes('IsDryRun=false'))).toBe(true)
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('Status=1') && String(url).includes('IsDryRun=true'))).toBe(true)

    fireEvent.click(screen.getByRole('radio', { name: '已跳过' }))
    const skippedSummary = await screen.findByText('已有同一任务正在执行，本次跳过')
    expect(within(skippedSummary.closest('tr')!).getByText('已跳过')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('Status=4'))).toBe(true)

    fireEvent.click(screen.getByRole('radio', { name: '仅演练' }))
    await screen.findByText(longSummary)
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('IsDryRun=true'))).toBe(true)
  })

  it('快捷筛选请求乱序完成时只接受最后一次筛选结果', async () => {
    let resolveFailed!: (response: Response) => void
    let resolveDryRun!: (response: Response) => void
    const response = (items: JobExecution[]) => ({
      ok: true,
      json: async () => ({ items }),
      text: async () => '',
    }) as unknown as Response
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async (input) => {
      const query = new URL(String(input), 'http://localhost').searchParams
      if (query.get('Status') === '2') {
        return await new Promise<Response>((resolve) => { resolveFailed = resolve })
      }
      if (query.get('IsDryRun') === 'true') {
        return await new Promise<Response>((resolve) => { resolveDryRun = resolve })
      }
      return response([succeeded, failed])
    }))

    render(<ExecutionHistoryButton jobKey="oa-base-info-sync" jobName="OA 基础信息同步" />)
    fireEvent.click(screen.getByRole('button', { name: '历史' }))
    await screen.findByText(longSummary)

    fireEvent.click(screen.getByRole('radio', { name: '失败' }))
    fireEvent.click(screen.getByRole('radio', { name: '仅演练' }))

    await act(async () => { resolveDryRun(response([succeeded])) })
    await screen.findByText(longSummary)
    await act(async () => { resolveFailed(response([failed])) })

    expect(screen.getByRole('radio', { name: '仅演练' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByText(longSummary)).toBeInTheDocument()
    expect(screen.queryByText(longError)).not.toBeInTheDocument()
  })

  it('执行中记录使用单飞轮询，慢请求完成终态后立即停止', async () => {
    vi.useFakeTimers()
    const running: JobExecution = { ...succeeded, id: 'running-1', status: 0, summary: '正在执行' }
    const completed: JobExecution = { ...running, status: 1, summary: '执行完成' }
    let mainCalls = 0
    let resolveSlowPoll!: (response: Response) => void
    const response = (items: JobExecution[]) => ({
      ok: true,
      json: async () => ({ items }),
      text: async () => '',
    }) as unknown as Response
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async (input) => {
      const query = new URL(String(input), 'http://localhost').searchParams
      if (query.get('Status') === '1') {
        const isDryRun = query.get('IsDryRun') === 'true'
        return response(successBaseline.filter((row) => row.isDryRun === isDryRun))
      }
      mainCalls += 1
      if (mainCalls === 1) return response([running])
      if (mainCalls === 2) {
        return await new Promise<Response>((resolve) => { resolveSlowPoll = resolve })
      }
      return response([completed])
    }))

    render(<ExecutionHistoryButton jobKey="oa-base-info-sync" jobName="OA 基础信息同步" />)
    fireEvent.click(screen.getByRole('button', { name: '历史' }))
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByText('正在执行')).toBeInTheDocument()

    await act(async () => { await vi.advanceTimersByTimeAsync(5000) })
    expect(mainCalls).toBe(2)
    await act(async () => { await vi.advanceTimersByTimeAsync(6000) })
    expect(mainCalls).toBe(2)

    await act(async () => { resolveSlowPoll(response([completed])) })
    expect(screen.getByText('执行完成')).toBeInTheDocument()
    await act(async () => { await vi.advanceTimersByTimeAsync(60000) })
    expect(mainCalls).toBe(2)
  })
})
