/**
 * @typedef {'redis' | 'memory'} SessionStoreMode
 * @typedef {{ mode: SessionStoreMode, redisUrl?: string }} SessionStoreConfiguration
 * @typedef {Record<string, string | undefined>} SessionStoreEnvironment
 */

function invalidConfiguration(message) {
  throw new Error(`Session store configuration is invalid: ${message}`)
}

function validateRedisUrl(raw) {
  if (
    raw !== raw.trim()
    || /[\u0000-\u001f\u007f]/.test(raw)
  ) {
    return invalidConfiguration('REDIS_URL contains invalid characters')
  }

  let parsed
  try {
    parsed = new URL(raw)
  } catch {
    return invalidConfiguration('REDIS_URL is not a valid URL')
  }

  if (
    (parsed.protocol !== 'redis:' && parsed.protocol !== 'rediss:')
    || !parsed.hostname
    || parsed.hash
  ) {
    return invalidConfiguration('REDIS_URL must be a redis:// or rediss:// URL')
  }

  return raw
}

/**
 * development/test 之外的会话存储必须显式选择 Redis；任何漏配都在进程启动前失败，
 * 禁止非标准 NODE_ENV（如 staging/prod）或缺失 NODE_ENV 时静默退回单进程 Map。
 * development/test 保留旧的便利行为：未显式配置 mode 时，有 REDIS_URL 走 Redis，否则走内存。
 *
 * @param {SessionStoreEnvironment} [environment]
 * @returns {SessionStoreConfiguration}
 */
export function resolveSessionStoreConfiguration(environment = process.env) {
  const rawMode = environment.SESSION_STORE_MODE
  const allowsMemory = environment.NODE_ENV === 'development'
    || environment.NODE_ENV === 'test'
  /** @type {SessionStoreMode} */
  let mode

  if (rawMode === undefined || rawMode === '') {
    if (!allowsMemory) {
      return invalidConfiguration(
        'SESSION_STORE_MODE=redis is required outside development/test',
      )
    }
    mode = environment.REDIS_URL ? 'redis' : 'memory'
  } else if (rawMode === 'redis' || rawMode === 'memory') {
    mode = rawMode
  } else {
    return invalidConfiguration('SESSION_STORE_MODE must be redis or memory')
  }

  if (!allowsMemory && mode !== 'redis') {
    return invalidConfiguration(
      'memory mode is forbidden outside development/test',
    )
  }

  if (mode === 'memory') {
    return { mode }
  }

  const redisUrl = environment.REDIS_URL
  if (!redisUrl) {
    return invalidConfiguration('REDIS_URL is required for redis mode')
  }

  return {
    mode,
    redisUrl: validateRedisUrl(redisUrl),
  }
}
