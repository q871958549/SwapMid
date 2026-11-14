import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { afterEach, describe, expect, it, vi } from 'vitest'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import type { ScheduledJob } from './columns'
import { DryRunDialog } from './dry-run-dialog'

const toastMock = vi.hoisted(() => vi.fn())
vi.mock('@/components/ui/use-toast', () => ({ toast: toastMock }))

const job: ScheduledJob = {
  key: 'oa-base-info-sync',
  displayName: 'OA 基础信息同步',
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
  toastMock.mockReset()
  vi.unstubAllGlobals()
})

describe('DryRunDialog', () => {
  it('演练返回 Skipped 时关闭表单并显示警示，不进入失败反馈', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(JSON.stringify({
      success: true,
      summary: '已有同一任务正在执行，本次跳过',
      error: null,
      status: 4,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    const onClose = vi.fn()
    const onDone = vi.fn()

    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <DryRunDialog target={job} onClose={onClose} onDone={onDone} />
      </NextIntlClientProvider>,
    )

    fireEvent.change(screen.getByPlaceholderText('you@company.com'), { target: { value: 'tester@example.com' } })
    fireEvent.click(screen.getByRole('button', { name: '发送演练' }))

    await waitFor(() => expect(toastMock).toHaveBeenCalledWith({
      variant: 'warning',
      title: '已跳过',
      description: 'OA 基础信息同步：已有同一任务正在执行，本次跳过',
    }))
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(onDone).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('演练失败')).not.toBeInTheDocument()
  })
})
