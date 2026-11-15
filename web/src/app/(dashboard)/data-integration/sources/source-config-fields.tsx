'use client'

import { useTranslations } from 'next-intl'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import {
  defaultPortForClickHouse,
  defaultPortForProvider,
  isKafkaSaslProtocol,
  type ClickHouseConfig,
  type KafkaConfig,
  type ObjectStorageConfig,
  type RelationalConfig,
} from './source-form'

const RELATIONAL_PROVIDERS = [
  { value: 'postgresql', label: 'PostgreSQL' },
  { value: 'sqlserver', label: 'SQL Server' },
  { value: 'mysql', label: 'MySQL' },
] as const

const STORAGE_PROVIDERS = [
  { value: 's3', label: 'Amazon S3' },
  { value: 'minio', label: 'MinIO' },
] as const

const STORAGE_FORMATS = [
  { value: 'parquet', label: 'Apache Parquet (.parquet)' },
  { value: 'jsonl', label: 'JSON Lines (.jsonl)' },
] as const

const KAFKA_PROTOCOLS = [
  { value: 'Plaintext', label: 'PLAINTEXT' },
  { value: 'Ssl', label: 'SSL' },
  { value: 'SaslPlaintext', label: 'SASL_PLAINTEXT' },
  { value: 'SaslSsl', label: 'SASL_SSL' },
] as const

const KAFKA_SASL_MECHANISMS = [
  { value: 'Plain', label: 'PLAIN' },
  { value: 'ScramSha256', label: 'SCRAM-SHA-256' },
  { value: 'ScramSha512', label: 'SCRAM-SHA-512' },
] as const

const KAFKA_ACKS = [
  { value: 'All', label: 'all (-1)' },
  { value: 'Leader', label: '1 (leader)' },
] as const

const KAFKA_COMPRESSIONS = [
  { value: 'Snappy', label: 'snappy' },
  { value: 'Gzip', label: 'gzip' },
  { value: 'Zstd', label: 'zstd' },
  { value: 'None', label: 'none' },
] as const

function TargetResourceGroupField({
  value,
  onChange,
}: {
  value: string
  onChange: (value: string) => void
}) {
  const t = useTranslations('DataIntegration.source')
  return (
    <div className="space-y-1.5">
      <Label htmlFor="target-resource-group">{t('targetResourceGroupFieldLabel')}</Label>
      <Input
        id="target-resource-group"
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={t('targetResourceGroupPlaceholder')}
        aria-describedby="target-resource-group-hint"
        required
      />
      <p id="target-resource-group-hint" className="text-xs text-muted-foreground">
        {t('targetResourceGroupRequiredHint')}
      </p>
    </div>
  )
}

