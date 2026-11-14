import { render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider, useTranslations } from 'next-intl'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import zhHansMessages from '../../../messages/zh-Hans.json'
import { IntlObservabilityProvider } from './intl-observability-provider'

function PipelineTitleHeader() {
  const t = useTranslations('DataIntegration.pipeline')
  return <span>{t('pageTitle')}</span>
}

function MissingMessageProbe() {
  const t = useTranslations('Probe')
  return <span>{t('missingLabel')}</span>
}

describe('IntlObservabilityProvider', () => {
  beforeEach(() => {
    window.history.replaceState({}, '', '/')
  })

  it('数据管道页标题由真实嵌套消息解析，防点号 key 回归', () => {
    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <IntlObservabilityProvider buildId="test-build">
          <PipelineTitleHeader />
        </IntlObservabilityProvider>
      </NextIntlClientProvider>,
    )

    expect(screen.getByText('数据管道')).toBeInTheDocument()
    expect(screen.queryByText('DataIntegration.pipeline.pageTitle')).not.toBeInTheDocument()
  })

  it('MissingMessage 显示安全文案并上报 locale/key/path/buildId', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    window.history.replaceState({}, '', '/data-integration/pipelines/p-1?tab=runs')

    render(
      <NextIntlClientProvider locale="en" messages={{ Probe: { present: 'Present' } }}>
        <IntlObservabilityProvider buildId="build-123">
          <MissingMessageProbe />
        </IntlObservabilityProvider>
      </NextIntlClientProvider>,
    )

    expect(screen.getByText('Content unavailable')).toBeInTheDocument()
    expect(screen.queryByText('Probe.missingLabel')).not.toBeInTheDocument()

    await waitFor(() => expect(warnSpy).toHaveBeenCalledTimes(1))
    expect(warnSpy.mock.calls[0][0]).toBe('[swapmid-client-error]')
    const payload = warnSpy.mock.calls[0][1] as { kind: string; message: string; buildId: string; url: string }
    const incident = JSON.parse(payload.message) as Record<string, string>

    expect(payload.kind).toBe('i18n-missing-message')
    expect(payload.buildId).toBe('build-123')
    expect(payload.url).toContain('/data-integration/pipelines/p-1?tab=runs')
    expect(incident).toEqual({
      code: 'MISSING_MESSAGE',
      locale: 'en',
      key: 'Probe.missingLabel',
      path: '/data-integration/pipelines/p-1',
      buildId: 'build-123',
    })
    warnSpy.mockRestore()
  })
})
