import {
  authenticatedBrowserStoragePolicies,
  type AuthenticatedBrowserStorage,
  type AuthenticatedBrowserStoragePolicy,
  type AuthenticatedBrowserStorageReadResult,
  type BoundRawStorageHandle,
  type BrowserStorageArea,
  type BrowserStateStorage,
  type BrowserStorageValidator,
  type SerializedStorageValidator,
} from './browser-storage-policy-definitions'

export * from './browser-storage-policy-definitions'

interface BrowserStorageEnvelope<T> {
  version: number
  savedAt: number
  value: T
}

const STORAGE_NAMESPACE = 'swapmid:browser-storage:v1'
const IDENTITY_NAMESPACE_PREFIX = `${STORAGE_NAMESPACE}:identity:`
const PRINCIPAL_NAMESPACE_PREFIX = `${STORAGE_NAMESPACE}:principal:`

export const SESSION_IDENTITY_MARKER_KEY = 'swapmid:identity-version'
export const SESSION_IDENTITY_CHANGE_MARKER_KEY = 'swapmid:identity-change-signal'

const LEGACY_AUTHENTICATED_LOCAL_KEYS = [
  'swapmid-api-console-history',
  'swapmid-recent-visits',
] as const

const LEGACY_AUTHENTICATED_SESSION_KEYS = ['swapmid-tabs', 'swapmid-scroll'] as const

function getStorage(area: BrowserStorageArea): Storage | null {
  if (typeof window === 'undefined') return null

  try {
    return area === 'local' ? window.localStorage : window.sessionStorage
  } catch {
    return null
  }
}

export function classifyBrowserStorageArea(
  area: BrowserStorageArea,
  candidate: Storage | null,
): 'match' | 'mismatch' | 'unavailable' {
  const storage = getStorage(area)
  if (!storage) return 'unavailable'
  return candidate === storage ? 'match' : 'mismatch'
}

export function isValidSessionIdentityMarker(value: string | null | undefined): value is string {
  return typeof value === 'string'
    && /^[A-Za-z0-9._~-]{8,128}$/.test(value)
}

export function writeSessionIdentityMarker(value: string): boolean {
  if (!isValidSessionIdentityMarker(value)) return false
  const local = getStorage('local')
  if (!local) return false
  try {
    local.setItem(SESSION_IDENTITY_MARKER_KEY, value)
    return true
  } catch {
    return false
  }
}

export function readSessionIdentityMarker(): string | null {
  const local = getStorage('local')
  if (!local) return null
  try {
    const value = local.getItem(SESSION_IDENTITY_MARKER_KEY)
    if (isValidSessionIdentityMarker(value)) return value
    if (value !== null) removeSilently(local, SESSION_IDENTITY_MARKER_KEY)
    return null
  } catch {
    return null
  }
}

export function writeSessionIdentityChangeMarker(value: string): boolean {
  if (!isValidSessionIdentityMarker(value)) return false
  const local = getStorage('local')
  if (!local) return false
  try {
    local.setItem(SESSION_IDENTITY_CHANGE_MARKER_KEY, value)
    return true
  } catch {
    return false
  }
}

function removeSilently(storage: Storage | null, key: string) {
  if (!storage) return

  try {
    storage.removeItem(key)
  } catch {
    // Storage denial and quota failures must never take down authenticated UI.
  }
}

function snapshotKeys(storage: Storage | null): string[] {
  if (!storage) return []

  try {
    const keys: string[] = []
    for (let index = 0; index < storage.length; index += 1) {
      const key = storage.key(index)
      if (key !== null) keys.push(key)
    }
    return keys
  } catch {
    return []
  }
}

function encodeKeyPart(value: string): string {
  try {
    return encodeURIComponent(value)
  } catch {
    return encodeURIComponent(value.replace(/[\uD800-\uDFFF]/g, '\uFFFD'))
  }
}

function utf8ByteLength(value: string): number {
  let bytes = 0

  for (let index = 0; index < value.length; index += 1) {
    const code = value.charCodeAt(index)
    if (code < 0x80) {
      bytes += 1
    } else if (code < 0x800) {
      bytes += 2
    } else if (code >= 0xd800 && code <= 0xdbff) {
      const next = value.charCodeAt(index + 1)
      if (next >= 0xdc00 && next <= 0xdfff) {
        bytes += 4
        index += 1
      } else {
        bytes += 3
      }
    } else {
      bytes += 3
    }
  }

  return bytes
}

function isEnvelope(value: unknown): value is BrowserStorageEnvelope<unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false

  const candidate = value as Partial<BrowserStorageEnvelope<unknown>>
  return (
    Number.isInteger(candidate.version) &&
    Number.isInteger(candidate.savedAt) &&
    Object.prototype.hasOwnProperty.call(candidate, 'value')
  )
}

function validatorAccepts<T>(validator: BrowserStorageValidator<T>, value: unknown): value is T {
  try {
    return validator(value)
  } catch {
    return false
  }
}

