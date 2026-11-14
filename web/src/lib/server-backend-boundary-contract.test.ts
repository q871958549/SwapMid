import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC_ROOT = resolve(process.cwd(), 'src')
const DIRECT_CALL_TARGETS = [
  'lib/api-client.ts',
  'lib/abp-config.ts',
  'lib/session-refresh.ts',
  'app/actions/auth.ts',
  'app/api/auth/start-impersonation/route.ts',
] as const

const SOURCE_CACHE = new Map<string, string>()

function readSource(relativePath: string): string {
  const cached = SOURCE_CACHE.get(relativePath)
  if (cached !== undefined) return cached
  const content = readFileSync(`${SRC_ROOT}/${relativePath}`, 'utf8')
  SOURCE_CACHE.set(relativePath, content)
  return content
}

function collectProductionSources(directory: string, prefix = ''): string[] {
  const files: string[] = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const relative = prefix ? `${prefix}/${entry.name}` : entry.name
    if (entry.isDirectory()) {
      files.push(...collectProductionSources(`${directory}/${entry.name}`, relative))
      continue
    }
    if (
      /\.(?:ts|tsx)$/.test(entry.name)
      && !/\.(?:test|spec)\.(?:ts|tsx)$/.test(entry.name)
    ) {
      files.push(relative)
    }
  }
  return files
}

const PRODUCTION_SOURCES = collectProductionSources(SRC_ROOT)

describe('服务端后端请求架构门禁', () => {
  it('所有既有服务端直连点只通过统一 helper 回源', () => {
    for (const relativePath of DIRECT_CALL_TARGETS) {
      const source = readSource(relativePath)
      expect(source, relativePath).toContain('serverBackendFetch')
      expect(source, relativePath).not.toMatch(/\bfetch\s*\(/)
      expect(source, relativePath).not.toMatch(
        /(?:process\.env|environment)\.(?:INTERNAL_API_URL|NEXT_PUBLIC_API_URL)/,
      )
    }
  })

  it('后端 origin 环境变量只有单一事实源可以读取', () => {
    const readers = PRODUCTION_SOURCES
      .filter(relativePath =>
        /(?:process\.env|environment)\.(?:INTERNAL_API_URL|NEXT_PUBLIC_API_URL)/
        .test(readSource(relativePath)))

    expect(readers).toEqual(['lib/backend-origin.ts'])
  })

  it('只有有界请求内核和流式 Route Handler 转发层可以直接解析后端 origin', () => {
    const users = PRODUCTION_SOURCES
      .filter(relativePath =>
        relativePath !== 'lib/backend-origin.ts'
        && /\bresolveBackendApiBase\b/.test(readSource(relativePath)))
      .sort()

    expect(users).toEqual([
      'app/api/backend-forwarding.ts',
      'app/api/proxy/proxy-forwarding.ts',
      'lib/server-backend-fetch.ts',
    ])
  })

  it('统一 helper 是 server-only，且流式 Route Handler 不反向依赖它', () => {
    expect(readSource('lib/backend-origin.ts'))
      .toMatch(/^import 'server-only'/)
    expect(readSource('lib/server-backend-fetch.ts'))
      .toMatch(/^import 'server-only'/)
    expect(readSource('app/api/backend-forwarding.ts'))
      .not.toContain('server-backend-fetch')
  })

  it('未声明客户端边界的生产模块只能在受审查的两处直接 fetch', () => {
    const serverSideDirectFetchers = PRODUCTION_SOURCES
      .filter((relativePath) => {
        const source = readSource(relativePath)
        return /\bfetch\s*\(/.test(source)
          && !/^['"]use client['"]/.test(source)
      })
      .sort()

    expect(serverSideDirectFetchers).toEqual([
      'app/api/proxy/[...path]/route.ts',
      'lib/server-backend-fetch.ts',
    ])
  })

  it('模拟登录的 cookie 会话切换只许从 /api/auth 进入', () => {
    const mutationEntries = PRODUCTION_SOURCES
      .filter((relativePath) => {
        if (relativePath === 'lib/session.ts') return false
        return /\b(?:start|stop)Impersonation\s*\(/.test(readSource(relativePath))
      })
      .sort()

    expect(mutationEntries).toEqual([
      'app/api/auth/start-impersonation/route.ts',
      'app/api/auth/stop-impersonation/route.ts',
    ])

    const leftoverActions = PRODUCTION_SOURCES.filter((relativePath) =>
      /\b(?:impersonateAction|stopImpersonationAction)\b/.test(readSource(relativePath)))
    expect(leftoverActions).toEqual([])
  })
})
