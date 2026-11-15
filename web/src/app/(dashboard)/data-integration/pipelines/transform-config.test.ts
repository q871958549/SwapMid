import { describe, expect, it } from 'vitest'
import {
  ETL_WRITE_MODES,
  buildAutoColumnMappings,
  getAllowedWriteModesForTarget,
  getDestinationKindForTarget,
  isMappingTypeIncompatible,
  isReceiptMode,
  isUpsertConfigValid,
  isUpsertMode,
  isValidDestinationForTarget,
  isWriteModeAllowedForTarget,
  parseEtlTransformConfig,
  parseKeyColumnsText,
  serializeEtlTransformConfig,
  supportsTargetReceipt,
} from './transform-config'

const NON_UPSERT_MODES = ETL_WRITE_MODES.filter(mode => !isUpsertMode(mode))
const UPSERT_MODES = ETL_WRITE_MODES.filter(mode => isUpsertMode(mode))

describe('ETL transform config', () => {
  it.each(NON_UPSERT_MODES)('完整往返受支持非 Upsert mode %s', (mode) => {
    const parsed = parseEtlTransformConfig(JSON.stringify({
      sourceQuery: ' SELECT 1 ',
      targetTable: 'dbo.Output',
      mode,
    }))
    expect(parsed).toMatchObject({ mode, transformConfigValid: true })
    expect(serializeEtlTransformConfig(parsed)).toBe(JSON.stringify({
      sourceQuery: 'SELECT 1',
      targetTable: 'dbo.Output',
      mode,
    }))
  })

  it.each(UPSERT_MODES)('完整往返受支持 Upsert mode %s（含 keyColumns 与 watermarkColumn）', (mode) => {
    const parsed = parseEtlTransformConfig(JSON.stringify({
      sourceQuery: ' SELECT id, updated_at, amount FROM orders ',
      targetTable: 'public.orders_mirror',
      mode,
      keyColumns: ['id'],
      watermarkColumn: 'updated_at',
    }))
    expect(parsed).toMatchObject({
      mode,
      keyColumnsText: 'id',
      watermarkColumn: 'updated_at',
      transformConfigValid: true,
    })
    expect(isReceiptMode(mode)).toBe(mode === 'upsert-receipt-v1')
    expect(serializeEtlTransformConfig(parsed)).toBe(JSON.stringify({
      sourceQuery: 'SELECT id, updated_at, amount FROM orders',
      targetTable: 'public.orders_mirror',
      mode,
      keyColumns: ['id'],
      watermarkColumn: 'updated_at',
    }))
  })

  it('校验逗号分隔业务主键与水位线合法性', () => {
    expect(parseKeyColumnsText(' tenant_id , external_code ')).toEqual(['tenant_id', 'external_code'])
    expect(parseKeyColumnsText('id, ID')).toBeNull()
    expect(parseKeyColumnsText('1bad_col')).toBeNull()
    expect(isUpsertConfigValid({
      mode: 'upsert',
      keyColumnsText: 'order_id',
      watermarkColumn: 'updated_at',
    })).toBe(true)
    expect(isUpsertConfigValid({
      mode: 'upsert',
      keyColumnsText: 'order_id',
      watermarkColumn: 'ORDER_ID',
    })).toBe(false)
  })

  it.each([
    '',
    '{bad-json',
    JSON.stringify({ mode: 'future-receipt-v2' }),
    JSON.stringify({ mode: 'upsert' }),
    JSON.stringify({ mode: 'upsert', keyColumns: [] }),
    JSON.stringify({ mode: 'upsert', keyColumns: ['id', 'ID'] }),
    JSON.stringify({ mode: 'upsert', keyColumns: ['id'], watermarkColumn: 'id' }),
    JSON.stringify({ mode: 'append', keyColumns: ['id'] }),
    JSON.stringify({ mode: 'truncate', watermarkColumn: 'updated_at' }),
    JSON.stringify({ sourceQuery: 42, targetTable: 'dbo.Output' }),
    JSON.stringify({ sourceQuery: null, targetTable: 'dbo.Output' }),
    JSON.stringify({ sourceQuery: 'SELECT 1', targetTable: null }),
  ])('畸形或未知配置失败关闭且禁止序列化', (config) => {
    const parsed = parseEtlTransformConfig(config)
    expect(parsed.mode).toBe('unsupported')
    expect(parsed.transformConfigValid).toBe(false)
    expect(() => serializeEtlTransformConfig(parsed)).toThrow()
  })

  it('旧配置省略 mode 时按 append 读取', () => {
    expect(parseEtlTransformConfig(JSON.stringify({ sourceQuery: 'SELECT 1' })))
      .toMatchObject({ mode: 'append', transformConfigValid: true })
  })

  it.each(NON_UPSERT_MODES.filter(mode => mode !== 'append'))(
    '非默认 mode %s 即使查询和目标表为空也必须持久化，不能静默降级为 append',
    (mode) => {
      const serialized = serializeEtlTransformConfig({
        sourceQuery: '',
        targetTable: '',
        mode,
        transformConfigValid: true,
      })

      expect(serialized).toBe(JSON.stringify({ sourceQuery: '', targetTable: '', mode }))
      expect(parseEtlTransformConfig(serialized)).toMatchObject({
        mode,
        transformConfigValid: true,
      })
    },
  )

  it('只有空白默认 append 配置沿用 null 兼容表示', () => {
    expect(serializeEtlTransformConfig({
      sourceQuery: '  ',
      targetTable: '',
      mode: 'append',
      transformConfigValid: true,
    })).toBeNull()
  })

  it.each([null, '', 42, false])('显式畸形 mode %j 必须失败关闭，不能冒充省略 mode', (mode) => {
    const parsed = parseEtlTransformConfig(JSON.stringify({
      sourceQuery: 'SELECT 1',
      targetTable: 'dbo.Output',
      mode,
    }))

    expect(parsed).toMatchObject({ mode: 'unsupported', transformConfigValid: false })
    expect(() => serializeEtlTransformConfig(parsed)).toThrow()
  })
})

