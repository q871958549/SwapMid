export interface SessionPayload {
  /** ABP Identity 稳定账号主键；可选仅为兼容发布前最长 8 小时的旧 Redis 会话。 */
  userId?: string
  userName: string
  name: string
  grantedPolicies: Record<string, boolean>
  /** 登录时镜像的账号界面偏好，供 SSR 首屏使用。 */
  theme?: string
  fontSize?: number
  language?: string
}

export interface FullSession extends SessionPayload {
  /** 浏览器文档与服务端身份的一致性世代；旧 Redis 会话可能暂缺。 */
  identityVersion?: string
  accessToken: string
  refreshToken?: string
  /** access token 的 epoch 毫秒到期时间，与空闲 8 小时 / 绝对 7 天 session store TTL 相互独立。 */
  accessTokenExpiresAt: number
  /** 首次签发时刻（epoch ms）。旧 Redis 会话可能暂缺，刷新时补上并开始 7 天封顶。 */
  issuedAt?: number
  /** 模拟登录时只在服务端 session store 保存的原身份快照。 */
  impersonator?: {
    userId?: string
    userName: string
    name: string
    grantedPolicies: Record<string, boolean>
    identityVersion?: string
    accessToken: string
    refreshToken?: string
    accessTokenExpiresAt: number
  }
}
