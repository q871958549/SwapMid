import { describe, expect, it } from 'vitest'
import {
  buildClickHouseConfig,
  buildKafkaConfig,
  buildObjectStorageConfig,
  buildRelationalConfig,
  changeDataSourceType,
  createDefaultDataSourceForm,
  isDataSourceConfigReadyToSave,
  isDataSourceTestable,
  isRelationalConfigReadyToSave,
  readClickHouseConfig,
  readKafkaConfig,
  readObjectStorageConfig,
  readRelationalConfig,
  requiresNewClickHousePassword,
  requiresNewKafkaSaslPassword,
  requiresNewObjectStorageSecretKey,
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

const clickHouseSource: DataSource = {
  id: 'source-ch',
  name: 'ClickHouse-Analytics',
  description: null,
  type: 4,
  config: JSON.stringify({
    provider: 'clickhouse',
    host: 'ch1.example.internal',
    port: 8443,
    database: 'analytics',
    username: 'etl_writer',
    password: 'must-not-leak',
    useTls: true,
    trustServerCertificate: false,
    targetResourceGroup: 'ch-analytics-prod',
  }),
  hasSecret: true,
  isEnabled: true,
  creationTime: '2026-11-15T00:00:00Z',
}

const objectStorageSource: DataSource = {
  id: 'source-s3',
  name: 'MinIO-Lake',
  description: null,
  type: 5,
  config: JSON.stringify({
    provider: 'minio',
    endpoint: 'http://minio.example.internal:9000',
    region: 'us-east-1',
    bucket: 'swapmid-lake',
    prefix: 'erp/daily',
    format: 'parquet',
    pathStyle: true,
    accessKey: 'minioadmin',
    secretKey: 'must-not-leak',
    targetResourceGroup: 'lake-minio-prod',
  }),
  hasSecret: true,
  isEnabled: true,
  creationTime: '2026-11-15T00:00:00Z',
}

const kafkaSource: DataSource = {
  id: 'source-kafka',
  name: 'Kafka-Events',
  description: null,
  type: 3,
  config: JSON.stringify({
    provider: 'kafka',
    bootstrapServers: 'kafka-1:9092,kafka-2:9092',
    clientId: 'swapmid-etl',
    securityProtocol: 'SaslSsl',
    saslMechanism: 'ScramSha256',
    saslUsername: 'etl_producer',
    saslPassword: 'must-not-leak',
    acks: 'All',
    compressionType: 'Snappy',
    targetResourceGroup: 'kafka-prod-bus',
  }),
  hasSecret: true,
  isEnabled: true,
  creationTime: '2026-11-15T00:00:00Z',
}

describe('ClickHouse / ObjectStorage / Kafka 表单与凭据边界', () => {
  it('ClickHouse 读取脱敏且仅在凭据作用域变化时要求重输密码', () => {
    const ch = readClickHouseConfig(clickHouseSource.config)
    expect(ch.password).toBe('')
    expect(ch.useTls).toBe(true)

    const form = toDataSourceForm(clickHouseSource)
    expect(requiresNewClickHousePassword(form, clickHouseSource)).toBe(false)
    form.clickhouse.targetResourceGroup = 'ch-analytics-alias'
    expect(requiresNewClickHousePassword(form, clickHouseSource)).toBe(false)

    form.clickhouse.host = 'ch2.example.internal'
    expect(requiresNewClickHousePassword(form, clickHouseSource)).toBe(true)
    expect(isDataSourceConfigReadyToSave(form, clickHouseSource)).toBe(false)

    form.clickhouse.password = 'new-secret'
    expect(isDataSourceConfigReadyToSave(form, clickHouseSource)).toBe(true)
    const built = JSON.parse(buildClickHouseConfig(form.clickhouse)) as Record<string, unknown>
    expect(built.password).toBe('new-secret')
    expect(built.provider).toBe('clickhouse')
  })

  it('ObjectStorage 新建必须填写 secretKey，编辑仅修改 prefix/format 可沿用密钥', () => {
    const createForm = changeDataSourceType(createDefaultDataSourceForm(), '5')
    createForm.name = 'Lake'
    createForm.objectStorage = {
      ...createForm.objectStorage,
      provider: 'minio',
      endpoint: 'http://minio.example.internal:9000/',
      bucket: 'swapmid-lake',
      accessKey: 'minioadmin',
      pathStyle: true,
      targetResourceGroup: 'lake-minio-prod',
    }
    expect(requiresNewObjectStorageSecretKey(createForm, null)).toBe(true)
    expect(isDataSourceConfigReadyToSave(createForm, null)).toBe(false)

    createForm.objectStorage.secretKey = 'minioadmin123'
    expect(isDataSourceConfigReadyToSave(createForm, null)).toBe(true)
    expect(isDataSourceTestable(createForm, null)).toBe(true)

    const editForm = toDataSourceForm(objectStorageSource)
    expect(readObjectStorageConfig(objectStorageSource.config).secretKey).toBe('')
    expect(requiresNewObjectStorageSecretKey(editForm, objectStorageSource)).toBe(false)

    editForm.objectStorage.prefix = '/erp/hourly/'
    editForm.objectStorage.format = 'jsonl'
    expect(requiresNewObjectStorageSecretKey(editForm, objectStorageSource)).toBe(false)
    const serialized = JSON.parse(buildObjectStorageConfig(editForm.objectStorage)) as Record<string, unknown>
    expect(serialized.prefix).toBe('erp/hourly')
    expect(serialized.secretKey).toBeUndefined()

    editForm.objectStorage.accessKey = 'other-key'
    expect(requiresNewObjectStorageSecretKey(editForm, objectStorageSource)).toBe(true)
  })

  it('Kafka Plaintext 不要求 SASL 凭据；SASL 作用域变化时强制重输 saslPassword', () => {
    const plainForm = changeDataSourceType(createDefaultDataSourceForm(), '3')
    plainForm.name = 'Local-Kafka'
    plainForm.kafka = {
      ...plainForm.kafka,
      bootstrapServers: 'localhost:9092',
      targetResourceGroup: 'kafka-local',
    }
    expect(requiresNewKafkaSaslPassword(plainForm, null)).toBe(false)
    expect(isDataSourceConfigReadyToSave(plainForm, null)).toBe(true)
    const plainJson = JSON.parse(buildKafkaConfig(plainForm.kafka)) as Record<string, unknown>
    expect(plainJson.saslMechanism).toBeUndefined()
    expect(plainJson.saslUsername).toBeUndefined()
    expect(plainJson.saslPassword).toBeUndefined()

    const saslForm = toDataSourceForm(kafkaSource)
    expect(readKafkaConfig(kafkaSource.config).saslPassword).toBe('')
    expect(requiresNewKafkaSaslPassword(saslForm, kafkaSource)).toBe(false)

    saslForm.kafka.compressionType = 'Zstd'
    expect(requiresNewKafkaSaslPassword(saslForm, kafkaSource)).toBe(false)

    saslForm.kafka.bootstrapServers = 'kafka-3:9092'
    expect(requiresNewKafkaSaslPassword(saslForm, kafkaSource)).toBe(true)
    expect(isDataSourceConfigReadyToSave(saslForm, kafkaSource)).toBe(false)

    saslForm.kafka.saslPassword = 'rotated-password'
    expect(isDataSourceConfigReadyToSave(saslForm, kafkaSource)).toBe(true)
    const body = toDataSourceBody(saslForm)
    expect(body.type).toBe(3)
  })
})
