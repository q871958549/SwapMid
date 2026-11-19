import { readForwardableEdgeToken, readTrustedClientIp, EDGE_TOKEN_HEADER } from '@/lib/trusted-client-ip'
import {
  encodeBackendPathSegments,
  resolveBackendApiBase,
  selectForwardedResponseHeaders,
} from '../backend-forwarding'

const KIB = 1024
const MIB = 1024 * KIB

export const PROXY_DEFAULT_MAX_BODY_BYTES = 1 * MIB
export const PROXY_MAX_PATH_LENGTH = 4096
export const PROXY_MAX_QUERY_LENGTH = 8192

const STANDARD_TIMING = {
  requestIdleTimeoutMs: 30_000,
  responseHeadersTimeoutMs: 120_000,
  responseIdleTimeoutMs: 60_000,
  totalTimeoutMs: 10 * 60_000,
} as const

const STANDARD_UPLOAD_TIMING = {
  requestIdleTimeoutMs: 30_000,
  responseHeadersTimeoutMs: 120_000,
  responseIdleTimeoutMs: 60_000,
  totalTimeoutMs: 10 * 60_000,
} as const

const DATA_PIPELINE_EXECUTION_TIMING = {
  requestIdleTimeoutMs: 30_000,
  responseHeadersTimeoutMs: 6 * 60_000,
  responseIdleTimeoutMs: 60_000,
  totalTimeoutMs: 7 * 60_000,
} as const

const GUID_SEGMENT = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const DATA_PIPELINE_EXECUTION_PATH = new RegExp(
  `^/api/v1/data-pipelines/${GUID_SEGMENT}/execute$`,
  'i',
)
const PIPELINE_EXECUTION_RESOLUTION_PATH = new RegExp(
  `^/api/v1/pipeline-executions/${GUID_SEGMENT}/resolve-outcome$`,
  'i',
)
const PIPELINE_EXECUTION_RECEIPT_VERIFICATION_PATH = new RegExp(
  `^/api/v1/pipeline-executions/${GUID_SEGMENT}/verify-target-receipt$`,
  'i',
)

interface UploadRule {
  matches: (canonicalPath: string) => boolean
  maxBodyBytes: number
  timing: typeof STANDARD_UPLOAD_TIMING
}

const UPLOAD_RULES: readonly UploadRule[] = [
  {
    matches: path => path === '/api/v1/avatars',
    maxBodyBytes: 2 * MIB + 256 * KIB,
    timing: STANDARD_UPLOAD_TIMING,
  },
] as const

const SAFE_RESPONSE_HEADERS = [
  'accept-ranges',
  'allow',
  'content-disposition',
  'content-language',
  'content-range',
  'content-security-policy',
  'content-type',
  'etag',
  'last-modified',
  'retry-after',
  'www-authenticate',
  'x-content-type-options',
  'x-swapmid-trace-id',
] as const

const W3C_TRACEPARENT_PATTERN = /^00-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}$/i

export interface ProxyBackendPath {
  backendPath: string
  canonicalPath: string
}

export interface ProxyRequestPolicy {
  maxBodyBytes: number
  requestIdleTimeoutMs: number
  responseHeadersTimeoutMs: number
  responseIdleTimeoutMs: number
  totalTimeoutMs: number
  invalidMultipartBoundary: boolean
  forbidden: boolean
}

export interface ProxyRequestPolicyContext {
  grantedPolicies?: Readonly<Record<string, boolean>>
  canExecuteDataPipelines?: boolean
  canResolvePipelineExecutionOutcome?: boolean
  canVerifyPipelineExecutionTargetReceipt?: boolean
}

export function buildProxyBackendPath(segments: readonly string[]): ProxyBackendPath | undefined {
  const encoded = encodeBackendPathSegments(segments)
  if (!encoded) return undefined

  const first = segments[0].toLowerCase()
  const isRootMounted =
    first === 'api'
    || first === 'gateway'
    || (segments.length === 3 && first === 'swagger' && segments[1].toLowerCase() === 'v1' && segments[2].toLowerCase() === 'swagger.json')
  const backendPath = isRootMounted ? `/${encoded}` : `/api/v1/${encoded}`
  if (backendPath.length > PROXY_MAX_PATH_LENGTH) return undefined

  const canonicalSegments = segments.map(segment => segment.toLowerCase()).join('/')
  return {
    backendPath,
    canonicalPath: isRootMounted
      ? `/${canonicalSegments}`
      : `/api/v1/${canonicalSegments}`,
  }
}

export function isValidProxyQuery(search: string): boolean {
  return search.length <= PROXY_MAX_QUERY_LENGTH && !/[\u0000-\u001f\u007f]/.test(search)
}

export function resolveProxyApiBase(
  environment: Record<string, string | undefined> = process.env,
): string {
  return resolveBackendApiBase(environment)
}

