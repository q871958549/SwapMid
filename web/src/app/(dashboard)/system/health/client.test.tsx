import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { SystemHealthClient } from './client'

describe('SystemHealthClient', () => {
  it('renders checkedAt timestamp and refreshes health snapshot cleanly', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        overallStatus: 0,
        overallConclusion: '所有检查项均正常',
        checkedAt: '2026-11-18T06:00:00Z',
        items: [],
      }),
    })
    vi.stubGlobal('fetch', fetchMock)

    try {
      render(
        <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
          <SystemHealthClient
            initial={{
              overallStatus: 0,
              overallConclusion: '系统运行正常',
              checkedAt: '2026-11-18T05:00:00Z',
              items: [
                {
                  name: 'PostgreSQL',
                  status: 0,
                  conclusion: '连接正常',
                },
              ],
            }}
          />
        </NextIntlClientProvider>,
      )

      expect(screen.getByText('系统运行正常')).toBeTruthy()
      expect(screen.getByText('PostgreSQL')).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: /刷新/ }))
      await waitFor(() => {
        expect(fetchMock).toHaveBeenCalledWith('/api/proxy/system-health', { cache: 'no-store' })
        expect(screen.getByText('所有检查项均正常')).toBeTruthy()
      })
    } finally {
      vi.unstubAllGlobals()
    }
  })
})
