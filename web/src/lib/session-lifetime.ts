/** 空闲超过此时长，Redis 会话失效；有 token 刷新则重新计时。 */
export const SESSION_IDLE_TTL_MS = 8 * 60 * 60 * 1000

/** 从签发起最长存活，防止无限滑动。 */
export const SESSION_ABSOLUTE_TTL_MS = 7 * 24 * 60 * 60 * 1000

export function resolveSessionIssuedAt(
  session: { issuedAt?: number },
  now = Date.now(),
): number {
  return typeof session.issuedAt === 'number'
    && Number.isFinite(session.issuedAt)
    && session.issuedAt > 0
    ? session.issuedAt
    : now
}

/** 刷新成功后续期：最短 0（已过绝对上限），最长一个空闲窗口。 */
export function slidingSessionTtlMs(issuedAt: number, now = Date.now()): number {
  const remainingAbsolute = SESSION_ABSOLUTE_TTL_MS - (now - issuedAt)
  if (remainingAbsolute <= 0) {
    return 0
  }

  return Math.min(SESSION_IDLE_TTL_MS, remainingAbsolute)
}