describe('Target Receipt v1 target capability', () => {
  it.each(['sqlserver', 'mssql', 'postgresql', 'postgres', 'npgsql'])(
    '只接受严格 TLS 的 %s 目标',
    (provider) => {
      expect(supportsTargetReceipt({
        type: 0,
        config: JSON.stringify({ provider, encrypt: true, trustServerCertificate: false }),
      })).toBe(true)
    },
  )

  it('兼容缺省 provider 的既有 SQL Server 配置，但仍要求严格 TLS', () => {
    expect(supportsTargetReceipt({
      type: 0,
      config: JSON.stringify({ encrypt: true, trustServerCertificate: false }),
    })).toBe(true)
  })

  it.each([
    { type: 0, config: JSON.stringify({ provider: 'mysql', encrypt: true, trustServerCertificate: false }) },
    { type: 0, config: JSON.stringify({ provider: null, encrypt: true, trustServerCertificate: false }) },
    { type: 0, config: JSON.stringify({ provider: 42, encrypt: true, trustServerCertificate: false }) },
    { type: 0, config: JSON.stringify({ provider: 'sqlserver', encrypt: false, trustServerCertificate: false }) },
    { type: 0, config: JSON.stringify({ provider: 'postgresql', encrypt: true, trustServerCertificate: true }) },
    { type: 0, config: JSON.stringify({ provider: 'sqlserver', encrypt: true }) },
    { type: 0, config: JSON.stringify({ provider: 'sqlserver', trustServerCertificate: false }) },
    { type: 0, config: JSON.stringify({ provider: 'sqlserver', encrypt: 1, trustServerCertificate: false }) },
    { type: 0, config: JSON.stringify({ provider: 'sqlserver', encrypt: true, trustServerCertificate: 0 }) },
    { type: 1, config: JSON.stringify({ provider: 'sqlserver', encrypt: true, trustServerCertificate: false }) },
    { type: 0, config: null },
    { type: 0, config: '{bad-json' },
  ])('不受支持或未认证目标不可选 receipt-v1', (target) => {
    expect(supportsTargetReceipt(target)).toBe(false)
  })
})

