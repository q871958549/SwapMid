export interface ClickHouseConfig {
  host: string
  port: string
  database: string
  targetResourceGroup: string
  username: string
  password: string
  useTls: boolean
  trustServerCertificate: boolean
}

export interface ObjectStorageConfig {
  provider: 's3' | 'minio'
  endpoint: string
  region: string
  bucket: string
  prefix: string
  format: 'parquet' | 'jsonl'
  pathStyle: boolean
  targetResourceGroup: string
  accessKey: string
  secretKey: string
}

export interface KafkaConfig {
  bootstrapServers: string
  clientId: string
  securityProtocol: 'Plaintext' | 'Ssl' | 'SaslPlaintext' | 'SaslSsl'
  saslMechanism: 'Plain' | 'ScramSha256' | 'ScramSha512'
  saslUsername: string
  saslPassword: string
  acks: 'All' | 'Leader'
  compressionType: 'None' | 'Gzip' | 'Snappy' | 'Zstd'
  targetResourceGroup: string
  schemaRegistryUrl: string
  schemaRegistryUsername: string
  schemaRegistryPassword: string
}

export const DEFAULT_CLICKHOUSE_CONFIG: ClickHouseConfig = {
  host: '',
  port: '8123',
  database: 'default',
  targetResourceGroup: '',
  username: 'default',
  password: '',
  useTls: false,
  trustServerCertificate: false,
}

export const DEFAULT_OBJECT_STORAGE_CONFIG: ObjectStorageConfig = {
  provider: 's3',
  endpoint: '',
  region: 'us-east-1',
  bucket: '',
  prefix: '',
  format: 'parquet',
  pathStyle: false,
  targetResourceGroup: '',
  accessKey: '',
  secretKey: '',
}

export const DEFAULT_KAFKA_CONFIG: KafkaConfig = {
  bootstrapServers: '',
  clientId: 'swapmid-etl',
  securityProtocol: 'Plaintext',
  saslMechanism: 'Plain',
  saslUsername: '',
  saslPassword: '',
  acks: 'All',
  compressionType: 'Snappy',
  targetResourceGroup: '',
  schemaRegistryUrl: '',
  schemaRegistryUsername: '',
  schemaRegistryPassword: '',
}

export function defaultPortForClickHouse(useTls: boolean): string {
  return useTls ? '8443' : '8123'
}

export function isKafkaSaslProtocol(protocol: string): boolean {
  return protocol === 'SaslPlaintext' || protocol === 'SaslSsl'
}

export function parseJsonObject(raw: string): Record<string, unknown> | null {
  try {
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null
    return value as Record<string, unknown>
  } catch {
    return null
  }
}

export function parsePortNumber(rawPort: string, fallback: number): number {
  const parsed = Number(rawPort.trim())
  return Number.isInteger(parsed) && parsed >= 1 && parsed <= 65535 ? parsed : fallback
}

function isValidPortInput(rawPort: string): boolean {
  const trimmed = rawPort.trim()
  if (trimmed === '') return true
  const parsed = Number(trimmed)
  return Number.isInteger(parsed) && parsed >= 1 && parsed <= 65535
}

export function haveSameScope<T extends Record<string, unknown>>(original: T, proposed: T): boolean {
  return Object.keys(original).every(key => original[key] === proposed[key])
}

export function buildClickHouseConfig(config: ClickHouseConfig) {
  const port = config.port.trim() || defaultPortForClickHouse(config.useTls)
  return JSON.stringify({
    provider: 'clickhouse',
    host: config.host.trim(),
    port: Number(port),
    database: config.database.trim() || 'default',
    targetResourceGroup: config.targetResourceGroup.trim(),
    username: config.username.trim() || 'default',
    ...(config.password.trim() ? { password: config.password } : {}),
    useTls: config.useTls,
    trustServerCertificate: config.trustServerCertificate,
  }, null, 2)
}

export function readClickHouseConfig(raw: string): ClickHouseConfig {
  const parsed = parseJsonObject(raw)
  if (!parsed) return { ...DEFAULT_CLICKHOUSE_CONFIG }

  const useTls = typeof parsed.useTls === 'boolean'
    ? parsed.useTls
    : typeof parsed.encrypt === 'boolean'
      ? parsed.encrypt
      : false
  return {
    ...DEFAULT_CLICKHOUSE_CONFIG,
    host: String(parsed.host ?? ''),
    port: String(parsed.port ?? defaultPortForClickHouse(useTls)),
    database: String(parsed.database ?? 'default'),
    targetResourceGroup: String(parsed.targetResourceGroup ?? ''),
    username: String(parsed.username ?? parsed.user ?? 'default'),
    password: '',
    useTls,
    trustServerCertificate: typeof parsed.trustServerCertificate === 'boolean'
      ? parsed.trustServerCertificate
      : false,
  }
}

