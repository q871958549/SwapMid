import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from './server-backend-fetch'

interface AbpAppConfig {
  currentUser: {
    id?: string
    userName?: string
    name?: string
    surName?: string
    email?: string
    roles?: string[]
  }
  auth: {
    grantedPolicies?: Record<string, boolean>
  }
  setting?: {
    values?: Record<string, string | null>
  }
}

export interface AbpUserConfig {
  userId?: string
  userName: string
  name: string
  email?: string
  roles: string[]
  grantedPolicies: Record<string, boolean>
  theme?: string
  fontSize?: number
  language?: string
}

export async function fetchAbpUserConfig(accessToken: string, fallbackUserName = ''): Promise<AbpUserConfig> {
  const res = await serverBackendFetch(
    '/api/abp/application-configuration?IncludeLocalizationResources=false',
    {
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: 'no-store',
      deadlineMs: SERVER_BACKEND_DEADLINE_MS.applicationConfiguration,
      maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.applicationConfiguration,
    },
  )
  if (!res.ok) {
    throw new Error(`application-configuration failed (${res.status})`)
  }
  const config: AbpAppConfig = await res.json()
  const allPolicies = config.auth.grantedPolicies ?? {}
  const grantedPolicies = Object.fromEntries(
    Object.entries(allPolicies).filter(([k, v]) => v && k.startsWith('SwapMid.')),
  )
  const settings = config.setting?.values ?? {}
  const rawTheme = settings['SwapMid.Preference.Theme'] ?? undefined
  const rawFont = Number(settings['SwapMid.Preference.FontSize'])
  const rawLang = settings['SwapMid.Preference.Language'] ?? undefined
  const u = config.currentUser
  const rawId = typeof u.id === 'string' ? u.id.trim() : ''
  return {
    userId: rawId.length > 0 && rawId.length <= 256 ? rawId : undefined,
    userName: u.userName ?? fallbackUserName,
    name: [u.surName, u.name].filter(Boolean).join('') || u.userName || fallbackUserName,
    email: u.email ?? undefined,
    roles: u.roles ?? [],
    grantedPolicies,
    theme: rawTheme === 'light' || rawTheme === 'dark' || rawTheme === 'system' ? rawTheme : undefined,
    fontSize: rawFont >= 13 && rawFont <= 18 ? rawFont : undefined,
    language: rawLang === 'zh-Hans' || rawLang === 'zh-Hant' || rawLang === 'en' ? rawLang : undefined,
  }
}