export function RelationalConfigFields({
  value,
  hasSecret,
  requiresNewPassword,
  onChangeProvider,
  onPatch,
}: {
  value: RelationalConfig
  hasSecret: boolean
  requiresNewPassword: boolean
  onChangeProvider: (provider: string) => void
  onPatch: (patch: Partial<RelationalConfig>) => void
}) {
  const t = useTranslations('DataIntegration.source')
  return (
    <div className="grid gap-3">
      <div className="space-y-1.5">
        <Label>{t('providerFieldLabel')}</Label>
        <Select value={value.provider} onValueChange={onChangeProvider}>
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            {RELATIONAL_PROVIDERS.map(p => (
              <SelectItem key={p.value} value={p.value}>{p.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('hostFieldLabel')}</Label>
          <Input value={value.host} onChange={e => onPatch({ host: e.target.value })} placeholder="db.example.internal" />
        </div>
        <div className="space-y-1.5">
          <Label>{t('portFieldLabel')}</Label>
          <Input
            value={value.port}
            onChange={e => onPatch({ port: e.target.value })}
            placeholder={defaultPortForProvider(value.provider)}
          />
        </div>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('databaseFieldLabel')}</Label>
          <Input value={value.database} onChange={e => onPatch({ database: e.target.value })} placeholder="app_db" />
        </div>
        <div className="space-y-1.5">
          <Label>{t('usernameFieldLabel')}</Label>
          <Input value={value.username} onChange={e => onPatch({ username: e.target.value })} placeholder={t('usernamePlaceholder')} />
        </div>
      </div>
      <TargetResourceGroupField
        value={value.targetResourceGroup}
        onChange={targetResourceGroup => onPatch({ targetResourceGroup })}
      />
      <div className="space-y-1.5">
        <Label>{t('passwordFieldLabel')}{requiresNewPassword ? ' *' : ''}</Label>
        <Input
          type="password"
          value={value.password}
          onChange={e => onPatch({ password: e.target.value })}
          placeholder={hasSecret && !requiresNewPassword ? t('passwordPlaceholderEdit') : t('passwordPlaceholderCreate')}
          required={requiresNewPassword}
        />
      </div>
      {value.provider !== 'mysql' && (
        <div className="flex flex-wrap gap-4 text-sm">
          <label className="flex items-center gap-2">
            <Switch checked={value.encrypt} onCheckedChange={encrypt => onPatch({ encrypt })} />
            {t('encryptSwitchLabel')}
          </label>
          <label className="flex items-center gap-2">
            <Switch checked={value.trustServerCertificate} onCheckedChange={trustServerCertificate => onPatch({ trustServerCertificate })} />
            {t('trustCertSwitchLabel')}
          </label>
        </div>
      )}
    </div>
  )
}

export function ClickHouseConfigFields({
  value,
  hasSecret,
  requiresNewPassword,
  onPatch,
}: {
  value: ClickHouseConfig
  hasSecret: boolean
  requiresNewPassword: boolean
  onPatch: (patch: Partial<ClickHouseConfig>) => void
}) {
  const t = useTranslations('DataIntegration.source')

  function handleTlsToggle(useTls: boolean) {
    const prevDefault = defaultPortForClickHouse(value.useTls)
    const nextDefault = defaultPortForClickHouse(useTls)
    const port = value.port.trim() === '' || value.port.trim() === prevDefault
      ? nextDefault
      : value.port
    onPatch({ useTls, port })
  }

  return (
    <div className="grid gap-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('hostFieldLabel')}</Label>
          <Input value={value.host} onChange={e => onPatch({ host: e.target.value })} placeholder="clickhouse.example.internal" />
        </div>
        <div className="space-y-1.5">
          <Label>{t('portFieldLabel')}</Label>
          <Input
            value={value.port}
            onChange={e => onPatch({ port: e.target.value })}
            placeholder={defaultPortForClickHouse(value.useTls)}
          />
        </div>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('databaseFieldLabel')}</Label>
          <Input value={value.database} onChange={e => onPatch({ database: e.target.value })} placeholder="default" />
        </div>
        <div className="space-y-1.5">
          <Label>{t('usernameFieldLabel')}</Label>
          <Input value={value.username} onChange={e => onPatch({ username: e.target.value })} placeholder="default" />
        </div>
      </div>
      <TargetResourceGroupField
        value={value.targetResourceGroup}
        onChange={targetResourceGroup => onPatch({ targetResourceGroup })}
      />
      <div className="space-y-1.5">
        <Label>{t('passwordFieldLabel')}{requiresNewPassword ? ' *' : ''}</Label>
        <Input
          type="password"
          value={value.password}
          onChange={e => onPatch({ password: e.target.value })}
          placeholder={hasSecret && !requiresNewPassword ? t('passwordPlaceholderEdit') : t('optionalPasswordPlaceholder')}
          required={requiresNewPassword}
        />
      </div>
      <div className="flex flex-wrap gap-4 text-sm">
        <label className="flex items-center gap-2">
          <Switch checked={value.useTls} onCheckedChange={handleTlsToggle} />
          {t('encryptSwitchLabel')}
        </label>
        <label className="flex items-center gap-2">
          <Switch checked={value.trustServerCertificate} onCheckedChange={trustServerCertificate => onPatch({ trustServerCertificate })} />
          {t('trustCertSwitchLabel')}
        </label>
      </div>
    </div>
  )
}

export function ObjectStorageConfigFields({
  value,
  hasSecret,
  requiresNewSecretKey,
  onPatch,
}: {
  value: ObjectStorageConfig
  hasSecret: boolean
  requiresNewSecretKey: boolean
  onPatch: (patch: Partial<ObjectStorageConfig>) => void
}) {
  const t = useTranslations('DataIntegration.source')

  function handleProviderChange(raw: string) {
    const provider: 's3' | 'minio' = raw === 'minio' ? 'minio' : 's3'
    onPatch({ provider, pathStyle: provider === 'minio' })
  }

  return (
    <div className="grid gap-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('storageProviderFieldLabel')}</Label>
          <Select value={value.provider} onValueChange={handleProviderChange}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {STORAGE_PROVIDERS.map(p => (
                <SelectItem key={p.value} value={p.value}>{p.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>{t('storageFormatFieldLabel')}</Label>
          <Select
            value={value.format}
            onValueChange={v => onPatch({ format: v === 'jsonl' ? 'jsonl' : 'parquet' })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {STORAGE_FORMATS.map(f => (
                <SelectItem key={f.value} value={f.value}>{f.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('endpointFieldLabel')}</Label>
          <Input value={value.endpoint} onChange={e => onPatch({ endpoint: e.target.value })} placeholder="http://minio.example.internal:9000" />
        </div>
        <div className="space-y-1.5">
          <Label>{t('regionFieldLabel')}</Label>
          <Input value={value.region} onChange={e => onPatch({ region: e.target.value })} placeholder="us-east-1" />
        </div>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('bucketFieldLabel')}</Label>
          <Input value={value.bucket} onChange={e => onPatch({ bucket: e.target.value })} placeholder="swapmid-lake" />
        </div>
        <div className="space-y-1.5">
          <Label>{t('prefixFieldLabel')}</Label>
          <Input value={value.prefix} onChange={e => onPatch({ prefix: e.target.value })} placeholder="raw/erp" />
        </div>
      </div>
      <TargetResourceGroupField
        value={value.targetResourceGroup}
        onChange={targetResourceGroup => onPatch({ targetResourceGroup })}
      />
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('accessKeyFieldLabel')}</Label>
          <Input value={value.accessKey} onChange={e => onPatch({ accessKey: e.target.value })} placeholder="AKIA..." />
        </div>
        <div className="space-y-1.5">
          <Label>{t('secretKeyFieldLabel')}{requiresNewSecretKey ? ' *' : ''}</Label>
          <Input
            type="password"
            value={value.secretKey}
            onChange={e => onPatch({ secretKey: e.target.value })}
            placeholder={hasSecret && !requiresNewSecretKey ? t('secretPlaceholderEdit') : t('secretPlaceholderCreate')}
            required={requiresNewSecretKey}
          />
        </div>
      </div>
      <label className="flex items-center gap-2 text-sm">
        <Switch checked={value.pathStyle} onCheckedChange={pathStyle => onPatch({ pathStyle })} />
        {t('pathStyleSwitchLabel')}
      </label>
    </div>
  )
}

export function KafkaConfigFields({
  value,
  hasSecret,
  requiresNewPassword,
  onPatch,
}: {
  value: KafkaConfig
  hasSecret: boolean
  requiresNewPassword: boolean
  onPatch: (patch: Partial<KafkaConfig>) => void
}) {
  const t = useTranslations('DataIntegration.source')
  const saslEnabled = isKafkaSaslProtocol(value.securityProtocol)

  return (
    <div className="grid gap-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('bootstrapServersFieldLabel')}</Label>
          <Input
            value={value.bootstrapServers}
            onChange={e => onPatch({ bootstrapServers: e.target.value })}
            placeholder="kafka-1:9092,kafka-2:9092"
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('clientIdFieldLabel')}</Label>
          <Input value={value.clientId} onChange={e => onPatch({ clientId: e.target.value })} placeholder="swapmid-etl" />
        </div>
      </div>
      <div className="grid grid-cols-3 gap-3">
        <div className="space-y-1.5">
          <Label>{t('securityProtocolFieldLabel')}</Label>
          <Select
            value={value.securityProtocol}
            onValueChange={v => onPatch({ securityProtocol: v as KafkaConfig['securityProtocol'] })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {KAFKA_PROTOCOLS.map(p => (
                <SelectItem key={p.value} value={p.value}>{p.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>{t('kafkaAcksFieldLabel')}</Label>
          <Select value={value.acks} onValueChange={v => onPatch({ acks: v as KafkaConfig['acks'] })}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {KAFKA_ACKS.map(a => (
                <SelectItem key={a.value} value={a.value}>{a.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>{t('kafkaCompressionFieldLabel')}</Label>
          <Select
            value={value.compressionType}
            onValueChange={v => onPatch({ compressionType: v as KafkaConfig['compressionType'] })}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {KAFKA_COMPRESSIONS.map(c => (
                <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
      {saslEnabled && (
        <div className="grid grid-cols-3 gap-3">
          <div className="space-y-1.5">
            <Label>{t('saslMechanismFieldLabel')}</Label>
            <Select
              value={value.saslMechanism}
              onValueChange={v => onPatch({ saslMechanism: v as KafkaConfig['saslMechanism'] })}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                {KAFKA_SASL_MECHANISMS.map(m => (
                  <SelectItem key={m.value} value={m.value}>{m.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label>{t('saslUsernameFieldLabel')}</Label>
            <Input value={value.saslUsername} onChange={e => onPatch({ saslUsername: e.target.value })} placeholder="etl_producer" />
          </div>
          <div className="space-y-1.5">
            <Label>{t('saslPasswordFieldLabel')}{requiresNewPassword ? ' *' : ''}</Label>
            <Input
              type="password"
              value={value.saslPassword}
              onChange={e => onPatch({ saslPassword: e.target.value })}
              placeholder={hasSecret && !requiresNewPassword ? t('passwordPlaceholderEdit') : t('passwordPlaceholderCreate')}
              required={requiresNewPassword}
            />
          </div>
        </div>
      )}
      <TargetResourceGroupField
        value={value.targetResourceGroup}
        onChange={targetResourceGroup => onPatch({ targetResourceGroup })}
      />
    </div>
  )
}
