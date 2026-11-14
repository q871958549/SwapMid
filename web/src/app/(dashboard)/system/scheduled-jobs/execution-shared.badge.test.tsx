import { render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { describe, expect, it } from 'vitest'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { StatusBadge } from './execution-shared'

function renderBadge(status: number) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <StatusBadge status={status} />
    </NextIntlClientProvider>,
  )
}

describe('StatusBadge', () => {
  it('把跳过展示为中性终态', () => {
    renderBadge(4)

    expect(screen.getByText('已跳过')).toBeInTheDocument()
  })

  it('未知枚举值不回退成执行中', () => {
    renderBadge(99)

    expect(screen.getByText('未知状态（99）')).toBeInTheDocument()
    expect(screen.queryByText('执行中')).not.toBeInTheDocument()
  })
})
