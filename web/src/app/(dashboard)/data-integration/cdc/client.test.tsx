import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import type { CdcStreamDto } from '@/lib/api/types'
import { CdcStreamsClient } from './client'
import {
  defaultCdcForm,
  detectCdcSourceTypeFromSource,
  isCdcFormValid,
  toCdcBody,
  toCdcForm,
} from './cdc-form-state'

type ActionColumn = {
  id?: string
  cell?: (context: { row: { original: CdcStreamDto } }) => ReactNode
}

const STREAM_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const SOURCE_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

const mocks = vi.hoisted(() => ({
  fetch: vi.fn(),
  reloadAndRefresh: vi.fn(),
  toast: vi.fn(),
  openEdit: vi.fn(),
  openCreate: vi.fn(),
  askDelete: vi.fn(),
}))

vi.mock('@/components/ui/use-toast', () => ({ toast: mocks.toast }))
vi.mock('@/lib/crud/use-crud-resource', () => ({
  useCrudResource: ({ initial }: { initial: { items: CdcStreamDto[]; totalCount: number } }) => ({
    rows: initial.items,
    total: initial.totalCount,
    reload: vi.fn(),
    reloadAndRefresh: mocks.reloadAndRefresh,
    tableProps: {},
  }),
}))
vi.mock('@/lib/crud/use-crud-form', () => ({
  useCrudForm: () => ({
    form: {
      name: '',
      description: '',
      sourceId: SOURCE_ID,
      sourceType: '0',
      publicationName: 'swapmid_cdc_pub',
      slotName: 'swapmid_cdc_slot',
      monitoredTables: 'public.orders',
      autoCreatePublicationAndSlot: true,
      initialSnapshotEnabled: false,
      pollIntervalSeconds: '15',
      maxBatchSize: '500',
      targetMode: '0',
      targetTopicName: 'integration.cdc.events',
      targetPipelineId: '',
      targetSinkId: '',
      targetSinkDestination: '',
      targetSinkKeyColumns: '',
      targetDeletePolicy: '0',
      softDeleteColumn: '',
      softDeleteTimestampColumn: '',
      targetFieldMappings: [],
      lakehouseTableFormat: '0',
      lakehouseCompactMinFiles: '',
      schemaDriftPolicy: '0',
    },
    openEdit: mocks.openEdit,
    openCreate: mocks.openCreate,
    askDelete: mocks.askDelete,
    set: vi.fn(),
  }),
}))
vi.mock('@/components/data-table/column-factories', () => ({
  actionsColumn: ({ extraItems }: { extraItems?: (row: CdcStreamDto) => ReactNode }) => ({
    id: 'actions',
    cell: ({ row }: { row: { original: CdcStreamDto } }) => extraItems?.(row.original),
  }),
  primaryColumn: (key: string) => ({ id: key }),
  numberColumn: (key: string) => ({ id: key }),
}))
vi.mock('@/components/data-table/data-table', () => ({
  DataTable: ({ columns, data }: { columns: ActionColumn[]; data: CdcStreamDto[] }) => {
    const actions = columns.find(column => column.id === 'actions')
    return <>{data.map(row => <div key={row.id}>{actions?.cell?.({ row: { original: row } })}</div>)}</>
  },
}))
vi.mock('@/components/data-table/data-table-column-header', () => ({
  DataTableColumnHeader: ({ title }: { title: string }) => <span>{title}</span>,
}))
vi.mock('@/components/data-table/page-header', () => ({
  PageHeader: ({ title, primaryAction }: { title: string; primaryAction?: ReactNode }) => (
    <div>
      <h1>{title}</h1>
      {primaryAction}
    </div>
  ),
}))
vi.mock('@/components/data-table/filter-bar', () => ({
  FilterBar: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
}))
vi.mock('@/components/data-table/list-page-shell', () => ({
  ListPageShell: ({ header, filters, table }: { header?: ReactNode; filters?: ReactNode; table?: ReactNode }) => (
    <div>{header}{filters}{table}</div>
  ),
}))
vi.mock('@/components/data-table/crud-form-dialog', () => ({
  CrudFormDialog: () => null,
}))
vi.mock('@/components/data-table/delete-confirm', () => ({
  DeleteConfirm: () => null,
}))
vi.mock('@/components/ui/dropdown-menu', () => ({
  DropdownMenuItem: ({
    children,
    disabled,
    onClick,
  }: {
    children: ReactNode
    disabled?: boolean
    onClick?: () => void
  }) => (
    <button type="button" disabled={disabled} onClick={onClick}>{children}</button>
  ),
}))

