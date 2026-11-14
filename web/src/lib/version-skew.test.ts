import { describe, expect, it } from 'vitest'
import { buildSkewDetected, shouldSilentVersionSwitch } from './version-skew'

const known = { buildId: 'build-a', release: '2026-a' }

describe('静默版本切换', () => {
  it('只有构建产物变化，并且用户已经换过路由，才切换', () => {
    expect(buildSkewDetected(known, { buildId: 'build-b', release: '2026-a' })).toBe(true)
    expect(buildSkewDetected(known, { buildId: 'build-a', release: '2026-b' })).toBe(false)
    expect(shouldSilentVersionSwitch({
      known,
      meta: { buildId: 'build-b' },
      navigated: false,
      now: 120_000,
      lastSwitchAt: 0,
    })).toBe(false)
    expect(shouldSilentVersionSwitch({
      known,
      meta: { buildId: 'build-b' },
      navigated: true,
      now: 120_000,
      lastSwitchAt: 0,
    })).toBe(true)
  })

  it('短时间多次发布不再次切换', () => {
    expect(shouldSilentVersionSwitch({
      known,
      meta: { buildId: 'build-c' },
      navigated: true,
      now: 30_000,
      lastSwitchAt: 10_000,
      cooldownMs: 60_000,
    })).toBe(false)
    expect(shouldSilentVersionSwitch({
      known,
      meta: { buildId: 'build-c' },
      navigated: true,
      now: 80_000,
      lastSwitchAt: 10_000,
      cooldownMs: 60_000,
    })).toBe(true)
  })

  it('dev 不切换', () => {
    expect(shouldSilentVersionSwitch({
      known: { buildId: 'dev', release: '' },
      meta: { buildId: 'build-b' },
      navigated: true,
      now: 10_000,
      lastSwitchAt: 0,
    })).toBe(false)
  })
})
