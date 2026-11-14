export const PLATFORM_UPDATING_CODE = 'PLATFORM_UPDATING'
export const PLATFORM_UPDATING_RETRY_AFTER_SECONDS = 15
export const PLATFORM_UPDATING_NOTICE_COOLDOWN_MS = 10 * 60_000

export interface PlatformUpdatingCopy {
  title: string
  description: string
}

let copy: PlatformUpdatingCopy | null = null
let notifier: () => void = () => undefined
let nextNoticeAt = 0

export function setPlatformUpdatingCopy(next: PlatformUpdatingCopy) {
  copy = next
}

export function platformUpdatingDescription(fallback: string) {
  return copy?.description ?? fallback
}

export function setPlatformUpdatingNotifier(next: () => void) {
  notifier = next
}

export function isPlatformUpdatingBody(text: string): boolean {
  try {
    const parsed = JSON.parse(text) as { code?: unknown }
    return parsed.code === PLATFORM_UPDATING_CODE
  } catch {
    return false
  }
}

/** 等待耗尽后的唯一提示。同一标签页冷却期内不再弹第二次，也不阻断页面。 */
export function notePlatformUpdatingFallback(now = Date.now()): boolean {
  if (!copy || now < nextNoticeAt) return false
  nextNoticeAt = now + PLATFORM_UPDATING_NOTICE_COOLDOWN_MS
  notifier()
  return true
}

/** 发布窗口的最后兜底：记一次提示并让调用方保留当前输入，不要再弹业务失败。 */
export function absorbPlatformUpdating(text: string): boolean {
  if (!isPlatformUpdatingBody(text)) return false
  notePlatformUpdatingFallback()
  return true
}

export function resetPlatformUpdatingForTests() {
  copy = null
  notifier = () => undefined
  nextNoticeAt = 0
}