function makeStream(overrides: Partial<CdcStreamDto> = {}): CdcStreamDto {
  return {
    id: STREAM_ID,
    name: 'orders-pg-cdc',
    sourceId: SOURCE_ID,
    sourceName: 'Local Demo PostgreSQL',
    publicationName: 'swapmid_orders_pub',
    slotName: 'swapmid_orders_slot',
    monitoredTables: 'public.etl_demo_source_orders',
    autoCreatePublicationAndSlot: true,
    pollIntervalSeconds: 15,
    maxBatchSize: 500,
    targetMode: 0,
    targetTopicName: 'integration.cdc.events',
    status: 0,
    lastCapturedCount: 0,
    totalCapturedCount: 0,
    creationTime: '2026-11-16T00:00:00Z',
    ...overrides,
  }
}

function renderClient(stream: CdcStreamDto) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <CdcStreamsClient
        data={{ items: [stream], totalCount: 1 }}
        sources={[{ id: SOURCE_ID, name: 'Local Demo PostgreSQL', type: 0, config: '{"provider":"PostgreSql"}' }]}
        pipelines={[]}
        topics={[{ id: 't1', name: 'integration.cdc.events' }]}
        canCreate
        canUpdate
        canDelete
        canManage
      />
    </NextIntlClientProvider>,
  )
}

