import { type NextRequest, NextResponse } from 'next/server'
import { buildAcceptLanguage } from '@/lib/locale'
import { hasPermission, Permissions } from '@/lib/permissions'
import {
  getValidSessionResult,
  invalidateSessionIfUnchanged,
} from '@/lib/session'
import {
  SESSION_IDENTITY_CHANGED_CODE,
  SESSION_IDENTITY_PRECONDITION_CODE,
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_UNAVAILABLE_CODE,
  extractSessionIdentitySearch,
  sessionIdentityChangedHeaders,
  sessionUnavailableHeaders,
} from '@/lib/session-availability'
import {
  getSessionIdentityVersion,
  hasExplicitSessionIdentityVersion,
} from '@/lib/session-identity'
import {
  ForwardingPayloadTooLargeError,
  ForwardingRequestIdleTimeoutError,
  limitRequestBody,
  streamForwardedResponseBody,
  validateContentLength,
} from '../../backend-forwarding'
import {
  PLATFORM_UPDATING_CODE,
  PLATFORM_UPDATING_RETRY_AFTER_SECONDS,
} from '@/lib/platform-updating'
import {
  StartupUnavailableError,
  fetchWithStartupRetry,
  isStartupConnectFailure,
} from '../proxy-startup-retry'
import {
  buildProxyBackendPath,
  getProxyRequestPolicy,
  isValidProxyQuery,
  resolveProxyApiBase,
  selectProxyRequestHeaders,
  selectProxyResponseHeaders,
} from '../proxy-forwarding'

const API_BASE = resolveProxyApiBase()

type ProbeAnswer = 'open' | 'not-ready' | 'absent' | 'down'

async function probeAcceptsRequest(target: string, signal: AbortSignal): Promise<ProbeAnswer> {
  try {
    const response = await fetch(target, {
      method: 'GET',
      cache: 'no-store',
      redirect: 'manual',
      signal,
    })
    await response.body?.cancel().catch(() => undefined)
    if (response.status === 200) return 'open'
    if (response.status === 503) return 'not-ready'
    if (response.status === 404) return 'absent'
    return 'open'
  } catch (error) {
    if (isStartupConnectFailure(error)) return 'down'
    throw error
  }
}

async function apiAcceptsWrites(signal: AbortSignal): Promise<boolean> {
  const ready = await probeAcceptsRequest(`${API_BASE}/health/ready`, signal)
  if (ready === 'open') return true
  if (ready === 'not-ready' || ready === 'down') return false
  const live = await probeAcceptsRequest(`${API_BASE}/health/live`, signal)
  if (live === 'open') return true
  if (live === 'not-ready' || live === 'down') return false
  return true
}

type DeadlinePhase = 'response-headers' | 'total'

function jsonError(error: string, status: number): NextResponse {
  return NextResponse.json({ error }, {
    status,
    headers: { 'Cache-Control': 'private, no-store' },
  })
}

function platformUpdatingResponse(): NextResponse {
  return NextResponse.json(
    { error: 'Service Unavailable', code: PLATFORM_UPDATING_CODE },
    {
      status: 503,
      headers: {
        'Cache-Control': 'private, no-store',
        'Retry-After': String(PLATFORM_UPDATING_RETRY_AFTER_SECONDS),
      },
    },
  )
}

function sessionUnavailableError(retryAfterSeconds: number): NextResponse {
  return NextResponse.json(
    { error: 'Service Unavailable', code: SESSION_UNAVAILABLE_CODE },
    {
      status: 503,
      headers: sessionUnavailableHeaders(retryAfterSeconds),
    },
  )
}

function sessionIdentityChangedError(identityVersion: string): NextResponse {
  return NextResponse.json(
    { error: 'Conflict', code: SESSION_IDENTITY_CHANGED_CODE },
    {
      status: 409,
      headers: sessionIdentityChangedHeaders(identityVersion),
    },
  )
}

function sessionIdentityPreconditionError(identityVersion: string): NextResponse {
  return NextResponse.json(
    { error: 'Precondition Required', code: SESSION_IDENTITY_PRECONDITION_CODE },
    {
      status: 401,
      headers: sessionIdentityChangedHeaders(identityVersion),
    },
  )
}

