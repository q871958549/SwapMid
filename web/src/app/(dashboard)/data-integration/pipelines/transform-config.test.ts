import { describe, expect, it } from 'vitest'
import {
  ETL_WRITE_MODES,
  isReceiptMode,
  isUpsertConfigValid,
  isUpsertMode,
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