function isMultipartFormData(contentType: string | null): boolean {
  if (!contentType || contentType.length > 512 || /[\r\n]/.test(contentType)) return false

  const parts = contentType.split(';').map(part => part.trim())
  if (parts.shift()?.toLowerCase() !== 'multipart/form-data') return false

  const boundaryPart = parts.find(part => /^boundary\s*=/i.test(part))
  if (!boundaryPart) return false

  const rawBoundary = boundaryPart.replace(/^boundary\s*=\s*/i, '')
  const boundary = rawBoundary.startsWith('"') && rawBoundary.endsWith('"')
    ? rawBoundary.slice(1, -1)
    : rawBoundary

  return boundary.length >= 1
    && boundary.length <= 70
    && !boundary.endsWith(' ')
    && /^[0-9A-Za-z'()+_,./:=? -]+$/.test(boundary)
}

function withTiming(
  maxBodyBytes: number,
  timing: typeof STANDARD_TIMING | typeof STANDARD_UPLOAD_TIMING | typeof DATA_PIPELINE_EXECUTION_TIMING,
  invalidMultipartBoundary = false,
  forbidden = false,
): ProxyRequestPolicy {
  return { maxBodyBytes, ...timing, invalidMultipartBoundary, forbidden }
}

export function getProxyRequestPolicy(
  canonicalPath: string,
  method: string,
  contentType: string | null,
  context: ProxyRequestPolicyContext = {},
): ProxyRequestPolicy {
  const normalizedMethod = method.toUpperCase()
  const normalizedPath = canonicalPath.toLowerCase()

  if (normalizedMethod === 'POST' && DATA_PIPELINE_EXECUTION_PATH.test(normalizedPath)) {
    if (!context.canExecuteDataPipelines) {
      return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING, false, true)
    }
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, DATA_PIPELINE_EXECUTION_TIMING)
  }

  if (normalizedMethod === 'POST' && PIPELINE_EXECUTION_RESOLUTION_PATH.test(normalizedPath)) {
    if (!context.canResolvePipelineExecutionOutcome) {
      return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING, false, true)
    }
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING)
  }

  if (normalizedMethod === 'POST' && PIPELINE_EXECUTION_RECEIPT_VERIFICATION_PATH.test(normalizedPath)) {
    if (!context.canVerifyPipelineExecutionTargetReceipt) {
      return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING, false, true)
    }
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING)
  }

  if (normalizedMethod !== 'POST') {
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING)
  }

  const uploadRule = UPLOAD_RULES.find(rule => rule.matches(normalizedPath))
  if (!uploadRule) {
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING)
  }

  const isMultipartMediaType = contentType
    ?.split(';', 1)[0]
    ?.trim()
    .toLowerCase() === 'multipart/form-data'
  if (!isMultipartMediaType) {
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING)
  }
  if (!isMultipartFormData(contentType)) {
    return withTiming(PROXY_DEFAULT_MAX_BODY_BYTES, STANDARD_TIMING, true)
  }

  return withTiming(uploadRule.maxBodyBytes, uploadRule.timing)
}

function copySafeHeader(
  target: Headers,
  source: Headers,
  name: string,
  maxLength: number,
): void {
  const value = source.get(name)
  if (!value || value.length > maxLength || /[\u0000-\u001f\u007f]/.test(value)) return
  target.set(name, value)
}

export function selectProxyRequestHeaders(
  source: Headers,
  method: string,
  accessToken: string,
  acceptLanguage: string,
  hasRequestBody: boolean,
): Headers {
  const result = new Headers({
    Accept: 'application/json',
    'Accept-Encoding': 'identity',
    'Accept-Language': acceptLanguage,
    Authorization: `Bearer ${accessToken}`,
  })

  copySafeHeader(result, source, 'accept', 1024)
  if (hasRequestBody) copySafeHeader(result, source, 'content-type', 512)

  if (method === 'GET' || method === 'HEAD') {
    const range = source.get('range')
    if (range && range.length <= 128 && /^bytes=(?:\d+-\d*|-\d+)$/.test(range.trim())) {
      result.set('Range', range.trim())
    }
    copySafeHeader(result, source, 'if-range', 256)
    copySafeHeader(result, source, 'if-none-match', 1024)
    copySafeHeader(result, source, 'if-modified-since', 128)
  }

  const clientIp = readTrustedClientIp(source)
  if (clientIp) {
    result.set('X-Forwarded-For', clientIp)
    result.set('X-SwapMid-Client-Ip', clientIp)
  }
  const edgeToken = readForwardableEdgeToken(source)
  if (edgeToken) result.set(EDGE_TOKEN_HEADER, edgeToken)

  const traceparent = source.get('traceparent')?.trim()
  if (traceparent && W3C_TRACEPARENT_PATTERN.test(traceparent)) {
    result.set('traceparent', traceparent.toLowerCase())
  }

  return result
}

export function selectProxyResponseHeaders(source: Headers): Headers {
  const result = selectForwardedResponseHeaders(source, SAFE_RESPONSE_HEADERS)
  result.set('Cache-Control', 'private, no-store')
  return result
}
