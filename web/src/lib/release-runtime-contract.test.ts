import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

function read(path: string): string {
  return readFileSync(new URL(path, import.meta.url), 'utf8')
}

function stripComments(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/.*$/gm, '$1')
}

function serviceBlock(compose: string, name: string): string {
  const lines = compose.split(/\r?\n/)
  const start = lines.findIndex((line) => line === `  ${name}:`)
  expect(start, `${name} service must exist`).toBeGreaterThanOrEqual(0)
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

describe('发布标识运行时契约', () => {
  it('展示函数不读取构建期内联环境变量', () => {
    const source = stripComments(read('./release-label.ts'))
    expect(source).not.toContain('NEXT_PUBLIC_SWAPMID_RELEASE')
    expect(source).not.toContain('process.env')
  })

  it('服务端从 SWAPMID_RELEASE 读取，且不使用 NEXT_PUBLIC 前缀', () => {
    const source = read('./runtime-release.ts')
    const code = stripComments(source)
    expect(source).toContain("import 'server-only'")
    expect(code).toContain("RUNTIME_RELEASE_ENV = 'SWAPMID_RELEASE'")
    expect(code).toContain('env[RUNTIME_RELEASE_ENV]')
    expect(code).not.toContain('NEXT_PUBLIC_')
  })

  it('登录页与后台壳把运行时标识传给客户端', () => {
    const login = read('../app/(auth)/login/page.tsx')
    const loginClient = read('../app/(auth)/login/client.tsx')
    const layout = read('../app/(dashboard)/layout.tsx')
    const sidebar = read('../components/layout/sidebar.tsx')
    const drawer = read('../components/layout/mobile-nav-drawer.tsx')

    expect(login).toContain("dynamic = 'force-dynamic'")
    expect(login).toContain('readRuntimeReleaseTag()')
    expect(login).toContain('releaseTag={releaseTag}')
    expect(loginClient).toContain('getReleaseDisplay(releaseTag)')
    expect(loginClient).not.toContain('getReleaseDisplay()')
    expect(layout).toContain('readRuntimeReleaseTag()')
    expect(layout).toContain('releaseTag={releaseTag}')
    expect(sidebar).toContain('getReleaseDisplay(releaseTag)')
    expect(sidebar).not.toContain('NEXT_PUBLIC_SWAPMID_RELEASE')
    expect(drawer).toContain('releaseTag={releaseTag}')
  })

  it('web 镜像构建层不接收发布标识，运行阶段只带 standalone 产物', () => {
    const dockerfile = read('../../Dockerfile')
    const config = read('../../next.config.ts')
    const start = read('../../scripts/start-production.mjs')

    expect(dockerfile).not.toContain('NEXT_PUBLIC_SWAPMID_RELEASE')
    expect(dockerfile).toContain('COPY --from=build --chown=swapmid:swapmid /app/.next/standalone ./')
    expect(dockerfile).toContain('COPY --from=build --chown=swapmid:swapmid /app/.next/static ./.next/static')
    expect(dockerfile).toContain('COPY --from=build --chown=swapmid:swapmid /app/public ./public')
    expect(dockerfile).not.toContain('COPY --from=build /app ./')
    expect(dockerfile).toContain('CMD ["node", "scripts/start-production.mjs"]')
    expect(dockerfile).toContain('org.swapmid.session-refresh-protocol="2"')
    expect(dockerfile).toContain('org.opencontainers.image.revision="$RELEASE_SHA"')
    expect(dockerfile).toContain('apk add --no-cache tzdata')
    expect(dockerfile).toContain('TZ=UTC')
    expect(config).toMatch(/output:\s*'standalone'/)
    expect(config).toContain('不设置 deploymentId')
    expect(config).not.toMatch(/\bdeploymentId\s*:/)
    expect(config).toContain('./scripts/wait-for-api-ready.mjs')
    expect(config).toContain('./scripts/retain-next-static.mjs')
    expect(start).toContain('standaloneServerPath')
    expect(start).toContain('waitForApiReady')
    expect(start).toContain('retainNextStaticGenerations')
    expect(start).toContain("[nextCliPath, 'start', ...process.argv.slice(2)]")
    expect(start).toContain("HOSTNAME: '0.0.0.0'")
  })

  it('docker-compose 在容器环境注入 SWAPMID_RELEASE，而不是构建参数', () => {
    const service = serviceBlock(read('../../../docker/docker-compose.yml'), 'web')

    expect(service).not.toContain('NEXT_PUBLIC_SWAPMID_RELEASE')
    expect(service).toMatch(/SWAPMID_RELEASE:\s*"\$\{IMAGE_TAG:\?/)
    const buildArgs = service.slice(service.indexOf('args:'), service.indexOf('image:'))
    const environment = service.slice(service.indexOf('environment:'))
    expect(buildArgs).not.toContain('NEXT_PUBLIC_SWAPMID_RELEASE')
    expect(buildArgs).not.toMatch(/^\s*SWAPMID_RELEASE:/m)
    expect(environment).toMatch(/^\s*SWAPMID_RELEASE:/m)
    expect(environment).toContain('SWAPMID_API_READY_URL: "http://api:8080/health/ready"')
    expect(environment).toContain('SWAPMID_API_READY_FALLBACK_URL: "http://api:8080/health-status"')
    expect(environment).toContain('SWAPMID_API_READY_BUDGET_MS: "180000"')
    expect(environment).toContain('SWAPMID_NEXT_STATIC_ARCHIVE: /app/var/next-static-archive')
    expect(environment).toContain('SWAPMID_NEXT_STATIC_KEEP: "5"')
    expect(environment).toContain('NEXT_SERVER_ACTIONS_ENCRYPTION_KEY: ${NEXT_SERVER_ACTIONS_ENCRYPTION_KEY:-}')
    expect(service).toContain('web_next_static:/app/var/next-static-archive')
    expect(service).toMatch(/start_period:\s*220s/)
  })
})
