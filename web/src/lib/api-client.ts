import {
  getValidSessionResult,
  invalidateSessionIfUnchanged,
} from '@/lib/session'
import {
  redirectSessionFailure,
  redirectSessionIdentityChanged,
} from '@/lib/session-navigation'
import { unstable_rethrow } from 'next/navigation'
import { headers } from 'next/headers'
import { buildAcceptLanguage } from '@/lib/locale'
import { getSessionIdentityVersion } from '@/lib/session-identity'
import {
  SERVER_BACKEND_DEADLINE_MS,
  SERVER_BACKEND_RESPONSE_LIMIT_BYTES,
  serverBackendFetch,
} from '@/lib/server-backend-fetch'

// 当前请求路径（middleware 注入到 x-swapmid-pathname），供登录后回跳；取不到则不带回跳。
async function currentPathname(): Promise<string> {
  try {
    return (await headers()).get('x-swapmid-pathname') ?? ''
  } catch {
    return ''
  }
}

// 平台界面固定简体。旧的 swapmid_language cookie 不再改变后端错误文案。
async function currentAcceptLanguage(): Promise<string> {
  return buildAcceptLanguage('zh-Hans')
}

/**
 * 将 Next 控制流异常置于 apiFetch 链上的显式 `.catch` fallback 之前。
 * 大量 Server Component 对可选业务数据使用 `.catch(fallback)`；普通网络/业务错误仍可降级，
 * 但 redirect/notFound/动态渲染等框架信号绝不能被这些 fallback 吞掉。
 */
class NextControlFlowSafePromise<T> implements Promise<T> {
  readonly [Symbol.toStringTag] = 'Promise'

  constructor(private readonly inner: Promise<T>) {}

  then<TResult1 = T, TResult2 = never>(
    onfulfilled?: ((value: T) => TResult1 | PromiseLike<TResult1>) | null,
    onrejected?: ((reason: unknown) => TResult2 | PromiseLike<TResult2>) | null,
  ): Promise<TResult1 | TResult2> {
    // `await` / Promise.all 通过 then(resolve, reject) 同化 thenable，必须让其 reject
    // 回调看到异常；业务代码的显式 `.catch(fallback)` 则由下方 catch 守卫。
    const next = this.inner.then(
      onfulfilled ?? undefined,
      onrejected ?? undefined,
    )
    return new NextControlFlowSafePromise(next)
  }

  catch<TResult = never>(
    onrejected?: ((reason: unknown) => TResult | PromiseLike<TResult>) | null,
  ): Promise<T | TResult> {
    const next = this.inner.catch((reason: unknown) => {
      unstable_rethrow(reason)
      if (onrejected) return onrejected(reason)
      throw reason
    })
    return new NextControlFlowSafePromise(next)
  }

  finally(onfinally?: (() => void) | null): Promise<T> {
    return new NextControlFlowSafePromise(
      this.inner.finally(onfinally ?? undefined),
    )
  }
}

/** `await apiFetch()` 外层 try/catch 的第一句必须调用本函数，再处理普通业务错误。 */
export function rethrowApiFetchControlFlow(error: unknown): void {
  unstable_rethrow(error)
}

export function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  return new NextControlFlowSafePromise(apiFetchInternal<T>(path, init))
}

async function apiFetchInternal<T>(path: string, init?: RequestInit): Promise<T> {
  const sessionResult = await getValidSessionResult()
  if (sessionResult.status === 'identity-changed') {
    return redirectSessionIdentityChanged(await currentPathname())
  }
  const session = sessionResult.status === 'found'
    ? sessionResult.session
    : redirectSessionFailure(sessionResult, await currentPathname())

  const reqHeaders: Record<string, string> = {
    'Content-Type': 'application/json',
    'Accept-Language': await currentAcceptLanguage(),
    ...(init?.headers as Record<string, string>),
  }
  reqHeaders['Authorization'] = `Bearer ${session.accessToken}`

  // Paths starting with /api/ (e.g. /api/identity/users) are used as-is
  // Other paths get /api/v1 prepended (our custom controllers)
  const fullPath = path.startsWith('/api/') ? path : `/api/v1${path}`
  const res = await serverBackendFetch(fullPath, {
    ...init,
    signal: init?.signal ?? undefined,
    headers: reqHeaders,
    deadlineMs: SERVER_BACKEND_DEADLINE_MS.authenticatedApi,
    maxResponseBytes: SERVER_BACKEND_RESPONSE_LIMIT_BYTES.authenticatedApiJson,
  })

  // 令牌在后端侧失效（极少见：被吊销 / 签名密钥变更）→ 同样引导重新登录，而非抛错白屏。
  if (res.status === 401) {
    const invalidation = await invalidateSessionIfUnchanged(session)
    if (invalidation.status === 'unavailable') {
      return redirectSessionFailure(
        invalidation,
        await currentPathname(),
      )
    }
    if (
      invalidation.status === 'identity-changed'
      || invalidation.status === 'session-updated'
    ) {
      return redirectSessionIdentityChanged(await currentPathname())
    }
    return redirectSessionFailure(
      { status: 'missing' },
      await currentPathname(),
      getSessionIdentityVersion(session),
    )
  }

  if (!res.ok) {
    // 保留稳定的 HTTP 状态分类口径，但不把可能含内部详情/敏感字段的原始正文写进日志。
    throw new Error(`API ${res.status}: request failed`)
  }

  if (res.status === 204) return undefined as T

  const contentType = res.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json') && !contentType.includes('text/json')) {
    throw new Error(`API returned non-JSON (${res.status})`)
  }

  return res.json()
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
}
