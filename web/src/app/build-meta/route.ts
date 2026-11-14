import { NextResponse } from 'next/server'
import { getBuildId } from '@/lib/build-id'
import { readRuntimeReleaseTag } from '@/lib/runtime-release'

// 版本探针：返回当前服务端构建标识和运行时发布号，供 VersionWatcher 比对。
// 路径特意不放 /api/ 下——Caddy 把 /api/* 转给后端，前端 Route Handler 需逐个豁免；
// 顶级路径自然落到 web，无需维护 Caddyfile 清单（proxy.ts 已将其加入 PUBLIC_PATHS）。
// release 是 IMAGE_TAG 一类的发布号，不是密钥。
export const dynamic = 'force-dynamic'

export function GET() {
  return NextResponse.json(
    { buildId: getBuildId(), release: readRuntimeReleaseTag() },
    { headers: { 'cache-control': 'no-store' } }
  )
}