function serializedValidatorAccepts(
  validator: SerializedStorageValidator | undefined,
  value: string,
): boolean {
  if (!validator) return true

  try {
    return validator(value)
  } catch {
    return false
  }
}

function preservesInvalidValues(policy: AuthenticatedBrowserStoragePolicy): boolean {
  return 'preserveInvalid' in policy && policy.preserveInvalid === true
}

function cleanupLegacyLocalStorage() {
  const local = getStorage('local')
  for (const key of LEGACY_AUTHENTICATED_LOCAL_KEYS) {
    removeSilently(local, key)
  }
}

function cleanupExpiredPrincipalStorage(): void {
  const local = getStorage('local')
  if (!local) return

  const now = Date.now()
  if (!Number.isFinite(now)) return
  const policies = Object.values(authenticatedBrowserStoragePolicies)
    .filter((policy) => policy.scope === 'principal')
    .map((policy) => ({
      keyFragment: `${encodeKeyPart(policy.id)}:v${policy.version}`,
      policy,
    }))

  for (const storedKey of snapshotKeys(local)) {
    if (!storedKey.startsWith(PRINCIPAL_NAMESPACE_PREFIX)) continue

    const suffix = storedKey.slice(PRINCIPAL_NAMESPACE_PREFIX.length)
    const ownerSeparator = suffix.indexOf(':')
    if (ownerSeparator <= 0) continue
    const policyVersionAndItem = suffix.slice(ownerSeparator + 1)
    const firstSeparator = policyVersionAndItem.indexOf(':')
    if (firstSeparator <= 0) continue
    const secondSeparator = policyVersionAndItem.indexOf(':', firstSeparator + 1)
    const policyVersionFragment = secondSeparator === -1
      ? policyVersionAndItem
      : policyVersionAndItem.slice(0, secondSeparator)
    const entry = policies.find(candidate => candidate.keyFragment === policyVersionFragment)
    if (!entry) continue

    let raw: string | null
    try {
      raw = local.getItem(storedKey)
    } catch {
      continue
    }
    if (raw === null) continue
    if (utf8ByteLength(raw) > entry.policy.maxBytes) {
      if (!preservesInvalidValues(entry.policy)) removeSilently(local, storedKey)
      continue
    }

    let parsed: unknown
    try {
      parsed = JSON.parse(raw)
    } catch {
      if (!preservesInvalidValues(entry.policy)) removeSilently(local, storedKey)
      continue
    }

    if (
      !isEnvelope(parsed)
      || parsed.version !== entry.policy.version
      || parsed.savedAt <= 0
      || parsed.savedAt > now
    ) {
      if (!preservesInvalidValues(entry.policy)) removeSilently(local, storedKey)
      continue
    }
    if (now - parsed.savedAt > entry.policy.ttlMs) {
      removeSilently(local, storedKey)
    }
  }
}