function normalizeObjectStorageEndpoint(raw: string): string {
  const trimmed = raw.trim().replace(/\/+$/, '')
  try {
    const url = new URL(trimmed)
    return `${url.protocol}//${url.host}`.toLowerCase()
  } catch {
    return trimmed.toLowerCase()
  }
}

export function buildObjectStorageConfig(config: ObjectStorageConfig) {
  const cleanPrefix = config.prefix.trim().replace(/^\/+|\/+$/g, '')
  return JSON.stringify({
    provider: config.provider,
    endpoint: config.endpoint.trim().replace(/\/+$/, ''),
    region: config.region.trim().toLowerCase() || 'us-east-1',
    bucket: config.bucket.trim().toLowerCase(),
    prefix: cleanPrefix,
    format: config.format,
    pathStyle: config.pathStyle,
    targetResourceGroup: config.targetResourceGroup.trim(),
    accessKey: config.accessKey.trim(),
    ...(config.secretKey.trim() ? { secretKey: config.secretKey } : {}),
  }, null, 2)
}

export function readObjectStorageConfig(raw: string): ObjectStorageConfig {
  const parsed = parseJsonObject(raw)
  if (!parsed) return { ...DEFAULT_OBJECT_STORAGE_CONFIG }

  const rawProvider = String(parsed.provider ?? 's3').trim().toLowerCase()
  const provider: 's3' | 'minio' = rawProvider === 'minio' ? 'minio' : 's3'
  const rawFormat = String(parsed.format ?? 'parquet').trim().toLowerCase()
  const format: 'parquet' | 'jsonl' = rawFormat === 'jsonl' ? 'jsonl' : 'parquet'

  return {
    ...DEFAULT_OBJECT_STORAGE_CONFIG,
    provider,
    endpoint: String(parsed.endpoint ?? ''),
    region: String(parsed.region ?? 'us-east-1'),
    bucket: String(parsed.bucket ?? ''),
    prefix: String(parsed.prefix ?? ''),
    format,
    pathStyle: typeof parsed.pathStyle === 'boolean' ? parsed.pathStyle : provider === 'minio',
    targetResourceGroup: String(parsed.targetResourceGroup ?? ''),
    accessKey: String(parsed.accessKey ?? ''),
    secretKey: '',
  }
}

function normalizeSecurityProtocol(raw: unknown): KafkaConfig['securityProtocol'] {
  switch (String(raw ?? '').trim().toLowerCase()) {
    case 'ssl':
      return 'Ssl'
    case 'saslplaintext':
    case 'sasl_plaintext':
      return 'SaslPlaintext'
    case 'saslssl':
    case 'sasl_ssl':
      return 'SaslSsl'
    default:
      return 'Plaintext'
  }
}

function normalizeSaslMechanism(raw: unknown): KafkaConfig['saslMechanism'] {
  switch (String(raw ?? '').trim().toLowerCase()) {
    case 'scram-sha-256':
    case 'scramsha256':
      return 'ScramSha256'
    case 'scram-sha-512':
    case 'scramsha512':
      return 'ScramSha512'
    default:
      return 'Plain'
  }
}

function normalizeAcks(raw: unknown): KafkaConfig['acks'] {
  switch (String(raw ?? '').trim().toLowerCase()) {
    case 'leader':
    case '1':
      return 'Leader'
    default:
      return 'All'
  }
}

function normalizeCompression(raw: unknown): KafkaConfig['compressionType'] {
  switch (String(raw ?? '').trim().toLowerCase()) {
    case 'none':
      return 'None'
    case 'gzip':
      return 'Gzip'
    case 'zstd':
      return 'Zstd'
    default:
      return 'Snappy'
  }
}

function normalizeKafkaBrokers(raw: string): string {
  const seen = new Set<string>()
  const normalized: string[] = []
  for (const part of raw.split(',').map(s => s.trim()).filter(Boolean)) {
    let broker = part.toLowerCase()
    if (broker.startsWith('[')) {
      const idx = broker.indexOf(']')
      if (idx > 0 && idx === broker.length - 1) broker = `${broker}:9092`
    } else if (!broker.includes(':')) {
      broker = `${broker}:9092`
    }
    if (!seen.has(broker)) {
      seen.add(broker)
      normalized.push(broker)
    }
  }
  return normalized.join(',')
}

