import { cookies } from 'next/headers'
import { getRequestConfig } from 'next-intl/server'
import {
  DEFAULT_LOCALE,
  PLATFORM_LOCALE_COOKIE,
  normalizeLocale,
  type SupportedLocale,
} from '@/lib/locale'

async function resolveRequestLocale(): Promise<SupportedLocale> {
  try {
    const cookieStore = await cookies()
    return normalizeLocale(cookieStore.get(PLATFORM_LOCALE_COOKIE)?.value)
  } catch {
    return DEFAULT_LOCALE
  }
}

export default getRequestConfig(async () => {
  const locale = await resolveRequestLocale()
  const messages = (await import(`../../messages/${locale}.json`)).default
  return {
    locale,
    messages,
    timeZone: 'UTC',
  }
})
