import type { ReactNode } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { afterEach, describe, expect, it, vi } from 'vitest'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import type { ScheduledJob } from './columns'
import { ScheduledJobsClient } from './client'

const mocks = vi.hoisted(() => ({
  requestRun: null as null | ((job: unknown) => void),
  refresh: vi.fn(),
  toast: vi.fn(),
}))

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: mocks.refresh }) }))
vi.mock('@/components/ui/use-toast', () => ({ toast: mocks.toast }))
vi.mock('./columns', () => ({
  buildColumns: (deps: { onRequestRun: (job: unknown) => void }) => {
    mocks.requestRun = deps.onRequestRun
    return []
  },
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({ data }: { data: unknown[] }) => (
    <button type="button" onClick={() => mocks.requestRun?.(data[0])}>测试立即执行</button>
  ),
}))
vi.mock('@/components/data-table/page-header', () => ({
  PageHeader: ({ title }: { title: ReactNode }) => <h1>{title}</h1>,
}))
vi.mock('./stats-cards', () => ({ StatsCards: () => null }))
vi.mock('./dry-run-dialog', () => ({ DryRunDialog: () => null }))

const job: ScheduledJob = {
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
}

afterEach(() => {
  mocks.requestRun = null
  mocks.refresh.mockReset()
  mocks.toast.mockReset()
  vi.unstubAllGlobals()
})

describe('ScheduledJobsClient', () => {
  it('同步执行返回 Skipped 时显示警示而不是成功或失败', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async (input) => {
      const url = String(input)
      if (url.endsWith('/scheduled-jobs/run')) {
        return new Response(JSON.stringify({
          success: true,
          summary: '已有同一任务正在执行，本次跳过',
          error: null,
          queued: false,
          status: 4,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (url.endsWith('/scheduled-jobs/stats')) {
        return new Response('null', { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })
    }))

    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <ScheduledJobsClient initial={[job]} initialStats={null} />
      </NextIntlClientProvider>,
    )

    fireEvent.click(screen.getByRole('button', { name: '测试立即执行' }))

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'warning',
      title: '已跳过',
      description: '审计日志定时清理：已有同一任务正在执行，本次跳过',
    }))
    expect(mocks.toast).not.toHaveBeenCalledWith(expect.objectContaining({ variant: 'destructive' }))
  })
})
