import { describe, expect, it } from 'vitest'
import {
  DEFAULT_LOCALE,
  PLATFORM_LOCALE_COOKIE,
  buildAcceptLanguage,
  normalizeLocale,
} from './locale'

describe('locale helpers', () => {
  it('normalizes supported Chinese and English variants and falls back to zh-Hans', () => {
    expect(PLATFORM_LOCALE_COOKIE).toBe('swapmid_language')
    expect(normalizeLocale(undefined)).toBe(DEFAULT_LOCALE)
    expect(normalizeLocale('')).toBe('zh-Hans')
    expect(normalizeLocale('zh-CN')).toBe('zh-Hans')
    expect(normalizeLocale('zh-Hans')).toBe('zh-Hans')
    expect(normalizeLocale('zh-TW')).toBe('zh-Hant')
    expect(normalizeLocale('zh-HK')).toBe('zh-Hant')
    expect(normalizeLocale('zh-Hant-HK')).toBe('zh-Hant')
    expect(normalizeLocale('en')).toBe('en')
    expect(normalizeLocale('en-US')).toBe('en')
    expect(normalizeLocale('ja-JP')).toBe('zh-Hans')
  })

  it('builds deterministic Accept-Language headers for backend requests', () => {
    expect(buildAcceptLanguage('zh-Hans')).toBe('zh-Hans')
    expect(buildAcceptLanguage('zh-Hant')).toBe('zh-Hant,zh-Hans;q=0.9')
    expect(buildAcceptLanguage('en')).toBe('en,zh-Hans;q=0.8')
  })
})