export function createAuthenticatedBrowserStorage({
  identityVersion,
  storagePrincipalId,
}: {
  identityVersion: string
  storagePrincipalId: string
}): AuthenticatedBrowserStorage {
  if (!identityVersion.trim()) {
    throw new TypeError('identityVersion must not be empty')
  }
  if (!storagePrincipalId.trim()) {
    throw new TypeError('storagePrincipalId must not be empty')
  }

  const encodedIdentityVersion = encodeKeyPart(identityVersion)
  const encodedPrincipalId = encodeKeyPart(storagePrincipalId)
  const currentIdentityPrefix = `${IDENTITY_NAMESPACE_PREFIX}${encodedIdentityVersion}:`
  let initialized = false

  const key = (policy: AuthenticatedBrowserStoragePolicy, itemKey?: string) => {
    const owner =
      policy.scope === 'identity'
        ? `identity:${encodedIdentityVersion}`
        : `principal:${encodedPrincipalId}`
    const base =
      `${STORAGE_NAMESPACE}:${owner}:${encodeKeyPart(policy.id)}:v${policy.version}`
    return itemKey === undefined ? base : `${base}:${encodeKeyPart(itemKey)}`
  }

  const remove = (policy: AuthenticatedBrowserStoragePolicy, itemKey?: string) => {
    initialize()
    removeSilently(getStorage(policy.area), key(policy, itemKey))
  }

  const readResult = <T>(
    policy: AuthenticatedBrowserStoragePolicy,
    validator: BrowserStorageValidator<T>,
    itemKey?: string,
  ): AuthenticatedBrowserStorageReadResult<T> => {
    initialize()
    const storage = getStorage(policy.area)
    const storageKey = key(policy, itemKey)
    if (!storage) return { status: 'unavailable' }

    let raw: string | null
    try {
      raw = storage.getItem(storageKey)
    } catch {
      return { status: 'unavailable' }
    }
    if (raw === null) return { status: 'absent' }

    if (utf8ByteLength(raw) > policy.maxBytes) {
      if (!preservesInvalidValues(policy)) removeSilently(storage, storageKey)
      return { status: 'invalid' }
    }

    let parsed: unknown
    try {
      parsed = JSON.parse(raw)
    } catch {
      if (!preservesInvalidValues(policy)) removeSilently(storage, storageKey)
      return { status: 'invalid' }
    }

    const now = Date.now()
    if (!Number.isFinite(now)) return { status: 'unavailable' }
    if (
      !isEnvelope(parsed) ||
      parsed.version !== policy.version ||
      parsed.savedAt <= 0 ||
      parsed.savedAt > now
    ) {
      if (!preservesInvalidValues(policy)) removeSilently(storage, storageKey)
      return { status: 'invalid' }
    }
    if (now - parsed.savedAt > policy.ttlMs) {
      removeSilently(storage, storageKey)
      return { status: 'absent' }
    }
    if (!validatorAccepts(validator, parsed.value)) {
      if (!preservesInvalidValues(policy)) removeSilently(storage, storageKey)
      return { status: 'invalid' }
    }

    return { status: 'found', value: parsed.value }
  }

  const read = <T>(
    policy: AuthenticatedBrowserStoragePolicy,
    validator: BrowserStorageValidator<T>,
    itemKey?: string,
  ): T | null => {
    const result = readResult(policy, validator, itemKey)
    return result.status === 'found' ? result.value : null
  }

  const write = <T>(
    policy: AuthenticatedBrowserStoragePolicy,
    value: T,
    itemKey?: string,
  ): boolean => {
    initialize()
    const storage = getStorage(policy.area)
    const storageKey = key(policy, itemKey)
    if (!storage) return false

    const savedAt = Date.now()
    if (!Number.isInteger(savedAt) || savedAt <= 0) {
      return false
    }

    let raw: string | undefined
    try {
      raw = JSON.stringify({
        version: policy.version,
        savedAt,
        value,
      } satisfies BrowserStorageEnvelope<T>)
    } catch {
      return false
    }

    if (raw === undefined || utf8ByteLength(raw) > policy.maxBytes) {
      return false
    }

    try {
      storage.setItem(storageKey, raw)
      return true
    } catch {
      return false
    }
  }

  const createStateStorage = (
    policy: AuthenticatedBrowserStoragePolicy,
    serializedValidator?: SerializedStorageValidator,
  ): BrowserStateStorage => {
    initialize()
    return {
      getItem(name) {
        return read(
          policy,
          (value): value is string =>
            typeof value === 'string' && serializedValidatorAccepts(serializedValidator, value),
          name,
        )
      },
      setItem(name, value) {
        if (
          typeof value !== 'string' ||
          !serializedValidatorAccepts(serializedValidator, value)
        ) {
          return
        }
        write(policy, value, name)
      },
      removeItem(name) {
        remove(policy, name)
      },
    }
  }

  const initialize = () => {
    if (initialized) return
    initialized = true

    cleanupLegacyLocalStorage()
    cleanupExpiredPrincipalStorage()

    const session = getStorage('session')
    for (const keyToDelete of LEGACY_AUTHENTICATED_SESSION_KEYS) {
      removeSilently(session, keyToDelete)
    }

    for (const storedKey of snapshotKeys(session)) {
      if (
        storedKey.startsWith(IDENTITY_NAMESPACE_PREFIX) &&
        !storedKey.startsWith(currentIdentityPrefix)
      ) {
        removeSilently(session, storedKey)
      }
    }
  }

  const installBoundary = () => {
    initialize()
    if (typeof window === 'undefined') return () => undefined

    const handleStorage = () => {
      cleanupLegacyLocalStorage()
    }
    window.addEventListener('storage', handleStorage)
    return () => window.removeEventListener('storage', handleStorage)
  }

  return {
    readResult,
    read,
    write,
    remove,
    key,
    createStateStorage,
    initialize,
    installBoundary,
  }
}

export function createAnonymousDraftRawHandle(formKey: string): BoundRawStorageHandle {
  const storageKey = `swapmid-draft:${formKey}`

  return {
    key: storageKey,
    getItem() {
      const storage = getStorage('session')
      if (!storage) return null
      try {
        return storage.getItem(storageKey)
      } catch {
        return null
      }
    },
    setItem(value) {
      const storage = getStorage('session')
      if (!storage) return
      try {
        storage.setItem(storageKey, value)
      } catch {
        // Anonymous drafts are best effort and must not block the form.
      }
    },
    removeItem() {
      removeSilently(getStorage('session'), storageKey)
    },
  }
}

export function createIdentityDraftRawHandle(
  storage: AuthenticatedBrowserStorage,
  formKey: string,
  serializedValidator?: SerializedStorageValidator,
): BoundRawStorageHandle {
  const policy = authenticatedBrowserStoragePolicies.authenticatedFormDraft
  const stateStorage = storage.createStateStorage(policy, serializedValidator)

  return {
    key: storage.key(policy, formKey),
    getItem: () => stateStorage.getItem(formKey),
    setItem: (value) => stateStorage.setItem(formKey, value),
    removeItem: () => stateStorage.removeItem(formKey),
  }
}
