import {
  PLATFORM_UPDATING_CODE,
  notePlatformUpdatingFallback,
  platformUpdatingDescription,
} from '@/lib/platform-updating'

const GENERIC_SERVER_ERROR = /\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef|An internal error occurred during your request/i
const GENERIC_VALIDATION_ERROR = /^(?:\u60a8\u7684\u8bf7\u6c42\u65e0\u6548[\uff01!]?|Your request is not valid[.!]?)$/i
const INCOMPLETE_UPLOAD = /Failed to read the request form\.[\s\S]*Unexpected end of Stream/i
const PROXY_TRANSPORT_ERROR = /^(Gateway Timeout|Request Timeout|Bad Gateway|Service Unavailable)$/i
const UPSTREAM_MISSING = '\u4e0d\u5b58\u5728'
const UPSTREAM_FIELD = '\u5b57\u6bb5'
const UPSTREAM_FIELD_MISSING = new RegExp(`${UPSTREAM_FIELD}[“"]([^”"]+)[”"]${UPSTREAM_MISSING}`)

function extractUpstreamStatusError(text: string): string | undefined {
  if (!text.includes('ResponseStatus') && !text.includes(UPSTREAM_MISSING)) return undefined
  const start = text.indexOf('{')
  const end = text.lastIndexOf('}')
  if (start >= 0 && end > start) {
    try {
      const parsed = JSON.parse(text.slice(start, end + 1)) as {
        Result?: { ResponseStatus?: { Errors?: { Message?: string }[] } }
      }
      const upstream = parsed.Result?.ResponseStatus?.Errors
        ?.map(e => e.Message)
        .filter((m): m is string => !!m?.trim())
        .join('；')
      if (upstream) return upstream
    } catch {
      /* protocol payload, not UI copy */
    }
  }
  return text.match(UPSTREAM_FIELD_MISSING)?.[0]
}

function polish(message: string | undefined, fallback: string) {
  if (!message) return fallback
  return extractUpstreamStatusError(message) || message
}

export function getApiErrorMessage(error: unknown, fallback: string, copy?: { incompleteUpload: string }) {
  const text = error instanceof Error ? error.message : String(error ?? '')
  if (!text) return fallback

  try {
    const parsed = JSON.parse(text) as {
      code?: string
      error?: { message?: string; details?: string; validationErrors?: { message?: string }[] } | string
      message?: string
      Result?: { ResponseStatus?: { Errors?: { Message?: string }[] } }
    }
    if (parsed.code === PLATFORM_UPDATING_CODE) {
      notePlatformUpdatingFallback()
      return platformUpdatingDescription(fallback)
    }
    const upstreamRoot = extractUpstreamStatusError(text)
    if (upstreamRoot && parsed.Result) return upstreamRoot
    if (typeof parsed.error === 'string') {
      return PROXY_TRANSPORT_ERROR.test(parsed.error) ? fallback : polish(parsed.error, fallback)
    }
    const message = parsed.error?.message || parsed.message
    const details = parsed.error?.details?.trim()
    const validation = parsed.error?.validationErrors
      ?.map(v => v.message)
      .filter((m): m is string => !!m && m.trim().length > 0)
      .join('；')
    if (INCOMPLETE_UPLOAD.test(`${details ?? ''}\n${validation ?? ''}`)) {
      return copy?.incompleteUpload ?? fallback
    }
    if (message && !GENERIC_SERVER_ERROR.test(message) && !GENERIC_VALIDATION_ERROR.test(message)) {
      return polish(message, fallback)
    }
    if (validation) return polish(validation, fallback)
    if (details) return polish(details, fallback)
    return polish(message, fallback)
  } catch {
    return polish(text, fallback)
  }
}

/** 读取非 2xx `fetch` 响应体并提取可读错误信息；兼容只实现部分方法的测试替身。 */
export async function readApiErrorMessage(
  res: Response,
  fallback: string,
  copy?: { incompleteUpload: string },
): Promise<string> {
  if (typeof res.text === 'function') {
    const bodyText = await res.text().catch(() => '')
    return bodyText ? getApiErrorMessage(bodyText, fallback, copy) : fallback
  }
  if (typeof res.json === 'function') {
    const payload = await res.json().catch(() => null)
    return payload ? getApiErrorMessage(JSON.stringify(payload), fallback, copy) : fallback
  }
  return fallback
}

/** 安全解析 `fetch` 成功响应 JSON，自动跳过 `204/205` 或空响应体（如 `activate`/`pause`）。 */
export async function parseOptionalJsonResponse<T>(res: Response): Promise<T> {
  const hasEmptyBody = res.status === 204
    || res.status === 205
    || res.headers?.get?.('content-length') === '0'
  if (hasEmptyBody) return undefined as T
  if (typeof res.text === 'function') {
    const text = await res.text()
    return (text ? JSON.parse(text) : undefined) as T
  }
  if (typeof res.json === 'function') {
    return await res.json() as T
  }
  return undefined as T
}
