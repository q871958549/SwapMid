import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import {
  resolveWebSingletonLeaseConfiguration,
} from './web-singleton-lease-core.mjs'

function readWorkspaceFile(path: string): string {
  return readFileSync(new URL(path, import.meta.url), 'utf8')
}

function readWebService(compose: string): string {
  const lines = compose.split(/\r?\n/)
  const start = lines.findIndex((line) => line === '  web:')
  expect(start, 'compose must contain a bounded web service').toBeGreaterThanOrEqual(0)
  let end = lines.length
  for (let index = start + 1; index < lines.length; index += 1) {
    const line = lines[index] ?? ''
    if (/^  [A-Za-z0-9_-]+:$/.test(line) || /^[A-Za-z0-9_-]+:$/.test(line)) {
      end = index
      break
    }
  }
  return lines.slice(start, end).join('\n')
}

function readSeconds(service: string, setting: string): number {
  const match = service.match(new RegExp(
    `^\\s+${setting}:\\s*(\\d+)s\\s*$`,
    'm',
  ))
  expect(match, `${setting} must be an explicit seconds duration`).not.toBeNull()
  return Number(match?.[1]) * 1_000
}

describe('生产会话存储部署契约', () => {
  it('Next 运行期必须保留 fail-closed 纵深门禁', () => {
    const instrumentation = readWorkspaceFile('../instrumentation.ts')
    const nodeInstrumentation = readWorkspaceFile('../instrumentation-node.ts')
    const serverConfig = readWorkspaceFile('./session-store-config.ts')

    expect(nodeInstrumentation).toContain(
      "from '@/lib/web-singleton-lease-core.mjs'",
    )
    expect(instrumentation).toMatch(
      /NEXT_RUNTIME === 'nodejs'[\s\S]*import\([\s\S]*'\.\/instrumentation-node'/,
    )
    expect(nodeInstrumentation).toMatch(
      /export async function registerNodeInstrumentation\(\)[\s\S]*assertProductionWebLeaseSupervision\(\)[\s\S]*startWebSingletonParentWatchdog\(\)[\s\S]*process\.exit\(78\)[\s\S]*assertSessionStoreConfiguration\(\)/,
    )
    expect(nodeInstrumentation).toContain(
      "import { assertSessionStoreConfiguration } from '@/lib/session-store-config'",
    )
    expect(serverConfig).toContain("from './session-store-config-core.mjs'")
  })

  it('生产 package start 必须只进入租约包装器，不得直接 next start', () => {
    const packageJson = JSON.parse(
      readWorkspaceFile('../../../web/package.json'),
    ) as { scripts?: Record<string, string> }
    const productionStart = readWorkspaceFile(
      '../../../web/scripts/start-production.mjs',
    )

    expect(packageJson.scripts?.start).toBe(
      'node scripts/start-production.mjs',
    )
    expect(packageJson.scripts?.start).not.toContain('next start')
    expect(productionStart).toContain(
      "from '../src/lib/session-store-config-core.mjs'",
    )
    expect(productionStart).toMatch(
      /resolveSessionStoreConfiguration\(\{[\s\S]*\.\.\.process\.env,[\s\S]*NODE_ENV: 'production'/,
    )
    expect(productionStart).toContain(
      "from '../src/lib/web-singleton-lease-core.mjs'",
    )
    expect(productionStart).toMatch(
      /await connectRedis\(redis, leaseConfiguration\.redisReadyTimeoutMs\)[\s\S]*await supervisor\.start\(\)/,
    )
    expect(productionStart).toMatch(
      /\[nextCliPath, 'start', \.\.\.process\.argv\.slice\(2\)\]/,
    )
    expect(productionStart).toContain(
      "[WEB_LEASE_SUPERVISED_ENV]: '1'",
    )
    expect(productionStart).toContain(
      '[WEB_LEASE_WATCHDOG_ENV]: String(',
    )
    expect(productionStart).toContain(
      "stdio: ['inherit', 'inherit', 'inherit', 'ipc']",
    )
    expect(productionStart).toContain(
      'takeoverIsolationMs: leaseConfiguration.takeoverIsolationMs',
    )
    expect(productionStart).toContain(
      'connectTimeout: leaseConfiguration.redisConnectTimeoutMs',
    )
    expect(productionStart).toContain(
      'commandTimeout: leaseConfiguration.redisCommandTimeoutMs',
    )
    expect(productionStart).toContain(
      'closeRedis(redis, leaseConfiguration.redisCloseTimeoutMs)',
    )
    expect(productionStart).not.toContain('WEB_SINGLETON_LEASE_OWNER')
  })

  it('docker-compose 必须显式选择 Redis、租约 key 与 TTL 且覆盖停机/启动预算', () => {
    const compose = readWorkspaceFile('../../../docker/docker-compose.yml')
    expect(compose).toMatch(/SESSION_STORE_MODE:\s*redis/)
    expect(compose).toMatch(/REDIS_URL:\s*"redis:\/\//)
    expect(compose).toMatch(/WEB_SINGLETON_LEASE_KEY:\s*"swapmid:/)
    expect(compose).toMatch(/WEB_SINGLETON_LEASE_TTL_MS:\s*"30000"/)

    const webService = readWebService(compose)
    const key = webService.match(/WEB_SINGLETON_LEASE_KEY:\s*"([^"]+)"/)?.[1]
    const ttl = webService.match(/WEB_SINGLETON_LEASE_TTL_MS:\s*"(\d+)"/)?.[1]
    const takeoverIsolation = webService.match(/WEB_SINGLETON_TAKEOVER_ISOLATION_MS:\s*"(\d+)"/)?.[1]
    const configuration = resolveWebSingletonLeaseConfiguration({
      WEB_SINGLETON_LEASE_KEY: key,
      WEB_SINGLETON_LEASE_TTL_MS: ttl,
      WEB_SINGLETON_TAKEOVER_ISOLATION_MS: takeoverIsolation,
    })

    expect(configuration.containerStopBudgetMs).toBe(20_000)
    expect(readSeconds(webService, 'stop_grace_period')).toBeGreaterThanOrEqual(
      configuration.containerStopBudgetMs,
    )

    expect(configuration.cleanStartBudgetMs).toBe(33_500)
    expect(readSeconds(webService, 'start_period')).toBeGreaterThanOrEqual(
      configuration.cleanStartBudgetMs,
    )
  })

  it('生产镜像必须让租约包装器成为 PID 1', () => {
    const dockerfile = readWorkspaceFile('../../../web/Dockerfile')

    expect(dockerfile).toContain('SESSION_STORE_MODE=redis')
    expect(dockerfile).toContain(
      'CMD ["node", "scripts/start-production.mjs"]',
    )
    expect(dockerfile).not.toContain('CMD ["pnpm", "start"]')
  })

  it('真实 Redis 集成必须先等待容器内部就绪再连接宿主映射端口', () => {
    const integration = readWorkspaceFile(
      '../../scripts/session-refresh-redis.integration.mjs',
    )

    const readinessProbe = integration.indexOf(
      "['exec', container, 'redis-cli', '--raw', 'ping']",
    )
    const readinessCall = integration.indexOf('await waitForRedisReady()')
    const mappedClient = integration.indexOf('const redis = new Redis(redisUrl')
    expect(readinessProbe).toBeGreaterThan(-1)
    expect(readinessCall).toBeGreaterThan(readinessProbe)
    expect(mappedClient).toBeGreaterThan(readinessCall)
    expect(integration).toContain('Redis container did not become ready within 20s')
    expect(integration).toContain(
      'const keepAlive = setInterval(() => undefined, 1_000)',
    )
    expect(integration).toContain('clearInterval(keepAlive)')
  })
})