describe('ETL columnMappings & field mapper helpers', () => {
  it('往返保留显式 columnMappings 并在空映射时省略字段以兼容旧配置', () => {
    const rawWithMappings = JSON.stringify({
      sourceQuery: 'SELECT cust_id, cust_name FROM src_orders',
      targetTable: 'dbo.dst_orders',
      mode: 'upsert',
      keyColumns: ['id'],
      watermarkColumn: 'updated_at',
      columnMappings: [
        { sourceColumn: 'cust_id', targetColumn: 'id' },
        { sourceColumn: 'cust_name', targetColumn: 'customer_name' },
      ],
    })

    const parsed = parseEtlTransformConfig(rawWithMappings)
    expect(parsed.transformConfigValid).toBe(true)
    expect(parsed.columnMappings).toEqual([
      { sourceColumn: 'cust_id', targetColumn: 'id' },
      { sourceColumn: 'cust_name', targetColumn: 'customer_name' },
    ])

    const reserialized = JSON.parse(serializeEtlTransformConfig(parsed) ?? '{}') as Record<string, unknown>
    expect(reserialized.columnMappings).toEqual([
      { sourceColumn: 'cust_id', targetColumn: 'id' },
      { sourceColumn: 'cust_name', targetColumn: 'customer_name' },
    ])

    const withoutMappings = serializeEtlTransformConfig({
      ...parsed,
      columnMappings: [],
    })
    expect(withoutMappings).not.toContain('columnMappings')
  })

  it('拒绝非法 columnMappings 结构、无效标识符及大小写重复 targetColumn', () => {
    expect(
      parseEtlTransformConfig(JSON.stringify({
        sourceQuery: 'SELECT 1',
        targetTable: 'dbo.t',
        mode: 'append',
        columnMappings: 'invalid',
      })).transformConfigValid,
    ).toBe(false)

    expect(
      parseEtlTransformConfig(JSON.stringify({
        sourceQuery: 'SELECT 1',
        targetTable: 'dbo.t',
        mode: 'append',
        columnMappings: [{ sourceColumn: '123bad', targetColumn: 'id' }],
      })).transformConfigValid,
    ).toBe(false)

    expect(
      parseEtlTransformConfig(JSON.stringify({
        sourceQuery: 'SELECT 1',
        targetTable: 'dbo.t',
        mode: 'append',
        columnMappings: [
          { sourceColumn: 'a', targetColumn: 'CustomerId' },
          { sourceColumn: 'b', targetColumn: 'customerid' },
        ],
      })).transformConfigValid,
    ).toBe(false)

    expect(() =>
      serializeEtlTransformConfig({
        sourceQuery: 'SELECT a',
        targetTable: 'dbo.t',
        mode: 'append',
        columnMappings: [
          { sourceColumn: 'a', targetColumn: 'Id' },
          { sourceColumn: 'b', targetColumn: 'id' },
        ],
        transformConfigValid: true,
      }),
    ).toThrow()
  })

  it('buildAutoColumnMappings 按大小写精确匹配优先，其次忽略下划线与连字符匹配', () => {
    const mappings = buildAutoColumnMappings(
      [
        { name: 'id', dataType: 'int' },
        { name: 'customer_name', dataType: 'varchar' },
        { name: 'orderAmount', dataType: 'decimal' },
      ],
      [
        { name: 'Id', dataType: 'int4' },
        { name: 'CustomerName', dataType: 'text' },
        { name: 'order_amount', dataType: 'numeric' },
        { name: 'unmatched_col', dataType: 'text' },
      ],
    )

    expect(mappings).toEqual([
      { sourceColumn: 'id', targetColumn: 'Id' },
      { sourceColumn: 'customer_name', targetColumn: 'CustomerName' },
      { sourceColumn: 'orderAmount', targetColumn: 'order_amount' },
    ])
  })

  it('isMappingTypeIncompatible 识别跨类型族冲突并允许数值互转及文本目标', () => {
    expect(isMappingTypeIncompatible('varchar(64)', 'int4')).toBe(true)
    expect(isMappingTypeIncompatible('timestamp', 'bool')).toBe(true)
    expect(isMappingTypeIncompatible('int4', 'numeric(18,2)')).toBe(false)
    expect(isMappingTypeIncompatible('int4', 'varchar(64)')).toBe(false)
    expect(isMappingTypeIncompatible('int4', 'Nullable(String)')).toBe(false)
    expect(isMappingTypeIncompatible(null, 'uuid')).toBe(false)
  })
})

describe('Analytical & Streaming Sink Targets (ClickHouse / Kafka / ObjectStorage)', () => {
  it('按目标类型限制允许的写入模式', () => {
    expect(getAllowedWriteModesForTarget(0)).toEqual(ETL_WRITE_MODES)
    expect(getAllowedWriteModesForTarget(4)).toEqual(['append', 'truncate'])
    expect(getAllowedWriteModesForTarget(3)).toEqual(['append'])
    expect(getAllowedWriteModesForTarget(5)).toEqual(['append'])
    expect(getAllowedWriteModesForTarget(1)).toEqual([])

    expect(isWriteModeAllowedForTarget('truncate', 4)).toBe(true)
    expect(isWriteModeAllowedForTarget('upsert', 4)).toBe(false)
    expect(isWriteModeAllowedForTarget('append-receipt-v1', 4)).toBe(false)
    expect(isWriteModeAllowedForTarget('append', 3)).toBe(true)
    expect(isWriteModeAllowedForTarget('truncate', 3)).toBe(false)
    expect(isWriteModeAllowedForTarget('append', 5)).toBe(true)
    expect(isWriteModeAllowedForTarget('upsert', 5)).toBe(false)
  })

  it('按目标类型校验 Topic / 对象键前缀 / 表名', () => {
    expect(getDestinationKindForTarget(0)).toBe('table')
    expect(getDestinationKindForTarget(4)).toBe('table')
    expect(getDestinationKindForTarget(3)).toBe('topic')
    expect(getDestinationKindForTarget(5)).toBe('prefix')

    expect(isValidDestinationForTarget('analytics.orders_daily', 4)).toBe(true)
    expect(isValidDestinationForTarget('orders/daily', 4)).toBe(false)

    expect(isValidDestinationForTarget('orders.events-v1', 3)).toBe(true)
    expect(isValidDestinationForTarget('..', 3)).toBe(false)
    expect(isValidDestinationForTarget('orders/events', 3)).toBe(false)

    expect(isValidDestinationForTarget('erp/orders/daily', 5)).toBe(true)
    expect(isValidDestinationForTarget('/leading-slash', 5)).toBe(false)
    expect(isValidDestinationForTarget('erp/../secret', 5)).toBe(false)
  })
})
