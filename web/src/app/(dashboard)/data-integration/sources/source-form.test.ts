import { describe, expect, it } from 'vitest'
import {
  buildRelationalConfig,
  changeDataSourceType,
  createDefaultDataSourceForm,
  isRelationalConfigReadyToSave,
  readRelationalConfig,
  requiresNewRelationalPassword,
  toDataSourceBody,
  toDataSourceForm,
  type DataSource,
} from './source-form'

const relationalSource: DataSource = {
  id: 'source-1',
  name: 'Orders-DB',
  description: null,
  type: 0,
  config: JSON.stringify({
    provider: 'sqlserver',
    host: 'db1.example.internal',
    port: 1433,
    database: 'orders_db',
    targetResourceGroup: 'erp-orders',
    username: 'etl_reader',
    password: 'must-not-reach-the-form',
    encrypt: false,
    trustServerCertificate: true,
  }),
  hasSecret: true,
  isEnabled: true,
  creationTime: '2026-07-27T00:00:00Z',
}

describe('DataSource 表单敏感配置边界', () => {
  it('每次创建默认表单都使用独立的关系型配置对象', () => {
    const first = createDefaultDataSourceForm()
    const second = createDefaultDataSourceForm()

    first.relational.host = 'changed-locally'

    expect(second.relational.host).toBe('')
    expect(first.relational).not.toBe(second.relational)
  })

  it('读取关系型配置时始终清空密码且只用 hasSecret 表示已配置', () => {
    const relational = readRelationalConfig(relationalSource.config)
    const form = toDataSourceForm(relationalSource)

    expect(relational.password).toBe('')
    expect(form.relational.password).toBe('')
    expect(form.hasSecret).toBe(true)
    expect(form.relational.host).toBe('db1.example.internal')
    expect(form.relational.targetResourceGroup).toBe('erp-orders')
  })

  it('targetResourceGroup 使用固定 JSON 键名并在往返时裁剪空白', () => {
    const serialized = buildRelationalConfig({
      ...readRelationalConfig(relationalSource.config),
      targetResourceGroup: '  erp-orders  ',
    })
    const parsed = JSON.parse(serialized) as Record<string, unknown>

    expect(parsed.targetResourceGroup).toBe('erp-orders')
    expect(readRelationalConfig(serialized).targetResourceGroup).toBe('erp-orders')
  })

  it('旧关系型配置没有 targetResourceGroup 时保持为空并禁止保存', () => {
    const config = readRelationalConfig(JSON.stringify({
      provider: 'sqlserver',
      host: 'db1.example.internal',
      port: 1433,
      database: 'orders_db',
    }))

    expect(config.targetResourceGroup).toBe('')
    expect(isRelationalConfigReadyToSave(config)).toBe(false)
    expect(isRelationalConfigReadyToSave({
      ...config,
      targetResourceGroup: 'erp-orders',
    })).toBe(true)
  })

  it('关系型编辑未输入新密码时不向更新请求写入 password 键', () => {
    const form = toDataSourceForm({
      ...relationalSource,
      config: JSON.stringify({
        provider: 'sqlserver',
        host: 'db1.example.internal',
        port: 1433,
        database: 'orders_db',
        username: 'etl_reader',
        encrypt: true,
      }),
    })

    form.relational.password = '   '
    const body = toDataSourceBody(form)
    const config = JSON.parse(body.config) as Record<string, unknown>

    expect(config.password).toBeUndefined()
    expect(config.host).toBe('db1.example.internal')
    expect(config.encrypt).toBe(true)
  })

  it('只有用户明确输入新密码时才将 password 写入请求', () => {
    const form = toDataSourceForm(relationalSource)

    const config = JSON.parse(buildRelationalConfig({
      ...form.relational,
      password: 'replacement-secret',
    })) as Record<string, unknown>

    expect(config.password).toBe('replacement-secret')
    expect(config.url).toBeUndefined()
    expect(config.driverClassName).toBeUndefined()
  })

  it.each([
    ['provider', { provider: 'mysql' }],
    ['host', { host: 'db2.example.internal' }],
    ['port', { port: '1434' }],
    ['database', { database: 'another-db' }],
    ['username', { username: 'another-user' }],
    ['encrypt', { encrypt: true }],
    ['trustServerCertificate', { trustServerCertificate: false }],
  ])('修改关系型凭据作用域字段 %s 时要求显式输入新密码', (_field, patch) => {
    const form = toDataSourceForm({
      ...relationalSource,
      config: JSON.stringify({
        provider: 'sqlserver',
        host: 'db1.example.internal',
        port: 1433,
        database: 'orders_db',
        username: 'etl_reader',
      }),
    })
    form.relational = { ...form.relational, ...patch }

    expect(requiresNewRelationalPassword(form, relationalSource)).toBe(true)
  })

  it('关系型凭据作用域仅有等价格式变化时仍可沿用密码', () => {
    const form = toDataSourceForm(relationalSource)
    form.relational = {
      ...form.relational,
      provider: 'MSSQL',
      host: ' db1.example.internal. ',
      port: '',
      database: ' orders_db ',
      username: ' etl_reader ',
    }

    expect(requiresNewRelationalPassword(form, relationalSource)).toBe(false)
  })

  it('只修改 targetResourceGroup 不扩大凭据作用域且不要求重输密码', () => {
    const form = toDataSourceForm(relationalSource)
    form.relational.targetResourceGroup = 'erp-orders-alias-group'

    expect(requiresNewRelationalPassword(form, relationalSource)).toBe(false)
    const body = toDataSourceBody(form)
    const config = JSON.parse(body.config) as Record<string, unknown>
    expect(config.targetResourceGroup).toBe('erp-orders-alias-group')
    expect(config.password).toBeUndefined()
  })

  it('旧配置即使没有已保存密码，作用域变化仍要求显式新密码', () => {
    const source = {
      ...relationalSource,
      hasSecret: false,
      config: JSON.stringify({
        provider: 'sqlserver',
        host: 'db1.example.internal',
        port: 1433,
        database: 'orders_db',
        username: 'etl_reader',
        encrypt: false,
        trustServerCertificate: true,
      }),
    }
    const form = toDataSourceForm(source)
    form.relational.host = 'db2.example.internal'

    expect(requiresNewRelationalPassword(form, source)).toBe(true)
  })

  it('REST/File 的空对象响应不补伪造秘密占位值', () => {
    const form = toDataSourceForm({
      ...relationalSource,
      type: 1,
      config: '{}',
      hasSecret: true,
    })

    const body = toDataSourceBody(form)

    expect(body.config).toBe('{}')
    expect(body.config).not.toContain('configured')
    expect(body.config).not.toContain('********')
  })

  it('切换数据源类型时清空另一类型配置且不沿用旧秘密状态', () => {
    const form = toDataSourceForm(relationalSource)
    form.config = '{"provider":"sqlserver","host":"db1.example.internal"}'

    const changed = changeDataSourceType(form, '1')

    expect(changed.type).toBe('1')
    expect(changed.config).toBe('{}')
    expect(changed.hasSecret).toBe(false)
    expect(changed.relational.host).toBe('')
  })
})
