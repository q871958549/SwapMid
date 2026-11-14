import 'server-only'

const LOCAL_API_URL = 'https://localhost:44362'

/**
 * 服务端回源统一事实源：生产优先走 Docker 内网；本地/测试才回退公开地址。
 *
 * 基址只接受纯 HTTP(S) origin。拒绝凭据、query、fragment、业务路径与首尾空白，
 * 避免配置值悄悄改变请求目标或把敏感信息带进 URL 语义。
 */
export function resolveBackendApiBase(
  environment: Record<string, string | undefined> = process.env,
): string {
  const raw = environment.INTERNAL_API_URL ?? environment.NEXT_PUBLIC_API_URL ?? LOCAL_API_URL
  if (raw !== raw.trim() || /[\u0000-\u001f\u007f]/.test(raw)) {
    throw new Error('Backend API base URL is invalid')
  }

  let parsed: URL
  try {
    parsed = new URL(raw)
  } catch {
    throw new Error('Backend API base URL is invalid')
  }

  if (
    (parsed.protocol !== 'http:' && parsed.protocol !== 'https:')
    || parsed.username
    || parsed.password
    || parsed.search
    || parsed.hash
    || (parsed.pathname !== '/' && parsed.pathname !== '')
  ) {
    throw new Error('Backend API base URL must be an HTTP(S) origin')
  }

  return parsed.origin
}
