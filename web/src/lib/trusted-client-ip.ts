import { isIP } from 'node:net'

/** Caddy 按 TCP 对端写入的专用来源头。浏览器自带的同名头会先被 Caddy 丢掉。 */
export const TRUSTED_CLIENT_IP_HEADER = 'X-SwapMid-Client-Ip'

/** Caddy 写入的边车令牌。客户端自带的同名头会先被丢掉。不要写入日志。 */
export const EDGE_TOKEN_HEADER = 'X-SwapMid-Edge-Token'

const MAX_HEADER_LENGTH = 128

const CLIENT_SOURCE_HEADERS = [
  'x-forwarded-for',
  'x-real-ip',
  'forwarded',
  'x-swapmid-client-ip',
  'x-client-ip',
  'cf-connecting-ip',
  'true-client-ip',
  'x-cluster-client-ip',
  'x-original-for',
  'x-original-proto',
  'x-original-host',
  'x-original-prefix',
  'x-swapmid-edge-token',
] as const

const EDGE_TOKEN_PATTERN = /^[A-Za-z0-9+/=_-]{32,256}$/

type HeaderReader = Pick<Headers, 'get'>

/**
 * 只接受单个裸 IP。链、端口、unknown、控制字符、超长、带括号或作用域的值都丢掉。
 * 专用来源头与 X-Forwarded-For 同时存在时必须是同一个地址；只有专用来源头时不采信。
 * 仅有单个 X-Forwarded-For 时接受，兼容 Caddy 尚未写入专用来源头的过渡。
 */
export function readTrustedClientIp(headers: HeaderReader): string | undefined {
  const dedicated = classifyHeader(headers.get('x-swapmid-client-ip'))
  const forwarded = classifyHeader(headers.get('x-forwarded-for'))
  if (dedicated.kind === 'invalid' || forwarded.kind === 'invalid') return undefined
  if (dedicated.kind === 'ip' && forwarded.kind === 'ip') {
    return dedicated.value.toLowerCase() === forwarded.value.toLowerCase()
      ? forwarded.value
      : undefined
  }
  if (dedicated.kind === 'ip') return undefined
  if (forwarded.kind === 'ip') return forwarded.value
  return undefined
}

/** 格式合法才转发。空值、超长和注入字符都丢掉，调用方不能用它覆盖 Caddy 写入的值。 */
export function readForwardableEdgeToken(headers: HeaderReader): string | undefined {
  const raw = headers.get('x-swapmid-edge-token')
  if (!raw || !EDGE_TOKEN_PATTERN.test(raw)) return undefined
  return raw
}

/** 先清掉目标请求上的来源头，再写入 Caddy 已清洗的单个 IP 和边车令牌。取不到就不写。 */
export function applyTrustedClientIp(target: Headers, incoming: HeaderReader): void {
  for (const name of CLIENT_SOURCE_HEADERS) target.delete(name)
  const ip = readTrustedClientIp(incoming)
  if (ip) {
    target.set('X-Forwarded-For', ip)
    target.set(TRUSTED_CLIENT_IP_HEADER, ip)
  }
  const edgeToken = readForwardableEdgeToken(incoming)
  if (edgeToken) target.set(EDGE_TOKEN_HEADER, edgeToken)
}

/**
 * 普通对象保持原有头名大小写，避免把调用方的 Authorization 等头改成 Headers 实例。
 * Headers / 二元组则走 Headers，删除逻辑与上面一致。
 */
export function withTrustedClientIp(
  headers: HeadersInit | undefined,
  incoming: HeaderReader,
): HeadersInit {
  if (headers instanceof Headers || Array.isArray(headers)) {
    const target = new Headers(headers)
    applyTrustedClientIp(target, incoming)
    return target
  }

  const next: Record<string, string> = { ...(headers ?? {}) }
  for (const key of Object.keys(next)) {
    if (CLIENT_SOURCE_HEADERS.includes(key.toLowerCase() as typeof CLIENT_SOURCE_HEADERS[number])) {
      delete next[key]
    }
  }
  const ip = readTrustedClientIp(incoming)
  if (ip) {
    next['X-Forwarded-For'] = ip
    next[TRUSTED_CLIENT_IP_HEADER] = ip
  }
  const edgeToken = readForwardableEdgeToken(incoming)
  if (edgeToken) next[EDGE_TOKEN_HEADER] = edgeToken
  return next
}

function classifyHeader(raw: string | null): { kind: 'absent' } | { kind: 'invalid' } | { kind: 'ip'; value: string } {
  if (raw == null) return { kind: 'absent' }
  if (raw.length === 0 || raw.length > MAX_HEADER_LENGTH || /[\u0000-\u001f\u007f]/.test(raw)) {
    return { kind: 'invalid' }
  }

  const candidate = raw.trim()
  if (
    !candidate
    || candidate.includes(',')
    || candidate.includes('%')
    || candidate.includes('[')
    || candidate.includes(']')
    || candidate.includes(' ')
    || (candidate.includes(':') && candidate.includes('.'))
    || isIP(candidate) === 0
  ) {
    return { kind: 'invalid' }
  }

  return { kind: 'ip', value: candidate }
}
