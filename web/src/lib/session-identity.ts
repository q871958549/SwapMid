import { createHash } from 'node:crypto'
import type { FullSession } from './session-types'

type SessionIdentity = Pick<FullSession, 'identityVersion' | 'userId' | 'userName'>

const STORAGE_PRINCIPAL_ID_PREFIX = 'sp1-'

export function hasExplicitSessionIdentityVersion(
  session: SessionIdentity,
): boolean {
  return typeof session.identityVersion === 'string'
    && session.identityVersion.length >= 8
    && session.identityVersion.length <= 128
}

/**
 * 每次登录、开始模拟、切回或自动结束模拟时生成新的身份世代。
 * 该值不是鉴权凭据，只用于拒绝“旧页面 + 新服务端身份”的错身份请求。
 */
export function createSessionIdentityVersion(): string {
  return crypto.randomUUID()
}

/**
 * 兼容部署前已存在于 Redis 的旧会话：不强制全员掉线，而是按账号生成稳定、ASCII 安全的
 * 过渡版本。会话下一次刷新或身份切换后会自然升级为随机 UUID。
 */
export function getSessionIdentityVersion(session: SessionIdentity): string {
  if (hasExplicitSessionIdentityVersion(session)) {
    return session.identityVersion!
  }

  return `legacy-${createHash('sha256')
    .update(session.userName.normalize('NFKC').toUpperCase(), 'utf8')
    .digest('base64url')}`
}

/**
 * 浏览器持久状态的账号隔离键。优先绑定 ABP 的稳定 userId；滚动发布期间旧 Redis
 * 会话尚无 userId 时，回退到与 legacy identity version 相同规则规范化的 userName。
 *
 * 返回值固定为 `sp1-` + 43 字符 SHA-256 base64url（总长 47），不会暴露账号主键或
 * 用户名。哈希输入带来源域分隔，避免 userId 与同文本 userName 相撞。它只用于存储
 * 命名空间隔离，不是鉴权凭据，不能据此授予任何权限。
 */
export function getSessionStoragePrincipalId(session: SessionIdentity): string {
  const userId = typeof session.userId === 'string'
    ? session.userId.trim()
    : ''
  const principal = userId
    ? `uid\u0000${userId}`
    : `username\u0000${session.userName.normalize('NFKC').trim().toUpperCase()}`

  return `${STORAGE_PRINCIPAL_ID_PREFIX}${createHash('sha256')
    .update(principal, 'utf8')
    .digest('base64url')}`
}
