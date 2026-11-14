import { describe, expect, it } from 'vitest'
import {
  SESSION_ABSOLUTE_TTL_MS,
  SESSION_IDLE_TTL_MS,
  resolveSessionIssuedAt,
  slidingSessionTtlMs,
} from './session-lifetime'

describe('session-lifetime', () => {
  it('旧会话没有 issuedAt 时从现在起算', () => {
    const now = 1_800_000_000_000
    expect(resolveSessionIssuedAt({}, now)).toBe(now)
    expect(resolveSessionIssuedAt({ issuedAt: now - 1_000 }, now)).toBe(now - 1_000)
  })

  it('空闲窗口内刷新续满 8 小时', () => {
    const issuedAt = 1_800_000_000_000
    expect(slidingSessionTtlMs(issuedAt, issuedAt + 60_000)).toBe(SESSION_IDLE_TTL_MS)
  })

  it('接近 7 天封顶时只续剩余寿命', () => {
    const issuedAt = 1_800_000_000_000
    const now = issuedAt + SESSION_ABSOLUTE_TTL_MS - 3_600_000
    expect(slidingSessionTtlMs(issuedAt, now)).toBe(3_600_000)
  })

  it('超过绝对上限不再续期', () => {
    const issuedAt = 1_800_000_000_000
    expect(slidingSessionTtlMs(issuedAt, issuedAt + SESSION_ABSOLUTE_TTL_MS + 1)).toBe(0)
  })
})