export function buildKafkaConfig(config: KafkaConfig) {
  const saslEnabled = isKafkaSaslProtocol(config.securityProtocol)
  const srUrl = config.schemaRegistryUrl.trim().replace(/\/+$/, '')
  return JSON.stringify({
    provider: 'kafka',
    bootstrapServers: config.bootstrapServers.trim(),
    clientId: config.clientId.trim() || 'swapmid-etl',
    securityProtocol: config.securityProtocol,
    ...(saslEnabled
      ? {
          saslMechanism: config.saslMechanism,
          saslUsername: config.saslUsername.trim(),
          ...(config.saslPassword.trim() ? { saslPassword: config.saslPassword } : {}),
        }
      : {}),
    acks: config.acks,
    compressionType: config.compressionType,
    targetResourceGroup: config.targetResourceGroup.trim(),
    ...(srUrl
      ? {
          schemaRegistryUrl: srUrl,
          ...(config.schemaRegistryUsername.trim() ? { schemaRegistryUsername: config.schemaRegistryUsername.trim() } : {}),
          ...(config.schemaRegistryPassword.trim() ? { schemaRegistryPassword: config.schemaRegistryPassword } : {}),
        }
      : {}),
  }, null, 2)
}

export function readKafkaConfig(raw: string): KafkaConfig {
  const parsed = parseJsonObject(raw)
  if (!parsed) return { ...DEFAULT_KAFKA_CONFIG }

  return {
    ...DEFAULT_KAFKA_CONFIG,
    bootstrapServers: String(parsed.bootstrapServers ?? ''),
    clientId: String(parsed.clientId ?? 'swapmid-etl'),
    securityProtocol: normalizeSecurityProtocol(parsed.securityProtocol),
    saslMechanism: normalizeSaslMechanism(parsed.saslMechanism),
    saslUsername: String(parsed.saslUsername ?? ''),
    saslPassword: '',
    acks: normalizeAcks(parsed.acks),
    compressionType: normalizeCompression(parsed.compressionType),
    targetResourceGroup: String(parsed.targetResourceGroup ?? ''),
    schemaRegistryUrl: String(parsed.schemaRegistryUrl ?? ''),
    schemaRegistryUsername: String(parsed.schemaRegistryUsername ?? ''),
    schemaRegistryPassword: '',
  }
}

export function clickHouseCredentialScope(config: ClickHouseConfig) {
  return {
    host: config.host.trim().replace(/\.+$/, '').toLowerCase(),
    port: parsePortNumber(config.port, Number(defaultPortForClickHouse(config.useTls))),
    database: config.database.trim() || 'default',
    username: config.username.trim() || 'default',
    useTls: config.useTls,
    trustServerCertificate: config.trustServerCertificate,
  }
}

export function objectStorageCredentialScope(config: ObjectStorageConfig) {
  return {
    provider: config.provider,
    endpoint: normalizeObjectStorageEndpoint(config.endpoint),
    region: config.region.trim().toLowerCase() || 'us-east-1',
    bucket: config.bucket.trim().toLowerCase(),
    pathStyle: config.pathStyle,
    accessKey: config.accessKey.trim(),
  }
}

export function kafkaCredentialScope(config: KafkaConfig) {
  const saslEnabled = isKafkaSaslProtocol(config.securityProtocol)
  return {
    bootstrapServers: normalizeKafkaBrokers(config.bootstrapServers),
    clientId: config.clientId.trim() || 'swapmid-etl',
    securityProtocol: config.securityProtocol,
    saslMechanism: saslEnabled ? config.saslMechanism : '',
    saslUsername: saslEnabled ? config.saslUsername.trim() : '',
  }
}

export function isClickHouseConfigReadyToSave(config: ClickHouseConfig) {
  return (
    config.host.trim().length > 0
    && isValidPortInput(config.port)
    && config.database.trim().length > 0
    && config.targetResourceGroup.trim().length > 0
  )
}

export function isObjectStorageConfigReadyToSave(config: ObjectStorageConfig) {
  return (
    config.endpoint.trim().length > 0
    && config.bucket.trim().length > 0
    && config.accessKey.trim().length > 0
    && config.targetResourceGroup.trim().length > 0
  )
}

export function isKafkaConfigReadyToSave(config: KafkaConfig) {
  return (
    config.bootstrapServers.trim().length > 0
    && config.targetResourceGroup.trim().length > 0
    && (!isKafkaSaslProtocol(config.securityProtocol) || config.saslUsername.trim().length > 0)
  )
}
