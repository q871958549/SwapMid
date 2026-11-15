import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { DataSourcesClient, type DataSource } from './client'

const mocks = vi.hoisted(() => ({
  fetch: vi.fn(),
  toast: vi.fn(),
  reload: vi.fn(),
}))

vi.mock('@/components/ui/use-toast', () => ({ toast: mocks.toast }))
vi.mock('@/lib/crud/use-crud-resource', () => ({
  useCrudResource: ({ initial }: { initial: { items: DataSource[]; totalCount: number } }) => ({
    rows: initial.items,
    total: initial.totalCount,
    reload: mocks.reload,
    reloadAndRefresh: mocks.reload,
    tableProps: {},
  }),
}))
vi.mock('@/lib/crud/use-crud-form', () => ({
  useCrudForm: () => ({
    mode: 'create',
    target: null,
    form: {
      name: '',
      type: '0',
      config: '{}',
      description: '',
      hasSecret: false,
      isEnabled: true,
      relational: {
        provider: 'postgresql',
        host: '',
        port: '5432',
        database: '',
        targetResourceGroup: '',
        username: '',
        password: '',
        tlsMode: 'verify-full',
      },
      clickhouse: {
        endpoint: '',
        database: 'default',
        targetResourceGroup: '',
        username: 'default',
        password: '',
      },
      objectStorage: {
        provider: 'minio',
        endpoint: '',
        region: 'us-east-1',
        bucket: '',
        prefix: '',
        format: 'parquet',
        pathStyle: true,
        targetResourceGroup: '',
        accessKey: '',
        secretKey: '',
      },
      kafka: {
        bootstrapServers: '',
        clientId: 'swapmid-etl',
        securityProtocol: 'Plaintext',
        saslMechanism: 'Plain',
        saslUsername: '',
        saslPassword: '',
        acks: 'all',
        compression: 'zstd',
        targetResourceGroup: '',
      },
    },
    openEdit: vi.fn(),
    openCreate: vi.fn(),
    askDelete: vi.fn(),
    set: vi.fn(),
  }),
}))
vi.mock('@/components/data-table/column-factories', () => ({
  actionsColumn: ({ extraItems }: { extraItems?: (row: DataSource) => ReactNode }) => ({
    id: 'actions',
    cell: ({ row }: { row: { original: DataSource } }) => extraItems?.(row.original) ?? null,
  }),
  booleanBadgeColumn: () => ({ id: 'isEnabled' }),
  primaryColumn: () => ({ id: 'name' }),
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({ columns, data }: { columns: { id?: string; cell?: (ctx: { row: { original: DataSource } }) => ReactNode }[]; data: DataSource[] }) => {
    const actions = columns.find(c => c.id === 'actions')
    return <>{data.map(row => <div key={row.id}>{actions?.cell?.({ row: { original: row } })}</div>)}</>
  },
}))
vi.mock('@/components/data-table/data-table-column-header', () => ({
  DataTableColumnHeader: ({ title }: { title: ReactNode }) => <>{title}</>,
}))
vi.mock('@/components/data-table/page-header', () => ({ PageHeader: () => null }))
vi.mock('@/components/data-table/create-button', () => ({ CreateButton: () => null }))
vi.mock('@/components/data-table/crud-form-dialog', () => ({ CrudFormDialog: () => null }))
vi.mock('@/components/data-table/delete-confirm', () => ({ DeleteConfirm: () => null }))
vi.mock('@/components/ui/dropdown-menu', () => ({
  DropdownMenuItem: ({ children, onClick, disabled }: {
    children: ReactNode
    onClick?: () => void
    disabled?: boolean
  }) => <button type="button" onClick={onClick} disabled={disabled}>{children}</button>,
}))

const MINIO_SOURCE_ID = '55555555-5555-4555-8555-555555555555'
const REST_SOURCE_ID = '66666666-6666-4666-8666-666666666666'

const minioSource: DataSource = {
  id: MINIO_SOURCE_ID,
  name: 'MinIO-Lake',
  type: 5,
  config: JSON.stringify({
    provider: 'minio',
    endpoint: 'http://minio:9000',
    region: 'us-east-1',
    bucket: 'swapmid-lake',
    prefix: 'e2e/orders',
    format: 'parquet',
    pathStyle: true,
    targetResourceGroup: 'minio-verify-lake',
    accessKey: 'swapmid_minio',
  }),
  description: null,
  hasSecret: true,
  isEnabled: true,
  creationTime: '2026-11-15T00:00:00Z',
}

const restSource: DataSource = {
  id: REST_SOURCE_ID,
  name: 'Partner-REST',
  type: 1,
  config: '{}',
  description: null,
  hasSecret: false,
  isEnabled: true,
  creationTime: '2026-11-15T00:00:00Z',
}

beforeEach(() => {
  vi.stubGlobal('fetch', mocks.fetch)
  mocks.fetch.mockReset()
  mocks.toast.mockReset()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DataSourcesClient · 列表行一键测试连接', () => {
  it('具备更新权限且为可探活数据源时允许触发行级测试连接并提示延迟', async () => {
    mocks.fetch.mockResolvedValue(new Response(JSON.stringify({
      success: true,
      message: 'OK',
      elapsedMs: 12,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))

    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <DataSourcesClient data={{ items: [minioSource, restSource], totalCount: 2 }} canUpdate />
      </NextIntlClientProvider>,
    )

    const testButtons = screen.getAllByRole('button', { name: '测试连接' })
    expect(testButtons).toHaveLength(1)
    fireEvent.click(testButtons[0])

    await waitFor(() => expect(mocks.toast).toHaveBeenCalledWith({
      variant: 'success',
      title: '连接成功 · 12ms',
      description: 'MinIO-Lake',
    }))
    expect(mocks.fetch).toHaveBeenCalledWith(
      `/api/proxy/data-sources/${MINIO_SOURCE_ID}/test-connection`,
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('无更新权限时隐藏列表行测试连接按钮', () => {
    render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <DataSourcesClient data={{ items: [minioSource], totalCount: 1 }} canUpdate={false} />
      </NextIntlClientProvider>,
    )

    expect(screen.queryByRole('button', { name: '测试连接' })).not.toBeInTheDocument()
  })
})
