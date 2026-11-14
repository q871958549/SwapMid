export interface I18nIncident {
  code: string
  locale: string
  key: string
  path: string
  buildId: string
}

const SAFE_FALLBACKS: Record<string, string> = {
  'zh-Hans': '内容暂不可用',
  'zh-Hant': '內容暫不可用',
  en: 'Content unavailable',
}

export function resolveMessageKey(namespace: string | undefined, key: string): string {
  return namespace ? `${namespace}.${key}` : key
}

/** 消息解析失败时显示稳定的安全文案，绝不把内部点号 key 暴露给最终用户。 */
export function getSafeMessageFallback(locale: string): string {
  return SAFE_FALLBACKS[locale] ?? SAFE_FALLBACKS['zh-Hans']
}

export function createI18nIncident(input: {
  code: string
  locale: string
  namespace?: string
  key: string
  path?: string
  buildId?: string
}): I18nIncident {
  return {
    code: input.code,
    locale: input.locale,
    key: resolveMessageKey(input.namespace, input.key),
    path: input.path || 'unknown',
    buildId: input.buildId || 'unknown',
  }
}

/** 单行 JSON 便于浏览器上报、服务端日志和 Seq 使用同一字段口径检索。 */
export function formatI18nIncident(incident: I18nIncident): string {
  return JSON.stringify(incident)
}
