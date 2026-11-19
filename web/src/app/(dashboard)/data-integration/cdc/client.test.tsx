import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import type { CdcStreamDto } from '@/lib/api/types'
import { CdcStreamsClient } from './client'
import { CdcStatusCell } from './cdc-row-actions'
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
      catalogType: '0',
      catalogSinkId: '',
      catalogNamespace: '',
      catalogTableName: '',
      schemaDriftPolicy: '0',
      maxFreshnessLagSeconds: '',
      maxAllowedWalLagMb: '',
      requiredColumns: '',
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

  it('validates External Open Catalog configuration and round-trips catalog fields', () => {
    const validCatalogForm = {
      ...defaultCdcForm,
      name: 'mysql-uc-delta',
      sourceId: SOURCE_ID,
      sourceType: '2' as const,
      monitoredTables: 'swapmid_cdc.orders',
      targetMode: '2' as const,
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: '1' as const,
      catalogType: '1' as const,
      catalogSinkId: 'rest-uc-1',
      catalogNamespace: 'analytics.swapmid_cdc',
      catalogTableName: 'orders',
    }
    expect(isCdcFormValid(validCatalogForm)).toBe(true)
    expect(isCdcFormValid({ ...validCatalogForm, catalogSinkId: '' })).toBe(false)
    expect(isCdcFormValid({ ...validCatalogForm, catalogNamespace: '..bad' })).toBe(false)
    expect(isCdcFormValid({ ...validCatalogForm, catalogTableName: 'bad table!' })).toBe(false)

    const body = toCdcBody(validCatalogForm)
    expect(body.catalogType).toBe(1)
    expect(body.catalogSinkId).toBe('rest-uc-1')
    expect(body.catalogNamespace).toBe('analytics.swapmid_cdc')
    expect(body.catalogTableName).toBe('orders')

    const roundTrip = toCdcForm(makeStream({
      targetMode: 2,
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: 1,
      catalogType: 2,
      catalogSinkId: 'rest-iceberg-1',
      catalogNamespace: 'swapmid_cdc',
      catalogTableName: 'orders_ic',
    }))
    expect(roundTrip.catalogType).toBe('2')
    expect(roundTrip.catalogSinkId).toBe('rest-iceberg-1')
    expect(roundTrip.catalogNamespace).toBe('swapmid_cdc')
    expect(roundTrip.catalogTableName).toBe('orders_ic')
  })

  it('opens Time-Travel dialog, switches version v1, and triggers Sync Catalog action', async () => {
    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          destination: 'orders/delta',
          tableFormat: 1,
          deltaLogDirectory: 'cdc/orders/delta/_delta_log',
          currentVersion: 2,
          selectedVersion: 2,
          totalCommitCount: 3,
          selectedActiveFileCount: 1,
          selectedTotalBytes: 2048,
          selectedTotalRecordCount: 15,
          commits: [
            { version: 2, timestampUtc: '2026-11-19T00:05:00Z', operation: 'OPTIMIZE', addedFileCount: 1, removedFileCount: 2, totalRecordCount: 15, deltaCommitKey: 'k2' },
            { version: 1, timestampUtc: '2026-11-19T00:02:00Z', operation: 'STREAMING UPDATE', addedFileCount: 1, removedFileCount: 0, totalRecordCount: 5, deltaCommitKey: 'k1' },
          ],
          activeFiles: [
            { relativePath: 'compact_2.parquet', objectKey: 'cdc/orders/delta/compact_2.parquet', sizeBytes: 2048, recordCount: 15, addedAtVersion: 2 },
          ],
          timeTravelQuerySql: 'SELECT * FROM delta.`s3a://swapmid-lake/cdc/orders/delta` VERSION AS OF 2;',
          summary: 'Delta Lake v2',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          destination: 'orders/delta',
          tableFormat: 1,
          deltaLogDirectory: 'cdc/orders/delta/_delta_log',
          currentVersion: 2,
          selectedVersion: 1,
          totalCommitCount: 3,
          selectedActiveFileCount: 2,
          selectedTotalBytes: 1536,
          selectedTotalRecordCount: 15,
          commits: [
            { version: 2, timestampUtc: '2026-11-19T00:05:00Z', operation: 'OPTIMIZE', addedFileCount: 1, removedFileCount: 2, totalRecordCount: 15, deltaCommitKey: 'k2' },
            { version: 1, timestampUtc: '2026-11-19T00:02:00Z', operation: 'STREAMING UPDATE', addedFileCount: 1, removedFileCount: 0, totalRecordCount: 5, deltaCommitKey: 'k1' },
          ],
          activeFiles: [
            { relativePath: 'part_0.parquet', objectKey: 'cdc/orders/delta/part_0.parquet', sizeBytes: 1024, recordCount: 10, addedAtVersion: 0 },
            { relativePath: 'part_1.parquet', objectKey: 'cdc/orders/delta/part_1.parquet', sizeBytes: 512, recordCount: 5, addedAtVersion: 1 },
          ],
          timeTravelQuerySql: 'SELECT * FROM delta.`s3a://swapmid-lake/cdc/orders/delta` VERSION AS OF 1;',
          summary: 'Delta Lake v1',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          catalogType: 1,
          synced: true,
          syncedVersion: 2,
          catalogTableIdentifier: 'analytics.swapmid_cdc.orders',
          syncedAtUtc: '2026-11-19T00:06:00Z',
          message: '已同步表 analytics.swapmid_cdc.orders（v2）',
        }),
      })

    renderClient(makeStream({
      sourceType: 2,
      targetMode: 2,
      targetSinkId: SOURCE_ID,
      targetSinkType: 5,
      targetSinkName: 'minio-delta',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: 1,
      catalogType: 1,
      catalogSinkId: 'rest-uc-1',
      catalogNamespace: 'analytics.swapmid_cdc',
      catalogTableName: 'orders',
    }))

    fireEvent.click(screen.getByRole('button', { name: 'Time-Travel' }))
    expect(await screen.findByText('compact_2.parquet')).toBeTruthy()
    expect(screen.getByText(/VERSION AS OF 2/)).toBeTruthy()

    const v1Button = screen.getByRole('button', { name: /STREAMING UPDATE/ })
    fireEvent.click(v1Button)
    expect(await screen.findByText('part_1.parquet')).toBeTruthy()
    expect(screen.getByText(/VERSION AS OF 1/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '关闭' }))
    await waitFor(() => {
      expect(screen.queryByText(/VERSION AS OF 1/)).toBeNull()
    })

    fireEvent.click(screen.getByRole('button', { name: '同步目录' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/sync-catalog`, { method: 'POST' })
    })
  })

  it('loads zero-infra Lakehouse preview rows inside Time-Travel dialog and evaluates Data Contract SLA', async () => {
    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          destination: 'orders/delta',
          tableFormat: 1,
          deltaLogDirectory: 'cdc/orders/delta/_delta_log',
          currentVersion: 1,
          selectedVersion: 1,
          totalCommitCount: 2,
          selectedActiveFileCount: 1,
          selectedTotalBytes: 2048,
          selectedTotalRecordCount: 2,
          commits: [
            { version: 1, timestampUtc: '2026-11-19T00:05:00Z', operation: 'STREAMING UPDATE', addedFileCount: 1, removedFileCount: 0, totalRecordCount: 2, deltaCommitKey: 'k1' },
          ],
          activeFiles: [
            { relativePath: 'part_1.parquet', objectKey: 'cdc/orders/delta/part_1.parquet', sizeBytes: 2048, recordCount: 2, addedAtVersion: 1 },
          ],
          timeTravelQuerySql: 'SELECT * FROM delta.`s3a://swapmid-lake/cdc/orders/delta` VERSION AS OF 1;',
          summary: 'Delta Lake v1',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          destination: 'orders/delta',
          tableFormat: 1,
          fileFormat: 'parquet',
          selectedVersion: 1,
          scannedFileCount: 1,
          totalActiveFileCount: 1,
          missingFileCount: 0,
          rowCount: 1,
          truncated: false,
          columns: ['op', 'order_no', 'after_json'],
          rows: [{ op: 'c', order_no: 'ORD-2026-9999', after_json: '{"amount":880}' }],
          sourceFiles: ['cdc/orders/delta/part_1.parquet'],
          summary: '已加载 v1 样本 1 行',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          streamName: 'orders-cdc',
          status: 1,
          violated: false,
          freshnessLagSeconds: 12,
          maxFreshnessLagSeconds: 300,
          requiredColumns: ['id', 'order_no'],
          missingRequiredColumns: [],
          schemaDriftDetected: false,
          violations: [],
          evaluatedAtUtc: '2026-11-19T00:06:00Z',
          summary: '数据契约 SLA 全部达标',
        }),
      })

    const stream = makeStream({
      sourceType: 2,
      targetMode: 2,
      targetSinkId: SOURCE_ID,
      targetSinkType: 5,
      targetSinkName: 'minio-delta',
      targetSinkDestination: 'orders/delta',
      lakehouseTableFormat: 1,
      maxFreshnessLagSeconds: 300,
      requiredColumns: 'id,order_no',
      contractStatus: 3,
      contractViolated: true,
      contractViolationSummary: 'Lag exceeded',
    })

    const { unmount } = render(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <CdcStatusCell stream={stream} />
      </NextIntlClientProvider>,
    )
    expect(screen.getByText('SLA 违约')).toBeTruthy()
    unmount()

    expect(isCdcFormValid({ ...defaultCdcForm, name: 's1', sourceId: SOURCE_ID, maxFreshnessLagSeconds: '5' })).toBe(false)
    expect(isCdcFormValid({ ...defaultCdcForm, name: 's1', sourceId: SOURCE_ID, maxFreshnessLagSeconds: '120', maxAllowedWalLagMb: '50', requiredColumns: 'id,order_no' })).toBe(true)

    renderClient(stream)

    fireEvent.click(screen.getByRole('button', { name: 'Time-Travel' }))
    expect(await screen.findByText('part_1.parquet')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '加载实时数据预览' }))
    expect(await screen.findByText('ORD-2026-9999')).toBeTruthy()
    expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/lakehouse-preview?version=1&maxRows=20`)

    fireEvent.click(screen.getByRole('button', { name: '关闭' }))
    await waitFor(() => {
      expect(screen.queryByText('ORD-2026-9999')).toBeNull()
    })

    fireEvent.click(screen.getByRole('button', { name: '契约校验' }))
    await waitFor(() => {
      expect(mocks.fetch).toHaveBeenCalledWith(`/api/proxy/cdc-streams/${STREAM_ID}/evaluate-contract`, { method: 'POST' })
    })
  })

  it('supports PushWebhook source validation, ApacheIceberg v2 format, and Iceberg Time-Travel + live preview', async () => {
    expect(detectCdcSourceTypeFromSource('wh-1', [
      { id: 'wh-1', name: 'Webhook Secret Source', type: 1, config: '{}' },
    ])).toBe('3')

    const validPushIcebergForm = {
      ...defaultCdcForm,
      name: 'orders-webhook-iceberg',
      sourceId: 'wh-1',
      sourceType: '3' as const,
      publicationName: '',
      slotName: '',
      monitoredTables: '',
      initialSnapshotEnabled: true,
      targetMode: '2' as const,
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/iceberg',
      lakehouseTableFormat: '2' as const,
      catalogType: '2' as const,
      catalogSinkId: 'rest-ic-1',
      catalogNamespace: 'swapmid_iceberg',
      catalogTableName: 'orders_v2',
    }
    expect(isCdcFormValid(validPushIcebergForm)).toBe(true)

    const body = toCdcBody(validPushIcebergForm)
    expect(body.sourceType).toBe(3)
    expect(body.lakehouseTableFormat).toBe(2)
    expect(body.initialSnapshotEnabled).toBe(false)
    expect(body.autoCreatePublicationAndSlot).toBe(false)

    const roundTrip = toCdcForm(makeStream({
      sourceType: 3,
      targetMode: 2,
      targetSinkId: 's3-1',
      targetSinkDestination: 'orders/iceberg',
      lakehouseTableFormat: 2,
    }))
    expect(roundTrip.sourceType).toBe('3')
    expect(roundTrip.lakehouseTableFormat).toBe('2')

    mocks.fetch
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          destination: 'orders/iceberg',
          tableFormat: 2,
          deltaLogDirectory: 'warehouse/orders/iceberg/metadata',
          currentVersion: 2,
          selectedVersion: 2,
          totalCommitCount: 2,
          selectedActiveFileCount: 1,
          selectedTotalBytes: 2048,
          selectedTotalRecordCount: 4,
          commits: [
            { version: 2, timestampUtc: '2026-11-19T05:00:00Z', operation: 'REPLACE', addedFileCount: 1, removedFileCount: 2, totalRecordCount: 4, deltaCommitKey: 'warehouse/orders/iceberg/metadata/v2.metadata.json' },
          ],
          activeFiles: [
            { relativePath: 'compact_iceberg.parquet', objectKey: 'warehouse/orders/iceberg/compact_iceberg.parquet', sizeBytes: 2048, recordCount: 4, addedAtVersion: 2 },
          ],
          timeTravelQuerySql: "SELECT * FROM iceberg_scan('s3://lake/warehouse/orders/iceberg/metadata/v2.metadata.json') LIMIT 100;",
          summary: 'Apache Iceberg v2 · v2',
        }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          streamId: STREAM_ID,
          destination: 'orders/iceberg',
          tableFormat: 2,
          fileFormat: 'parquet',
          selectedVersion: 2,
          scannedFileCount: 1,
          totalActiveFileCount: 1,
          missingFileCount: 0,
          rowCount: 1,
          truncated: false,
          columns: ['op', 'after_json'],
          rows: [{ op: 'insert', after_json: '{"order_no":"IC-2026-01"}' }],
          sourceFiles: ['warehouse/orders/iceberg/compact_iceberg.parquet'],
          summary: '已加载 v2 样本 1 行',
        }),
      })

    renderClient(makeStream({
      sourceType: 3,
      status: 1,
      targetMode: 2,
      targetSinkId: SOURCE_ID,
      targetSinkType: 5,
      targetSinkName: 'minio-iceberg',
      targetSinkDestination: 'orders/iceberg',
      lakehouseTableFormat: 2,
    }))

    expect(screen.queryByRole('button', { name: '立即捕获' })).toBeNull()
    expect(screen.queryByRole('button', { name: '全量快照' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Time-Travel' }))
    expect(await screen.findByText(/Iceberg Metadata: warehouse\/orders\/iceberg\/metadata/)).toBeTruthy()
    expect(screen.getByText('compact_iceberg.parquet')).toBeTruthy()
    expect(screen.getByText(/iceberg_scan/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: '加载实时数据预览' }))
    expect(await screen.findByText('{"order_no":"IC-2026-01"}')).toBeTruthy()
  })
})
