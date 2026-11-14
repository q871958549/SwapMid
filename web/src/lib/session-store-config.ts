import 'server-only'
import {
  resolveSessionStoreConfiguration as resolveSessionStoreConfigurationCore,
} from './session-store-config-core.mjs'

export type SessionStoreMode = 'redis' | 'memory'

export interface SessionStoreConfiguration {
  mode: SessionStoreMode
  redisUrl?: string
}

type SessionStoreEnvironment = Record<string, string | undefined>

/**
 * 服务端运行期复用与启动前预检完全相同的纯配置解析器；此包装层负责保留 server-only 边界。
 */
export function resolveSessionStoreConfiguration(
  environment: SessionStoreEnvironment = process.env,
): SessionStoreConfiguration {
  return resolveSessionStoreConfigurationCore(environment)
}

/** Next instrumentation 的同步启动门禁；不得在失败后吞异常继续启动。 */
export function assertSessionStoreConfiguration(): void {
  resolveSessionStoreConfiguration()
}
