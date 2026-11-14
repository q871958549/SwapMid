export const SUPPORTED_LOCALES = ['zh-Hans', 'zh-Hant', 'en'] as const
export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]

export const DEFAULT_LOCALE: SupportedLocale = 'zh-Hans'
export const PLATFORM_LOCALE_COOKIE = 'swapmid_language'

export function normalizeLocale(raw: string | null | undefined): SupportedLocale {
  if (!raw) return DEFAULT_LOCALE
  const trimmed = raw.trim()
  if (!trimmed) return DEFAULT_LOCALE
  const lower = trimmed.toLowerCase()
  if (
    lower === 'zh-hant'
    || lower === 'zh-tw'
    || lower === 'zh-hk'
    || lower === 'zh-mo'
    || lower.startsWith('zh-hant-')
  ) {
    return 'zh-Hant'
  }
  if (lower === 'en' || lower.startsWith('en-')) {
    return 'en'
  }
  if (
    lower === 'zh'
    || lower === 'zh-hans'
    || lower === 'zh-cn'
    || lower === 'zh-sg'
    || lower.startsWith('zh-')
  ) {
    return 'zh-Hans'
  }
  return DEFAULT_LOCALE
}

export function buildAcceptLanguage(locale: SupportedLocale): string {
  switch (locale) {
    case 'zh-Hant':
      return 'zh-Hant,zh-Hans;q=0.9'
    case 'en':
      return 'en,zh-Hans;q=0.8'
    case 'zh-Hans':
    default:
      return 'zh-Hans'
  }
}
