import path from 'node:path'
import type { NextConfig } from 'next'
import createNextIntlPlugin from 'next-intl/plugin'

// 本地开发对接 ASP.NET Core 自签名 HTTPS 证书（仅非生产或显式 ALLOW_INSECURE_TLS=true 生效）。
if (process.env.NODE_ENV !== 'production' || process.env.ALLOW_INSECURE_TLS === 'true') {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'
}

const withNextIntl = createNextIntlPlugin('./src/i18n/request.ts')

const SECURITY_HEADERS = [
  { key: 'X-Frame-Options', value: 'DENY' },
  { key: 'X-Content-Type-Options', value: 'nosniff' },
  { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
  { key: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=()' },
]

// 不设置 deploymentId，避免构建期固化发布标识
const nextConfig: NextConfig = {
  output: 'standalone',
  outputFileTracingRoot: path.join(process.cwd()),
  serverExternalPackages: ['ioredis'],
  outputFileTracingIncludes: {
    '/*': [
      './scripts/start-production.mjs',
      './scripts/wait-for-api-ready.mjs',
      './scripts/retain-next-static.mjs',
      './src/lib/session-store-config-core.mjs',
      './src/lib/web-singleton-lease-core.mjs',
      './src/lib/web-singleton-lease-supervisor.mjs',
      './src/lib/web-singleton-child-watchdog.mjs',
    ],
  },
  allowedDevOrigins: ['127.0.0.1'],
  experimental: {
    turbopackFileSystemCacheForBuild:
      process.env.SWAPMID_CI_TURBOPACK_BUILD_CACHE === '1',
  },
  async headers() {
    return [
      {
        source: '/:path*',
        headers: SECURITY_HEADERS,
      },
    ]
  },
}

export default withNextIntl(nextConfig)
