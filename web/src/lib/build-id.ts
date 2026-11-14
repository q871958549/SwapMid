import 'server-only'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

// 构建标识：next build 产物 .next/BUILD_ID，同一镜像/构建内恒定，发布新版本必变。
// 用于客户端版本偏斜检测（旧页面发现服务端已是新构建 → 自动整页刷新）。
// dev（Turbopack dev 无该文件）回退 'dev'——值恒定，永不触发刷新。
let cached: string | null = null

export function getBuildId(): string {
  if (cached === null) {
    try {
      cached = readFileSync(join(process.cwd(), '.next', 'BUILD_ID'), 'utf8').trim() || 'dev'
    } catch {
      cached = 'dev'
    }
  }
  return cached
}
