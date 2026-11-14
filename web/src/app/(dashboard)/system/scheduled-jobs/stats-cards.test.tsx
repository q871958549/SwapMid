import { describe, expect, it } from 'vitest'
import { render as rtlRender, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { StatsCards, buildFailedExecutionsHref } from './stats-cards'

function render(ui: React.ReactElement) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      {ui}
    </NextIntlClientProvider>,
  )
}

describe('StatsCards', () => {
  it('近 24 小时失败卡直达带失败状态与精确闭合窗口的全局执行历史', () => {
    render(
      <StatsCards
        stats={{
          enabledCount: 12,
          totalCount: 15,
          failed24hCount: 3,
          failed24hStartedFrom: '2026-07-21T08:30:15',
          failed24hMeasuredAt: '2026-07-22T08:30:15',
          runningCount: 1,
        }}
      />,
    )

    const failedLink = screen.getByRole('link', { name: /近 24 小时失败/ })
    expect(failedLink).toHaveAttribute(
      'href',
      '/system/scheduled-jobs/executions?status=2&windowStartedAt=2026-07-21T08%3A30%3A15&windowEndedAt=2026-07-22T08%3A30%3A15',
    )
    expect(screen.getAllByRole('link')).toHaveLength(1)
  })

  it('URL 构造函数保留固定状态并编码闭合窗口两端时间', () => {
    expect(buildFailedExecutionsHref('2026-07-21T08:30:15', '2026-07-22T08:30:15')).toBe(
      '/system/scheduled-jobs/executions?status=2&windowStartedAt=2026-07-21T08%3A30%3A15&windowEndedAt=2026-07-22T08%3A30%3A15',
    )
  })

  it('后端尚未返回窗口起点时保留统计展示但不生成错误钻取链接', () => {
    render(
      <StatsCards stats={{ enabledCount: 12, totalCount: 15, failed24hCount: 3, runningCount: 1 }} />,
    )

    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