async function handler(req: NextRequest, { params }: { params: Promise<{ path: string[] }> }) {
  const { path } = await params
  const proxyPath = buildProxyBackendPath(path)
  if (!proxyPath || !isValidProxyQuery(req.nextUrl.search)) {
    return jsonError('Not Found', 404)
  }
  const identitySearch = extractSessionIdentitySearch(req.nextUrl.search)
  if (!identitySearch || identitySearch.identityVersions.length > 1) {
    return jsonError('Invalid identity precondition', 400)
  }
  const forwardedSearch = identitySearch.forwardedSearch

  const sessionResult = await getValidSessionResult()
  if (sessionResult.status === 'missing') {
    return jsonError('Unauthorized', 401)
  }
  if (sessionResult.status === 'unavailable') {
    return sessionUnavailableError(sessionResult.retryAfterSeconds)
  }
  if (sessionResult.status === 'identity-changed') {
    return sessionIdentityChangedError(
      getSessionIdentityVersion(sessionResult.session),
    )
  }
  const session = sessionResult.session

  const method = req.method.toUpperCase()
  const headerIdentityVersion = req.headers.get(SESSION_IDENTITY_VERSION_HEADER)
  const queryIdentityVersion = identitySearch.identityVersions[0] ?? null
  const currentIdentityVersion = getSessionIdentityVersion(session)
  if (
    (headerIdentityVersion !== null && headerIdentityVersion !== currentIdentityVersion)
    || (queryIdentityVersion !== null && queryIdentityVersion !== currentIdentityVersion)
  ) {
    return sessionIdentityChangedError(currentIdentityVersion)
  }
  if (
    headerIdentityVersion === null
    && queryIdentityVersion === null
    && hasExplicitSessionIdentityVersion(session)
  ) {
    return sessionIdentityPreconditionError(currentIdentityVersion)
  }

  const contentType = req.headers.get('content-type')
  const policy = getProxyRequestPolicy(proxyPath.canonicalPath, method, contentType, {
    grantedPolicies: session.grantedPolicies,
    canExecuteDataPipelines: hasPermission(
      session.grantedPolicies,
      Permissions.DataIntegration.DataPipelinesExecute,
    ),
    canResolvePipelineExecutionOutcome: hasPermission(
      session.grantedPolicies,
      Permissions.DataIntegration.PipelineExecutionsResolveOutcome,
    ),
    canVerifyPipelineExecutionTargetReceipt: hasPermission(
      session.grantedPolicies,
      Permissions.DataIntegration.PipelineExecutionsVerifyTargetReceipt,
    ),
  })
  if (policy.forbidden) {
    return jsonError('Forbidden', 403)
  }
  if (policy.invalidMultipartBoundary) {
    return jsonError('Invalid multipart boundary', 400)
  }

  const mayHaveRequestBody = method !== 'GET' && method !== 'HEAD'
  if (mayHaveRequestBody) {
    const lengthValidation = validateContentLength(
      req.headers.get('content-length'),
      policy.maxBodyBytes,
    )
    if (lengthValidation === 'invalid') {
      return jsonError('Invalid Content-Length', 400)
    }
    if (lengthValidation === 'too-large') {
      return jsonError('Payload Too Large', 413)
    }
  }

  const hasRequestBody = mayHaveRequestBody && req.body != null
  const contentEncoding = req.headers.get('content-encoding')?.trim().toLowerCase()
  if (hasRequestBody && contentEncoding && contentEncoding !== 'identity') {
    return jsonError('Unsupported Content-Encoding', 415)
  }

  const url = `${API_BASE}${proxyPath.backendPath}${forwardedSearch}`
  const headers = selectProxyRequestHeaders(
    req.headers,
    method,
    session.accessToken,
    buildAcceptLanguage('zh-Hans'),
    hasRequestBody,
  )

  const bodyController = new AbortController()
  const deadlineController = new AbortController()
  const requestStopController = new AbortController()
  const exchangeSignal = AbortSignal.any([req.signal, deadlineController.signal])
  const signal = AbortSignal.any([exchangeSignal, bodyController.signal])
  let deadlinePhase: DeadlinePhase | undefined
  let responseHeadersSettled = false
  let responseHeadersTimer: ReturnType<typeof setTimeout> | undefined
  let totalTimer: ReturnType<typeof setTimeout> | undefined
  let requestFinalized = !hasRequestBody
  let responseFinalized = false

  const abortForDeadline = (phase: DeadlinePhase, message: string) => {
    if (deadlineController.signal.aborted) return
    deadlinePhase = phase
    deadlineController.abort(new DOMException(message, 'TimeoutError'))
  }
  const armResponseHeadersTimer = () => {
    if (responseHeadersSettled || responseHeadersTimer || deadlineController.signal.aborted) return
    responseHeadersTimer = setTimeout(() => {
      abortForDeadline('response-headers', 'proxy upstream response headers timeout')
    }, policy.responseHeadersTimeoutMs)
  }
  const clearResponseHeadersTimer = () => {
    if (responseHeadersTimer) clearTimeout(responseHeadersTimer)
    responseHeadersTimer = undefined
  }
  const clearTotalTimer = () => {
    if (totalTimer) clearTimeout(totalTimer)
    totalTimer = undefined
  }
  const finalizeExchangeIfSettled = () => {
    if (requestFinalized && responseFinalized) clearTotalTimer()
  }
  const finalizeResponseSide = () => {
    if (responseFinalized) return
    responseFinalized = true
    if (!requestFinalized && !requestStopController.signal.aborted) {
      requestStopController.abort(
        new DOMException('upstream final response completed', 'AbortError'),
      )
    }
    finalizeExchangeIfSettled()
  }

  totalTimer = setTimeout(() => {
    abortForDeadline('total', 'proxy total deadline exceeded')
  }, policy.totalTimeoutMs)

  let requestBodyStarted = false
  const init: RequestInit & { duplex?: 'half' } = {
    method,
    headers,
    cache: 'no-store',
    redirect: 'manual',
    signal,
  }

  let res: Response
  try {
    const idempotent = method === 'GET' || method === 'HEAD'
    res = await fetchWithStartupRetry(url, init, {
      idempotent,
      probe: idempotent ? undefined : () => apiAcceptsWrites(signal),
      fetchImpl: async () => {
        try {
          if (hasRequestBody) {
            init.body = limitRequestBody(req.body!, policy.maxBodyBytes, bodyController, {
              idleTimeoutMs: policy.requestIdleTimeoutMs,
              signal: exchangeSignal,
              gracefulStopSignal: requestStopController.signal,
              onComplete: armResponseHeadersTimer,
              onFinalize: () => {
                requestFinalized = true
                finalizeExchangeIfSettled()
              },
            })
            requestBodyStarted = true
            init.duplex = 'half'
          } else {
            armResponseHeadersTimer()
          }
          return await fetch(url, init)
        } catch (error) {
          clearResponseHeadersTimer()
          throw error
        }
      },
    })
    responseHeadersSettled = true
  } catch (error) {
    responseHeadersSettled = true
    clearResponseHeadersTimer()

    if (hasRequestBody && !requestBodyStarted) {
      requestFinalized = true
      void req.body!.cancel(error).catch(() => undefined)
    }
    const abortReason = signal.aborted && error === signal.reason ? signal.reason : undefined
    finalizeResponseSide()
    if (abortReason instanceof ForwardingPayloadTooLargeError) {
      return jsonError('Payload Too Large', 413)
    }
    if (abortReason instanceof ForwardingRequestIdleTimeoutError) {
      return jsonError('Request Timeout', 408)
    }
    if (req.signal.aborted && abortReason === req.signal.reason) {
      return new Response(null, {
        status: 499,
        headers: { 'Cache-Control': 'private, no-store' },
      })
    }
    if (
      deadlinePhase
      && deadlineController.signal.aborted
      && abortReason === deadlineController.signal.reason
    ) {
      return jsonError('Gateway Timeout', 504)
    }
    if (error instanceof StartupUnavailableError) {
      return platformUpdatingResponse()
    }
    return jsonError('Bad Gateway', 502)
  }
  clearResponseHeadersTimer()

  if (res.status === 401) {
    finalizeResponseSide()
    if (res.body) void res.body.cancel().catch(() => undefined)
    const invalidation = await invalidateSessionIfUnchanged(session)
    if (invalidation.status === 'unavailable') {
      return sessionUnavailableError(invalidation.retryAfterSeconds)
    }
    if (
      invalidation.status === 'identity-changed'
      || invalidation.status === 'session-updated'
    ) {
      return sessionIdentityChangedError(
        getSessionIdentityVersion(invalidation.session),
      )
    }
    return jsonError('Unauthorized', 401)
  }

  const responseHeaders = selectProxyResponseHeaders(res.headers)
  const bodyForbidden = method === 'HEAD'
    || res.status === 204
    || res.status === 205
    || res.status === 304
  if (bodyForbidden || !res.body) {
    finalizeResponseSide()
    if (res.body) void res.body.cancel().catch(() => undefined)
    return new Response(null, {
      status: res.status,
      statusText: res.statusText,
      headers: responseHeaders,
    })
  }

  if (!responseHeaders.has('content-type')) {
    responseHeaders.set('Content-Type', 'application/json')
  }
  const responseBody = streamForwardedResponseBody(res.body, {
    idleTimeoutMs: policy.responseIdleTimeoutMs,
    upstreamAbortController: deadlineController,
    signal,
    onFinalize: finalizeResponseSide,
  })

  return new Response(responseBody, {
    status: res.status,
    statusText: res.statusText,
    headers: responseHeaders,
  })
}

export { handler as GET, handler as POST, handler as PUT, handler as DELETE, handler as PATCH, handler as HEAD }
