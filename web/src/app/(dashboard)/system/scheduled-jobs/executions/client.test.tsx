import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../../messages/zh-Hans.json'
import type { JobExecution } from '../execution-shared'
import { ScheduledJobExecutionsClient } from './client'

const mocks = vi.hoisted(() => ({
  useCrudResource: vi.fn(),
  useUrlListState: vi.fn(),
}))

vi.mock('@/lib/crud/use-crud-resource', () => ({ useCrudResource: mocks.useCrudResource }))
vi.mock('@/lib/crud/use-url-list-state', () => ({ useUrlListState: mocks.useUrlListState }))
vi.mock('@/components/data-table/page-header', () => ({
  PageHeader: ({ title }: { title: ReactNode }) => <h1>{title}</h1>,
}))
vi.mock('@/components/data-table/data-table-column-header', () => ({
  DataTableColumnHeader: ({ title }: { title: ReactNode }) => <span>{title}</span>,
}))
vi.mock('@/components/ui/date-picker', () => ({
  DatePicker: () => <button type="button">日期</button>,
}))
vi.mock('@/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: () => null,
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({ columns, data }: {
    columns: Array<{ accessorKey?: string; cell?: (context: { row: { original: JobExecution } }) => ReactNode }>
    data: JobExecution[]
  }) => {
    const summaryColumn = columns.find((column) => column.accessorKey === 'summary')
    return <div>{data[0] && summaryColumn?.cell?.({ row: { original: data[0] } })}</div>
  },
}))

const execution: JobExecution = {
  id: 'execution-1',
  jobKey: 'audit-log-cleanup',
  jobType: 'OaBaseInfoSyncJob',
  jobMethod: 'ExecuteAsync',
  triggerKind: 1,
  status: 1,
  startedAt: '2026-07-22T08:00:00',
  finishedAt: '2026-07-22T08:00:01',
  durationMs: 1000,
  summary: '已同步 128 条记录',
  error: null,
  isDryRun: false,
  argsJson: null,
  creatorId: 'user-1',
}

function renderClient(filterOverrides: Partial<{
  jobKey: string
  status: string
  triggerKind: string
  isDryRun: string
  startedFrom: string
  windowStartedAt: string
  windowEndedAt: string
  startedTo: string
}> = {}) {
  const setFilters = vi.fn()
  mocks.useUrlListState.mockReturnValue([{
    jobKey: '', status: '', triggerKind: '', isDryRun: '', startedFrom: '', windowStartedAt: '', windowEndedAt: '', startedTo: '',
    ...filterOverrides,
  }, setFilters])
  mocks.useCrudResource.mockReturnValue({
    rows: [execution],
    total: 1,
    setPage: vi.fn(),
    tableProps: {},
  })

  const result = render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <ScheduledJobExecutionsClient
        initial={{ items: [execution], totalCount: 1 }}
        jobs={[{
          key: 'audit-log-cleanup',
          displayName: '审计日志定时清理',
          description: null,
          cron: '0 0 * * *',
          enabled: true,
          supportsParameterizedRun: true,
          parameters: [],
          isDestructive: false,
          isLongRunning: false,
          lastExecution: null,
          nextExecution: null,
          lastJobState: null,
        }]}
      />
    </NextIntlClientProvider>,
  )
  return { ...result, setFilters }
}

describe('ScheduledJobExecutionsClient · 详情焦点恢复', () => {
  it('状态筛选包含已跳过终态', () => {
    renderClient()

    expect(screen.getByText('已跳过')).toBeInTheDocument()
  })

  it.each([
    ['页脚按钮', () => fireEvent.click(screen.getByRole('button', { name: '关闭' }))],
    ['右上角按钮', () => fireEvent.click(screen.getByRole('button', { name: 'Close' }))],
    ['Escape', () => fireEvent.keyDown(document, { key: 'Escape' })],
  ])('通过%s关闭后焦点回到原摘要按钮', async (_closeMethod, close) => {
    renderClient()
    const trigger = screen.getByRole('button', { name: execution.summary! })

    fireEvent.click(trigger)
    expect(await screen.findByRole('heading', { name: '执行详情 · 审计日志定时清理' })).toBeInTheDocument()

    close()
    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: '执行详情 · 审计日志定时清理' })).not.toBeInTheDocument()
    })
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('展示可见的精确统计时间窗，并允许用户清除', () => {
    const { setFilters } = renderClient({
      windowStartedAt: '2026-07-21T08:30:15',
      windowEndedAt: '2026-07-22T08:30:15',
    })
    const windowFilter = screen.getByRole('button', { name: '统计卡时间窗' })

    expect(windowFilter).toHaveAttribute(
      'title',
      '统计卡快照窗口：2026-07-21 08:30:15 至 2026-07-22 08:30:15',
    )
    fireEvent.click(windowFilter)
    expect(setFilters).toHaveBeenCalledWith({ windowStartedAt: '', windowEndedAt: '' })
  })
})
