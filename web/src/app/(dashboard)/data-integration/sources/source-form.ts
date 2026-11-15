import type { DataSourceDto } from '@/lib/api/types'
import {
  DEFAULT_CLICKHOUSE_CONFIG,
  DEFAULT_KAFKA_CONFIG,
  DEFAULT_OBJECT_STORAGE_CONFIG,
  buildClickHouseConfig,
  buildKafkaConfig,
  buildObjectStorageConfig,
  clickHouseCredentialScope,
  defaultPortForClickHouse,
  haveSameScope,
  isClickHouseConfigReadyToSave,
  isKafkaConfigReadyToSave,
  isKafkaSaslProtocol,
  isObjectStorageConfigReadyToSave,
  kafkaCredentialScope,
  objectStorageCredentialScope,
  parseJsonObject,
  parsePortNumber,
  readClickHouseConfig,
  readKafkaConfig,
  readObjectStorageConfig,
  type ClickHouseConfig,
  type KafkaConfig,
  type ObjectStorageConfig,
} from './sink-config-forms'

export {
  buildClickHouseConfig,
  buildKafkaConfig,
  buildObjectStorageConfig,
  defaultPortForClickHouse,
  isKafkaSaslProtocol,
  readClickHouseConfig,
  readKafkaConfig,
  readObjectStorageConfig,
  type ClickHouseConfig,
  type KafkaConfig,
  type ObjectStorageConfig,
}

export type DataSource = DataSourceDto

export interface RelationalConfig {
  provider: string
  host: string
  port: string
  database: string
  targetResourceGroup: string
  username: string
  password: string
  encrypt: boolean
  trustServerCertificate: boolean
}

export interface DataSourceFormState {
  name: string
  type: string
  config: string
  description: string
  hasSecret: boolean
  isEnabled: boolean
  relational: RelationalConfig
  clickhouse: ClickHouseConfig
  objectStorage: ObjectStorageConfig
  kafka: KafkaConfig
}

export function defaultPortForProvider(provider: string): string {
  switch (normalizeProvider(provider)) {
    case 'postgresql':
      return '5432'
    case 'mysql':
      return '3306'
    default:
      return '1433'
  }
}

const defaultRelational: RelationalConfig = {
  provider: 'postgresql',
  host: '',
  port: '5432',
  database: '',
  targetResourceGroup: '',
  username: '',
  password: '',
  encrypt: false,
  trustServerCertificate: true,
}

export function createDefaultDataSourceForm(): DataSourceFormState {
  return {
    name: '',
    type: '0',
    config: '{}',
    description: '',
    hasSecret: false,
    isEnabled: true,
    relational: { ...defaultRelational },
    clickhouse: { ...DEFAULT_CLICKHOUSE_CONFIG },
    objectStorage: { ...DEFAULT_OBJECT_STORAGE_CONFIG },
    kafka: { ...DEFAULT_KAFKA_CONFIG },
  }
}

export function buildRelationalConfig(config: RelationalConfig) {
  const provider = config.provider || 'postgresql'
  const host = config.host.trim()
  const port = config.port.trim() || defaultPortForProvider(provider)
  const database = config.database.trim()
  const targetResourceGroup = config.targetResourceGroup.trim()

  return JSON.stringify({
    provider,
    host,
    port: Number(port),
    database,
    targetResourceGroup,
    username: config.username.trim(),
    ...(config.password.trim() ? { password: config.password } : {}),
    encrypt: config.encrypt,
    trustServerCertificate: config.trustServerCertificate,
  }, null, 2)
}

export function readRelationalConfig(raw: string): RelationalConfig {
  const parsed = parseJsonObject(raw)
  if (!parsed) return { ...defaultRelational }

  const provider = String(parsed.provider ?? 'sqlserver')
  return {
    ...defaultRelational,
    provider,
    host: String(parsed.host ?? ''),
    port: String(parsed.port ?? defaultPortForProvider(provider)),
    database: String(parsed.database ?? ''),
    targetResourceGroup: String(parsed.targetResourceGroup ?? ''),
    username: String(parsed.username ?? parsed.userName ?? ''),
    password: '',
    encrypt: typeof parsed.encrypt === 'boolean' ? parsed.encrypt : false,
    trustServerCertificate:
      typeof parsed.trustServerCertificate === 'boolean'
        ? parsed.trustServerCertificate
        : true,
  }
}

function normalizeProvider(provider: string) {
  switch (provider.trim().toLowerCase()) {
    case '':
    case 'mssql':
    case 'sqlserver':
      return 'sqlserver'
    case 'mariadb':
    case 'mysql':
      return 'mysql'
    case 'postgres':
    case 'postgresql':
    case 'npgsql':
      return 'postgresql'
    default:
      return provider.trim().toLowerCase()
  }
}

