import 'server-only'

/**
 * 运行时发布标识的环境变量名。
 *
 * 故意不是 `NEXT_PUBLIC_*`。那个前缀会在 `pnpm build` 时写进客户端包，
 * 发布标识一变，构建层就无法跨版本复用。容器环境在启动时注入，
 * 服务端渲染再把原文传给登录页和侧栏。
 */
export const RUNTIME_RELEASE_ENV = 'SWAPMID_RELEASE'

export function readRuntimeReleaseTag(
  env: Record<string, string | undefined> = process.env,
): string {
  const value = env[RUNTIME_RELEASE_ENV]
  return typeof value === 'string' ? value.trim() : ''
}
