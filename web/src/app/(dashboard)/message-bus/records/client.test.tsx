import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { MessageRecordsClient, type MessageRecord } from './client'

const ID_1 = '11111111-1111-4111-8111-111111111111'
const ID_2 = '22222222-2222-4222-8222-222222222222'
const TOPIC_ID = '99999999-9999-4999-8999-999999999999'

const mocks = vi.hoisted(() => ({
  fetch: vi.fn(),
  reloadAndRefresh: vi.fn(),
  toast: vi.fn(),
  setFilters: vi.fn(),
}))

vi.mock('@/components/ui/use-toast', () => ({
  useToast: () => ({ toast: mocks.toast }),
}))
vi.mock('@/lib/hooks/use-url-list-state', () => ({
  useUrlListState: () => [{ status: '' }, mocks.setFilters],
}))
vi.mock('@/lib/crud/use-crud-resource', () => ({
  useCrudResource: ({ initial }: { initial: { items: MessageRecord[]; totalCount: number } }) => ({
    rows: initial.items,
    total: initial.totalCount,
    reload: vi.fn(),
    reloadAndRefresh: mocks.reloadAndRefresh,
    setPage: vi.fn(),
    tableProps: {},
  }),
}))
vi.mock('@/components/data-table/column-factories', () => ({
  numberColumn: (key: string) => ({ id: key }),
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({
    data,
    getRowId,
    bulkActions,
  }: {
    data: MessageRecord[]
    getRowId?: (row: MessageRecord, index: number) => string
    bulkActions?: (rows: MessageRecord[], clearSelection: () => void, selectedIds: string[]) => ReactNode
  }) => (
    <div>
      {bulkActions?.(
        data,
        vi.fn(),
        data.map((item, idx) => (getRowId ? getRowId(item, idx) : String(idx))),
      )}
    </div>
  ),
}))
vi.mock('@/components/data-table/data-table-column-header', () => ({
  DataTableColumnHeader: ({ title }: { title: string }) => <span>{title}</span>,
}))
vi.mock('@/components/data-table/page-header', () => ({
  PageHeader: ({ title }: { title: string }) => <h1>{title}</h1>,
}))

function makeRecord(id: string, subject: string): MessageRecord {
  return {
    id,
    topicId: TOPIC_ID,
    topicName: 'orders.events',
    subject,
    payload: '{"orderId":101}',
    status: 3,
    retryCount: 1,
    deliveredAt: null,
    errorMessage: 'webhook timeout',
    creationTime: '2026-11-16T00:00:00Z',
  }
}

describe('MessageRecordsClient batch replay', () => {
  beforeEach(() => {
    mocks.fetch.mockReset()
    mocks.reloadAndRefresh.mockReset()
    mocks.toast.mockReset()
    vi.stubGlobal('fetch', mocks.fetch)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('calls POST /api/proxy/message-records/batch-replay once for selected records', async () => {
    mocks.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ requested: 2, succeeded: 2, skipped: 0 }),
    })

    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <MessageRecordsClient
          initial={{
            items: [makeRecord(ID_1, 'evt-1'), makeRecord(ID_2, 'evt-2')],
            totalCount: 2,
          }}
        />
      </NextIntlClientProvider>,
    )

    fireEvent.click(screen.getByRole('button', { name: /批量重推/ }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledTimes(1)
      expect(mocks.fetch).toHaveBeenCalledWith(
        '/api/proxy/message-records/batch-replay',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ ids: [ID_1, ID_2] }),
        }),
      )
      expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce()
    })
  })
})