function relationalCredentialScope(config: RelationalConfig) {
  const provider = normalizeProvider(config.provider)
  const port = parsePortNumber(config.port, Number(defaultPortForProvider(provider)))

  return {
    provider,
    host: config.host.trim().replace(/\.+$/, '').toLowerCase(),
    port,
    database: config.database.trim(),
    username: config.username.trim(),
    encrypt: config.encrypt,
    trustServerCertificate: config.trustServerCertificate,
  }
}

export function isRelationalConfigReadyToSave(config: RelationalConfig) {
  return config.host.trim().length > 0 && config.targetResourceGroup.trim().length > 0
}

export function requiresNewRelationalPassword(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (!source || form.type !== '0' || source.type !== 0)
    return false

  return !haveSameScope(
    relationalCredentialScope(readRelationalConfig(source.config)),
    relationalCredentialScope(form.relational),
  )
}

export function requiresNewClickHousePassword(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (!source || form.type !== '4' || source.type !== 4)
    return false

  return !haveSameScope(
    clickHouseCredentialScope(readClickHouseConfig(source.config)),
    clickHouseCredentialScope(form.clickhouse),
  )
}

export function requiresNewObjectStorageSecretKey(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (form.type !== '5') return false
  if (!source || source.type !== 5 || !source.hasSecret) return true

  return !haveSameScope(
    objectStorageCredentialScope(readObjectStorageConfig(source.config)),
    objectStorageCredentialScope(form.objectStorage),
  )
}

export function requiresNewKafkaSaslPassword(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (form.type !== '3' || !isKafkaSaslProtocol(form.kafka.securityProtocol))
    return false

  if (!source || source.type !== 3 || !source.hasSecret)
    return true

  const original = readKafkaConfig(source.config)
  if (!isKafkaSaslProtocol(original.securityProtocol))
    return true

  return !haveSameScope(
    kafkaCredentialScope(original),
    kafkaCredentialScope(form.kafka),
  )
}

export function requiresNewCredential(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  switch (form.type) {
    case '0':
      return requiresNewRelationalPassword(form, source)
    case '3':
      return requiresNewKafkaSaslPassword(form, source)
    case '4':
      return requiresNewClickHousePassword(form, source)
    case '5':
      return requiresNewObjectStorageSecretKey(form, source)
    default:
      return false
  }
}

function hasRequiredCredentialForSave(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (!requiresNewCredential(form, source)) return true
  switch (form.type) {
    case '0':
      return form.relational.password.trim().length > 0
    case '3':
      return form.kafka.saslPassword.trim().length > 0
    case '4':
      return form.clickhouse.password.trim().length > 0
    case '5':
      return form.objectStorage.secretKey.trim().length > 0
    default:
      return true
  }
}

export function isDataSourceConfigReadyToSave(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (!hasRequiredCredentialForSave(form, source)) return false
  switch (form.type) {
    case '0':
      return isRelationalConfigReadyToSave(form.relational)
    case '3':
      return isKafkaConfigReadyToSave(form.kafka)
    case '4':
      return isClickHouseConfigReadyToSave(form.clickhouse)
    case '5':
      return isObjectStorageConfigReadyToSave(form.objectStorage)
    default:
      return form.config.trim().length > 0
  }
}

export function isDataSourceTestable(
  form: DataSourceFormState,
  source: DataSource | null,
) {
  if (form.type !== '0' && form.type !== '3' && form.type !== '4' && form.type !== '5')
    return false
  return isDataSourceConfigReadyToSave(form, source)
}

export function buildDataSourceConfig(form: DataSourceFormState): string {
  switch (form.type) {
    case '0':
      return buildRelationalConfig(form.relational)
    case '3':
      return buildKafkaConfig(form.kafka)
    case '4':
      return buildClickHouseConfig(form.clickhouse)
    case '5':
      return buildObjectStorageConfig(form.objectStorage)
    default:
      return form.config
  }
}

export function changeDataSourceType(
  form: DataSourceFormState,
  type: string,
): DataSourceFormState {
  if (type === form.type)
    return form

  const fresh = createDefaultDataSourceForm()
  return {
    ...form,
    type,
    config: '{}',
    hasSecret: false,
    relational: fresh.relational,
    clickhouse: fresh.clickhouse,
    objectStorage: fresh.objectStorage,
    kafka: fresh.kafka,
  }
}

export function toDataSourceForm(source: DataSource): DataSourceFormState {
  return {
    name: source.name,
    type: String(source.type),
    config: source.config,
    description: source.description ?? '',
    hasSecret: source.hasSecret,
    isEnabled: source.isEnabled,
    relational: readRelationalConfig(source.config),
    clickhouse: readClickHouseConfig(source.config),
    objectStorage: readObjectStorageConfig(source.config),
    kafka: readKafkaConfig(source.config),
  }
}

export function toDataSourceBody(form: DataSourceFormState) {
  return {
    name: form.name,
    type: Number(form.type),
    config: buildDataSourceConfig(form),
    description: form.description || null,
    isEnabled: form.isEnabled,
  }
}
