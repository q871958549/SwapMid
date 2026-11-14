import { defineConfig } from '@hey-api/openapi-ts'

// OpenAPI → TypeScript 类型生成（pnpm gen:api）。
//
// 输入**固定**为「契约快照」（docs/api/openapi.generated.json）——离线、确定性、
// 无自签证书问题；快照由 `pnpm sync:api`（推荐，自动起本仓 Host 安全导出）或
// `pnpm export:api` 刷新。
// ⚠ 特意不再读 OPENAPI_INPUT：该变量曾被 export 与本配置共用，带着它跑 gen:api
// 会直连 URL 生成、把固定 baseUrl 写进 types.gen.ts 污染产物（CI 契约检查必红，
// 两会话各踩一次，2026-07-27 分家）——生成步骤永远走快照，环境变量对它无效。
//
// 只启用 typescript 插件生成纯类型（types.gen.ts）：本项目请求路径固定为
// apiFetch（服务端，带 Bearer）/ fetch('/api/proxy/...')（客户端，经代理注入会话），
// 不使用生成的 SDK/client，避免与会话与反代机制冲突。
// 产物提交进仓库——构建（含 Docker）不依赖后端在线。
export default defineConfig({
  input: '../docs/api/openapi.generated.json',
  output: 'src/lib/api/generated',
  plugins: ['@hey-api/typescript'],
})
