// 接口产物 stale 校验：断言契约快照与前端类型 = 当前后端的最新状态。
//
// 由 `pnpm verify:api` 调用——它先 export:api（刷新快照）+ gen:api（刷新类型），
// 再跑本脚本：若工作树里这些产物出现未提交改动，说明你改了接口却忘了提交最新产物，
// 退出码 1。可用于本地提交前自检，将来也可挂到 CI。
import { execSync } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const PATHS = [
  'docs/api/openapi.generated.json',
  'web/src/lib/api/generated',
  'web/src/app/(dashboard)/docs/api-reference/api-doc-data.generated.ts',
]
// 路径含 (dashboard) 圆括号，统一加引号避免 shell 解析
const SPEC = PATHS.map((p) => `"${p}"`).join(' ')

try {
  execSync(`git diff --quiet -- ${SPEC}`, { cwd: ROOT, stdio: 'ignore' })
  console.log('[verify:api] 接口产物与提交一致 ✓')
} catch {
  console.error('\n[verify:api] 接口产物已过期——以下文件有未提交改动：')
  console.error(execSync(`git status --short -- ${SPEC}`, { cwd: ROOT }).toString())
  console.error('[verify:api] 请运行 `pnpm sync:api` 并提交最新产物。\n')
  process.exit(1)
}
