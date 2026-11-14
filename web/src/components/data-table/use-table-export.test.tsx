import type { ReactNode } from 'react'
import type { Table } from '@tanstack/react-table'
import { act, renderHook } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { describe, expect, it, vi } from 'vitest'
import zhHansMessages from '../../../messages/zh-Hans.json'
import { useTableExport } from './use-table-export'

function Wrapper({ children }: { children: ReactNode }) {
  return (
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      {children}
    </NextIntlClientProvider>
  )
}

describe('useTableExport · 服务端原生流', () => {
  it('在首个异步暂停前同步调用服务端导出，保留浏览器 user activation', async () => {
    let finishExport!: () => void
    const exportPending = new Promise<void>((resolve) => {
      finishExport = resolve
    })
    const onServerExport = vi.fn(() => exportPending)
    const { result } = renderHook(
      () => useTableExport({
        table: {} as Table<Record<string, unknown>>,
        exportFileName: 'unused',
        onServerExport,
      }),
      { wrapper: Wrapper },
    )

    const resultPromise = result.current.handleExport()

    // window.open 位于 onServerExport 内；若这里之前新增 await，真实浏览器会丢失点击激活态。
    expect(onServerExport).toHaveBeenCalledTimes(1)
    finishExport()
    await act(async () => { await resultPromise })
  })
})
