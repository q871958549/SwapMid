import type { DataSourceDto } from '@/lib/api/types'

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
  try {
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object' || Array.isArray(value))
      return { ...defaultRelational }

    const parsed = value as Record<string, unknown>
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
  } catch {
    return { ...defaultRelational }
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
  const parsedPort = Number(config.port.trim())
  const port = Number.isInteger(parsedPort) && parsedPort > 0
    ? parsedPort
    : Number(defaultPortForProvider(provider))

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
  if (!source || form.type !== '0')
    return false

  if (source.type !== 0)
    return false

  const originalScope = relationalCredentialScope(readRelationalConfig(source.config))
  const proposedScope = relationalCredentialScope(form.relational)
  return Object.keys(originalScope).some((key) => {
    const field = key as keyof typeof originalScope
    return originalScope[field] !== proposedScope[field]
  })
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
  }
}

export function toDataSourceBody(form: DataSourceFormState) {
  return {
    name: form.name,
    type: Number(form.type),
    config: form.type === '0' ? buildRelationalConfig(form.relational) : form.config,
    description: form.description || null,
    isEnabled: form.isEnabled,
  }
}