describe('CdcStreamsClient', () => {
  beforeEach(() => {
    mocks.fetch.mockReset()
    mocks.reloadAndRefresh.mockReset()
    mocks.toast.mockReset()
    vi.stubGlobal('fetch', mocks.fetch)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('triggers preflight check and renders ready summary dialog', async () => {
    mocks.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        isReady: true,
        walLevel: 'logical',
        postgreSqlVersion: '18.0',
        publicationExists: true,
        slotExists: true,
        slotActive: false,
        slotPlugin: 'pgoutput',
        confirmedFlushLsn: '0/16B6C50',
        currentWalLsn: '0/16B6D00',
        walLagBytes: 2097152,
        tableReplicaIdentities: [
          { schema: 'public', table: 'etl_demo_source_orders', replicaIdentity: 'full' },
        ],
        diagnostics: [],
        summary: 'Ready for logical replication',
      }),
    })

    renderClient(makeStream({ status: 0 }))
    fireEvent.click(screen.getByRole('button', { name: '前置预检' }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/preflight`, { method: 'POST' })
    })
    expect(await screen.findByText('logical')).toBeTruthy()
    expect(screen.getByText('0/16B6C50')).toBeTruthy()
    expect(screen.getByText('2.00 MB')).toBeTruthy()
    expect(screen.getByText('public.etl_demo_source_orders · full')).toBeTruthy()
  })

  it('drops an idle replication slot after confirmation', async () => {
    mocks.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        streamId: STREAM_ID,
        slotName: 'swapmid_orders_slot',
        dropped: true,
        message: 'Dropped slot swapmid_orders_slot',
      }),
    })

    renderClient(makeStream({ status: 2 }))
    fireEvent.click(screen.getByRole('button', { name: '释放槽位' }))
    const buttons = await screen.findAllByRole('button', { name: '释放槽位' })
    fireEvent.click(buttons[buttons.length - 1])

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/drop-slot`, { method: 'POST' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce()
    })
  })

  it('activates a Draft stream and refreshes list', async () => {
    mocks.fetch.mockResolvedValueOnce({ ok: true })
    renderClient(makeStream({ status: 0 }))

    const captureBtn = screen.getByRole('button', { name: '激活后可捕获' }) as HTMLButtonElement
    expect(captureBtn.disabled).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: '激活' }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/activate`, { method: 'PUT' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce()
    })
  })

  it('captures WAL batch and pauses an Active stream', async () => {
    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          capturedEventCount: 3,
          deliveredCount: 1,
          newCheckpointLsn: '0/16B7000',
        }),
      })
      .mockResolvedValueOnce({ ok: true })

    renderClient(makeStream({ status: 1 }))

    fireEvent.click(screen.getByRole('button', { name: '立即捕获' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/capture`, { method: 'POST' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledTimes(1)
    })

    fireEvent.click(screen.getByRole('button', { name: '暂停' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/pause`, { method: 'PUT' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledTimes(2)
    })
  })

  it('loads external sink DDL template and provisions ClickHouse table on click', async () => {
    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          targetType: 4,
          destination: 'analytics.cdc_orders_changelog',
          partitionKeyExpression: 'table:order_id=<value>',
          targetExists: false,
          clickHouseDdl: 'CREATE TABLE IF NOT EXISTS `analytics`.`cdc_orders_changelog` ... ENGINE = ReplacingMergeTree(`commit_timestamp_utc`)',
          columns: [
            {
              name: 'op',
              logicalType: 'string',
              clickHouseType: 'LowCardinality(String)',
              nullable: false,
              description: 'Event operation',
            },
          ],
          samplePayloadJson: '{"op":"insert","payload":{"order_id":1001}}',
          summary: 'Generated ClickHouse DDL template for analytics.cdc_orders_changelog',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          targetType: 4,
          destination: 'analytics.cdc_orders_changelog',
          partitionKeyExpression: 'table:order_id=<value>',
          targetExists: true,
          clickHouseDdl: 'CREATE TABLE IF NOT EXISTS `analytics`.`cdc_orders_changelog` ... ENGINE = ReplacingMergeTree(`commit_timestamp_utc`)',
          columns: [],
          samplePayloadJson: '{"op":"insert"}',
          summary: 'ClickHouse target ready',
        }),
      })

    renderClient(makeStream({
      status: 0,
      targetMode: 2,
      targetSinkId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
      targetSinkName: 'ClickHouse Analytics',
      targetSinkDestination: 'analytics.cdc_orders_changelog',
      targetSinkKeyColumns: 'order_id',
    }))

    fireEvent.click(screen.getByRole('button', { name: '查看模板' }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/sink-template`)
    })
    expect(await screen.findByText(/CREATE TABLE IF NOT EXISTS `analytics`.`cdc_orders_changelog`/)).toBeTruthy()
    expect(screen.getByText('Key: table:order_id=<value>')).toBeTruthy()
    expect(screen.getByText('LowCardinality(String)')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '一键建表' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/provision-sink`, { method: 'POST' })
    })
    expect(await screen.findByText('目标已就绪')).toBeTruthy()
  })

  it('runs consistent initial snapshot and refreshes stream list', async () => {
    mocks.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        streamId: STREAM_ID,
        consistentPointLsn: '0/1F00000',
        snapshotRowCount: 12,
        deliveredCount: 12,
        usedExportedSnapshot: true,
      }),
    })

    renderClient(makeStream({ status: 0, initialSnapshotEnabled: true }))
    fireEvent.click(screen.getByRole('button', { name: '全量快照' }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/snapshot`, { method: 'POST' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce()
    })
  })

  it('displays schema drift alert in preflight and accepts new schema baseline', async () => {
    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          isReady: true,
          walLevel: 'logical',
          postgreSqlVersion: '18.0',
          publicationExists: true,
          slotExists: true,
          slotActive: false,
          slotPlugin: 'pgoutput',
          confirmedFlushLsn: '0/16B6C50',
          currentWalLsn: '0/16B6D00',
          schemaDriftDetected: true,
          schemaDriftSummary: 'public.etl_demo_source_orders: +col loyalty_tier (text)',
          currentSchemaHash: 'abcdef0123456789',
          diagnostics: ['Schema drift detected'],
          summary: 'Ready',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          schemaBaselineHash: 'abcdef0123456789',
          message: 'Schema accepted',
        }),
      })

    renderClient(makeStream({
      status: 2,
      targetMode: 3,
      targetSinkId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
      targetSinkName: 'Mirror PostgreSQL',
      targetSinkDestination: 'public.etl_demo_orders_mirror',
      targetSinkKeyColumns: 'order_id',
      schemaDriftPolicy: 2,
      schemaDriftDetected: true,
      schemaDriftSummary: 'public.etl_demo_source_orders: +col loyalty_tier (text)',
    }))

    fireEvent.click(screen.getByRole('button', { name: '前置预检' }))
    expect(await screen.findByText('public.etl_demo_source_orders: +col loyalty_tier (text)')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '关闭' }))

    fireEvent.click(screen.getByRole('button', { name: '确认Schema' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/accept-schema`, { method: 'POST' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledOnce()
    })
  })

  it('handles 204 No Content responses cleanly on activate and pause lifecycle actions', async () => {
    mocks.fetch
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))

    const { rerender } = renderClient(makeStream({ status: 0 }))
    fireEvent.click(screen.getByRole('button', { name: '激活' }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/activate`, { method: 'PUT' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledTimes(1)
      expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success', title: 'CDC 流已激活' }))
    })

    rerender(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <CdcStreamsClient
          data={{ items: [makeStream({ status: 1 })], totalCount: 1 }}
          sources={[{ id: SOURCE_ID, name: 'Local Demo PostgreSQL', type: 0, config: '{"provider":"PostgreSql"}' }]}
          pipelines={[]}
          topics={[{ id: 't1', name: 'integration.cdc.events' }]}
          canCreate
          canUpdate
          canDelete
          canManage
        />
      </NextIntlClientProvider>,
    )

    fireEvent.click(screen.getByRole('button', { name: '暂停' }))

    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/pause`, { method: 'PUT' })
      expect(mocks.reloadAndRefresh).toHaveBeenCalledTimes(2)
      expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success', title: 'CDC 流已暂停' }))
    })
  })

  it('parses ABP error JSON body on 403 capture drift failure and refreshes stream row state', async () => {
    mocks.fetch.mockResolvedValueOnce(new Response(
      JSON.stringify({ error: { code: 'SwapMid:ETL:036', message: '检测到上游表 Schema 漂移，已自动暂停 CDC 流' } }),
      { status: 403, headers: { 'Content-Type': 'application/json' } },
    ))

    renderClient(makeStream({ status: 1 }))
    fireEvent.click(screen.getByRole('button', { name: '立即捕获' }))

    await waitFor(() => {
      expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
        variant: 'destructive',
        title: 'WAL 捕获失败',
        description: '检测到上游表 Schema 漂移，已自动暂停 CDC 流',
      }))
      expect(mocks.reloadAndRefresh).toHaveBeenCalledTimes(1)
    })
  })

  it('serializes and validates SoftDelete tombstone and field mappings in CdcFormState helpers', () => {
    const valid = isCdcFormValid({
      ...defaultCdcForm,
      name: 'mirror-sd',
      sourceId: SOURCE_ID,
      monitoredTables: 'public.orders',
      targetMode: '3',
      targetSinkId: SOURCE_ID,
      targetSinkDestination: 'public.orders_mirror',
      targetSinkKeyColumns: 'order_id',
      targetDeletePolicy: '2',
      softDeleteColumn: 'is_deleted',
      softDeleteTimestampColumn: 'deleted_at_utc',
      targetFieldMappings: [{ sourceColumn: 'src_id', targetColumn: 'order_id' }],
    })
    expect(valid).toBe(true)

    const missingSdCol = isCdcFormValid({
      ...defaultCdcForm,
      name: 'mirror-sd',
      sourceId: SOURCE_ID,
      monitoredTables: 'public.orders',
      targetMode: '3',
      targetSinkId: SOURCE_ID,
      targetSinkDestination: 'public.orders_mirror',
      targetSinkKeyColumns: 'order_id',
      targetDeletePolicy: '2',
      softDeleteColumn: '',
    })
    expect(missingSdCol).toBe(false)

    const body = toCdcBody({
      ...defaultCdcForm,
      name: 'mirror-sd',
      sourceId: SOURCE_ID,
      monitoredTables: 'public.orders',
      targetMode: '3',
      targetSinkId: SOURCE_ID,
      targetSinkDestination: 'public.orders_mirror',
      targetSinkKeyColumns: 'order_id',
      targetDeletePolicy: '2',
      softDeleteColumn: 'is_deleted',
      softDeleteTimestampColumn: 'deleted_at_utc',
      targetFieldMappings: [{ sourceColumn: 'src_id', targetColumn: 'order_id' }],
    })
    expect(body.targetDeletePolicy).toBe(2)
    expect(body.softDeleteColumn).toBe('is_deleted')
    expect(body.softDeleteTimestampColumn).toBe('deleted_at_utc')
    expect(body.targetFieldMappings).toEqual([{ sourceColumn: 'src_id', targetColumn: 'order_id' }])

    const roundTrip = toCdcForm(makeStream({
      targetMode: 3,
      targetSinkId: SOURCE_ID,
      targetSinkDestination: 'public.orders_mirror',
      targetSinkKeyColumns: 'order_id',
      targetDeletePolicy: 2,
      softDeleteColumn: 'is_deleted',
      softDeleteTimestampColumn: 'deleted_at_utc',
      targetFieldMappings: [{ sourceColumn: 'src_id', targetColumn: 'order_id' }],
    }))
    expect(roundTrip.targetDeletePolicy).toBe('2')
    expect(roundTrip.softDeleteColumn).toBe('is_deleted')
    expect(roundTrip.targetFieldMappings).toEqual([{ sourceColumn: 'src_id', targetColumn: 'order_id' }])
  })

  it('renders Lakehouse manifest, analytics SQL, and triggers manual compaction', async () => {
    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          destination: 'orders/raw',
          targetType: 5,
          targetExists: true,
          manifestDirectory: 'cdc/orders/raw/_swapmid/manifests',
          manifestLatestKey: 'cdc/orders/raw/_swapmid/latest.json',
          latestManifestJson: '{"checkpointLsn":"0/1A00","format":"parquet"}',
          lakehouseQuerySql: "-- DuckDB\nSELECT * FROM read_parquet('s3://lakehouse/cdc/orders/raw/**/*.parquet');",
          samplePayloadJson: '{"op":"insert"}',
          columns: [],
          summary: 'ObjectStorage parquet',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          compacted: true,
          compactedFileCount: 3,
          consolidatedRowCount: 3,
          message: '已合并 3 个 PARQUET 分片',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          destination: 'orders/raw',
          targetType: 5,
          targetExists: true,
          manifestDirectory: 'cdc/orders/raw/_swapmid/manifests',
          manifestLatestKey: 'cdc/orders/raw/_swapmid/latest.json',
          latestManifestJson: '{"compactionCount":1,"format":"parquet"}',
          lakehouseQuerySql: "-- DuckDB\nSELECT * FROM read_parquet('s3://lakehouse/cdc/orders/raw/**/*.parquet');",
          samplePayloadJson: '{"op":"insert"}',
          columns: [],
          summary: 'ObjectStorage parquet',
        }),
      })

    renderClient(makeStream({
      targetMode: 2,
      targetSinkId: SOURCE_ID,
      targetSinkName: 'minio-lakehouse',
      targetSinkDestination: 'orders/raw',
      lakehouseCompactMinFiles: 5,
    }))
    fireEvent.click(screen.getByRole('button', { name: '查看模板' }))

    expect(await screen.findByText(/cdc\/orders\/raw\/_swapmid\/manifests/)).toBeTruthy()
    expect(screen.getByText(/cdc\/orders\/raw\/_swapmid\/latest\.json/)).toBeTruthy()
    expect(screen.getByText(/"checkpointLsn":"0\/1A00"/)).toBeTruthy()
    expect(screen.getByText(/read_parquet/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '合并分片' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(
        `/api/proxy/cdc-streams/${STREAM_ID}/compact-lakehouse`,
        expect.objectContaining({ method: 'POST' }),
      )
      expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({
        variant: 'success',
        title: 'Lakehouse 分片合并完成',
      }))
    })
    expect(await screen.findByText(/"compactionCount":1/)).toBeTruthy()
  })

  it('supports SqlServerChangeTracking validation and lakehouseCompactMinFiles serialization', () => {
    expect(detectCdcSourceTypeFromSource('sql-1', [
      { id: 'sql-1', name: 'SQL Server Main', type: 0, config: '{"provider":"SqlServer"}' },
    ])).toBe('1')

    expect(isCdcFormValid({
      ...defaultCdcForm,
      name: 'sql-ct-orders',
      sourceId: 'sql-1',
      sourceType: '1',
      publicationName: '',
      slotName: '',
      monitoredTables: 'dbo.orders',
      targetMode: '2',
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/ct',
      lakehouseCompactMinFiles: '5',
    })).toBe(true)

    expect(isCdcFormValid({
      ...defaultCdcForm,
      name: 'sql-ct-orders',
      sourceId: 'sql-1',
      sourceType: '1',
      publicationName: '',
      slotName: '',
      monitoredTables: '',
      targetMode: '0',
    })).toBe(false)

    const body = toCdcBody({
      ...defaultCdcForm,
      name: 'sql-ct-orders',
      sourceId: 'sql-1',
      sourceType: '1',
      publicationName: '',
      slotName: '',
      monitoredTables: 'dbo.orders',
      targetMode: '2',
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/ct',
      lakehouseCompactMinFiles: '5',
    })
    expect(body.sourceType).toBe(1)
    expect(body.lakehouseCompactMinFiles).toBe(5)
    expect(body.publicationName).toBeNull()
  })

  it('supports MySqlBinlog detection, DeltaLake serialization, and round-trip mapping', () => {
    expect(detectCdcSourceTypeFromSource('mysql-1', [
      { id: 'mysql-1', name: 'MySQL 8.4 Orders', type: 0, config: '{"provider":"MySql"}' },
    ])).toBe('2')
    expect(detectCdcSourceTypeFromSource('maria-1', [
      { id: 'maria-1', name: 'MariaDB Analytics', type: 0, config: '{"provider":"MariaDb"}' },
    ])).toBe('2')

    const validMySqlDeltaForm = {
      ...defaultCdcForm,
      name: 'mysql-binlog-delta',
      sourceId: 'mysql-1',
      sourceType: '2' as const,
      publicationName: '',
      slotName: '',
      monitoredTables: 'swapmid_cdc.orders',
      targetMode: '2' as const,
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: '1' as const,
      lakehouseCompactMinFiles: '2',
    }
    expect(isCdcFormValid(validMySqlDeltaForm)).toBe(true)
    expect(isCdcFormValid({ ...validMySqlDeltaForm, monitoredTables: '' })).toBe(false)

    const body = toCdcBody(validMySqlDeltaForm)
    expect(body.sourceType).toBe(2)
    expect(body.lakehouseTableFormat).toBe(1)
    expect(body.lakehouseCompactMinFiles).toBe(2)
    expect(body.publicationName).toBeNull()
    expect(body.slotName).toBeNull()

    const roundTrip = toCdcForm(makeStream({
      sourceType: 2,
      publicationName: undefined,
      slotName: undefined,
      targetMode: 2,
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: 1,
      lakehouseCompactMinFiles: 2,
    }))
    expect(roundTrip.sourceType).toBe('2')
    expect(roundTrip.lakehouseTableFormat).toBe('1')
  })

  it('renders Delta Lake version badge, _delta_log path, and delta_scan SQL in template dialog', async () => {
    mocks.fetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        destination: 'orders/delta',
        targetType: 5,
        targetExists: true,
        manifestDirectory: 'cdc/orders/delta/_swapmid/manifests',
        manifestLatestKey: 'cdc/orders/delta/_swapmid/latest.json',
        deltaLogDirectory: 'cdc/orders/delta/_delta_log',
        latestDeltaVersion: 2,
        latestManifestJson: '{"latestDeltaVersion":2,"format":"parquet"}',
        lakehouseQuerySql: "-- DuckDB Delta Lake\nSELECT * FROM delta_scan('s3://lakehouse/cdc/orders/delta');",
        samplePayloadJson: '{"op":"insert"}',
        columns: [],
        summary: 'ObjectStorage parquet (Delta Lake v2)',
      }),
    })

    renderClient(makeStream({
      sourceType: 2,
      targetMode: 2,
      targetSinkId: SOURCE_ID,
      targetSinkName: 'minio-delta',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: 1,
    }))
    fireEvent.click(screen.getByRole('button', { name: '查看模板' }))

    expect(await screen.findByText('Delta Lake v2')).toBeTruthy()
    expect(screen.getByText(/Delta Log: cdc\/orders\/delta\/_delta_log/)).toBeTruthy()
    expect(screen.getByText(/delta_scan\('s3:\/\/lakehouse\/cdc\/orders\/delta'\)/)).toBeTruthy()
  })
})
