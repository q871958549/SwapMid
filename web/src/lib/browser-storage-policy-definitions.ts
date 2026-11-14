export type BrowserStorageArea = 'local' | 'session'

export type BrowserStorageScope = 'device' | 'anonymous' | 'principal' | 'identity'

export type BrowserStorageSensitivity = 'low' | 'moderate' | 'high'

export const browserStorageScopeRegistry = {
  device: {
    area: 'local',
    authenticated: false,
    description: 'Preferences shared by every user of this browser profile.',
  },
  anonymous: {
    area: 'session',
    authenticated: false,
    description: 'Anonymous workflow state that belongs to the current browser tab.',
  },
  principal: {
    area: 'local',
    authenticated: true,
    description: 'Account-scoped state that can survive identity-version rotation.',
  },
  identity: {
    area: 'session',
    authenticated: true,
    description: 'State bound to one authenticated identity revision in one browser tab.',
  },
} as const satisfies Record<
  BrowserStorageScope,
  {
    area: BrowserStorageArea
    authenticated: boolean
    description: string
  }
>

export interface BrowserStoragePolicy {
  id: string
  area: BrowserStorageArea
  scope: BrowserStorageScope
  version: number
  ttlMs: number
  maxBytes: number
  sensitivity: BrowserStorageSensitivity
  /** 正确性状态损坏时保留原值作为跨标签 fail-closed 证据，直到显式合法写入/删除。 */
  preserveInvalid?: boolean
  description: string
}

export const authenticatedBrowserStoragePolicies = {
  tabs: {
    id: 'tabs',
    area: 'session',
    scope: 'identity',
    version: 1,
    ttlMs: 8 * 60 * 60 * 1_000,
    maxBytes: 256 * 1_024,
    sensitivity: 'low',
    description: 'Open dashboard tabs and their ordering.',
  },
  scroll: {
    id: 'scroll',
    area: 'session',
    scope: 'identity',
    version: 1,
    ttlMs: 8 * 60 * 60 * 1_000,
    maxBytes: 128 * 1_024,
    sensitivity: 'low',
    description: 'Per-route dashboard scroll positions.',
  },
  authenticatedFormDraft: {
    id: 'authenticated-form-draft',
    area: 'session',
    scope: 'identity',
    version: 1,
    ttlMs: 8 * 60 * 60 * 1_000,
    maxBytes: 256 * 1_024,
    sensitivity: 'high',
    description: 'Authenticated business-form drafts, partitioned by form key.',
  },
  apiConsoleHistory: {
    id: 'api-console-history',
    area: 'session',
    scope: 'identity',
    version: 1,
    ttlMs: 8 * 60 * 60 * 1_000,
    maxBytes: 32 * 1_024,
    sensitivity: 'high',
    description: 'API console method, path, and timestamp only; headers and bodies are forbidden.',
  },
  recentVisits: {
    id: 'recent-visits',
    area: 'local',
    scope: 'principal',
    version: 1,
    ttlMs: 30 * 24 * 60 * 60 * 1_000,
    maxBytes: 64 * 1_024,
    sensitivity: 'moderate',
    description: 'Recently visited authenticated routes for one account.',
  },
  etlExecutionRecovery: {
    id: 'etl-execution-recovery',
    area: 'local',
    scope: 'principal',
    version: 1,
    ttlMs: 30 * 24 * 60 * 60 * 1_000,
    maxBytes: 8 * 1_024,
    sensitivity: 'moderate',
    preserveInvalid: true,
    description: 'Account-scoped ETL RunIds shared across tabs and identity rotations for idempotent recovery.',
  },
} as const satisfies Record<
  string,
  BrowserStoragePolicy & {
    scope: 'principal' | 'identity'
  }
>

export type AuthenticatedBrowserStoragePolicy =
  (typeof authenticatedBrowserStoragePolicies)[keyof typeof authenticatedBrowserStoragePolicies]

export type BrowserStorageValidator<T> = (value: unknown) => value is T

export type SerializedStorageValidator = (value: string) => boolean

export type AuthenticatedBrowserStorageReadResult<T> =
  | { status: 'found'; value: T }
  | { status: 'absent' }
  | { status: 'invalid' }
  | { status: 'unavailable' }

export interface BrowserStateStorage {
  getItem(name: string): string | null
  setItem(name: string, value: string): void
  removeItem(name: string): void
}

export interface BoundRawStorageHandle {
  readonly key: string
  getItem(): string | null
  setItem(value: string): void
  removeItem(): void
}

export interface AuthenticatedBrowserStorage {
  readResult<T>(
    policy: AuthenticatedBrowserStoragePolicy,
    validator: BrowserStorageValidator<T>,
    itemKey?: string,
  ): AuthenticatedBrowserStorageReadResult<T>
  read<T>(
    policy: AuthenticatedBrowserStoragePolicy,
    validator: BrowserStorageValidator<T>,
    itemKey?: string,
  ): T | null
  write<T>(policy: AuthenticatedBrowserStoragePolicy, value: T, itemKey?: string): boolean
  remove(policy: AuthenticatedBrowserStoragePolicy, itemKey?: string): void
  key(policy: AuthenticatedBrowserStoragePolicy, itemKey?: string): string
  createStateStorage(
    policy: AuthenticatedBrowserStoragePolicy,
    serializedValidator?: SerializedStorageValidator,
  ): BrowserStateStorage
  initialize(): void
  installBoundary(): () => void
}
