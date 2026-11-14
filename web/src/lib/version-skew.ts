export interface KnownVersion {
  buildId: string | null
  release: string
}

export interface RemoteVersion {
  buildId?: unknown
  release?: unknown
}

/** 同一标签页里，两次无感切换至少间隔这么久，避免一天多次发布连着打断。 */
export const SILENT_SWITCH_COOLDOWN_MS = 60_000

/**
 * 只有构建产物变了才需要换文档。发布号每次发布都变，静态资源经常没变，
 * 不能拿它触发刷新。
 */
export function buildSkewDetected(known: KnownVersion, meta: RemoteVersion): boolean {
  if (typeof meta.buildId !== 'string' || meta.buildId.length === 0 || meta.buildId === 'dev') {
    return false
  }
  if (!known.buildId || known.buildId === 'dev') return false
  return meta.buildId !== known.buildId
}

export function shouldSilentVersionSwitch(input: {
  known: KnownVersion
  meta: RemoteVersion
  navigated: boolean
  now: number
  lastSwitchAt: number
  cooldownMs?: number
}): boolean {
  if (!input.navigated) return false
  const cooldown = input.cooldownMs ?? SILENT_SWITCH_COOLDOWN_MS
  if (input.lastSwitchAt > 0 && input.now - input.lastSwitchAt < cooldown) return false
  return buildSkewDetected(input.known, input.meta)
}
