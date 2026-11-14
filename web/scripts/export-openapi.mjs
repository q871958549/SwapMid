// 导出运行中后端的 OpenAPI 契约快照到 docs/api/openapi.generated.json。
//
// 这是接口文档的「版本化事实源」：模块上线时随前端类型一起刷新并提交，
// 评审在 PR diff 里即可看到接口变化——取代手写、易滞后的 *.yaml（见 ADR-0002）。
//
// 用法：
//   pnpm sync:api                       # 推荐：自动起本 worktree 的 Host → 导出 → 停（sync-api.mjs）
//   pnpm export:api                     # 仅导出：连已在运行的后端（默认 https://localhost:44362）
//   OPENAPI_INPUT=<url> pnpm export:api # 连非默认地址的后端
//
// 契约来源防呆（2026-07-27 机制化；此前多会话并行已三次实际/险些误连他人 Host）：
// 本机多个 worktree 并行时，44362 上监听的可能是**别的会话**的 Host——HTTP 200 只证明
// 「端口上有进程」，不证明「是本仓的后端」。故导出前强制校验监听进程归属（fail-closed）：
//   - ownerPid 给定（sync:api 自托管模式）：监听 PID 必须 == 我们刚起的 Host 进程；
//   - 否则：监听进程 exe 路径必须位于本仓 src/SwapMid.HttpApi.Host/ 之下
//     （dotnet run 起的 Host 进程是 bin 下的 apphost exe，天然满足；
//      别的 worktree / 主仓 / codex 会话的 Host 路径不在本仓此目录下，一查即拒）；
//   - 查不到监听 / 查不到进程路径 / 路径不符：硬失败，并打印对方进程信息。
// 逃生舱（明确要连非本仓后端时才用）：OPENAPI_SKIP_OWNER_CHECK=1。
//
// 自签证书：本地后端用自签 TLS，仅本导出进程跳过校验（开发期约定，与生产无关，
// 同 web/src/instrumentation.ts）。
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import { stableOpenapiOrder } from './openapi-stable-order.mjs'

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const OUT = resolve(ROOT, 'docs/api/openapi.generated.json')
const HOST_PROJECT_DIR = resolve(ROOT, 'src', 'SwapMid.HttpApi.Host')
const DEFAULT_INPUT = 'https://localhost:44362/swagger/all/swagger.json'
// 拉取 swagger 的总时限（含读完响应体）。Swashbuckle 每次请求都重新生成文档，1000+ 接口实测 20s+；
// 留足余量，同时保证后端卡死时能带着原因失败，而不是无限期挂起。
const FETCH_TIMEOUT_MS = 180_000

/** undici 的 "fetch failed" 只是外壳，真正原因（错误码 + 消息）在 cause 里。 */
function describeFetchError(e) {
  const cause = e?.cause
  const code = cause?.code ?? e?.name
  const detail = cause?.message ?? e?.message ?? String(e)
  return code ? `${code}: ${detail}` : detail
}

/** 统一失败出口：详情就地打印，抛带标记的错误交调用方（CLI / sync-api.mjs）收尾。 */
function abort(lines) {
  for (const l of lines) console.error(l)
  const err = new Error(lines[0] ?? 'export:api 失败')
  err.alreadyReported = true
  throw err
}

/** 查询本机某端口的全部 LISTEN 进程（PID + exe 路径 + 启动时间）。查询本身失败返回 null。 */
function queryPortListeners(port) {
  const script =
    `$ErrorActionPreference='SilentlyContinue';` +
    `$pids=@(Get-NetTCPConnection -LocalPort ${port} -State Listen | ForEach-Object OwningProcess | Sort-Object -Unique);` +
    `$rows=@(foreach($p in $pids){$proc=Get-Process -Id $p;[pscustomobject]@{ProcessId=[int]$p;Path=$proc.Path;StartTime=if($proc -and $proc.StartTime){$proc.StartTime.ToString('yyyy-MM-dd HH:mm:ss')}else{$null}}});` +
    `ConvertTo-Json -InputObject $rows -Compress`
  const r = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8',
    windowsHide: true,
  })
  if (r.status !== 0 || !r.stdout?.trim()) return null
  try {
    const parsed = JSON.parse(r.stdout)
    return Array.isArray(parsed) ? parsed : [parsed]
  } catch {
    return null
  }
}

const normalizeDir = (p) => (p.replace(/\//g, '\\').replace(/\\+$/, '') + '\\').toLowerCase()

/** 契约来源防呆：断言 inputUrl 端口的监听进程确属本仓，防把别会话后端的契约写进事实源。 */
function assertListenerIsOurs(inputUrl, ownerPid) {
  if (process.env.OPENAPI_SKIP_OWNER_CHECK === '1') {
    console.warn('[export:api] ⚠ OPENAPI_SKIP_OWNER_CHECK=1：已跳过契约来源校验——确保你清楚目标后端是谁的。')
    return
  }
  let url
  try {
    url = new URL(inputUrl)
  } catch {
    abort([`[export:api] 输入不是合法 URL：${inputUrl}`])
  }
  if (!['localhost', '127.0.0.1', '::1', '[::1]'].includes(url.hostname)) {
    abort([
      `[export:api] ⛔ 目标 ${url.hostname} 不是本机回环地址，无法核实其进程归属（契约事实源只应来自本仓代码）。`,
      '[export:api] 确认要连外部后端导契约时，设 OPENAPI_SKIP_OWNER_CHECK=1 重跑。',
    ])
  }
  if (process.platform !== 'win32') {
    console.warn('[export:api] ⚠ 非 Windows 平台，跳过监听进程归属校验（本仓多会话并行形态仅存在于 Windows 开发机）。')
    return
  }
  const port = Number(url.port || (url.protocol === 'https:' ? 443 : 80))
  const listeners = queryPortListeners(port)
  if (listeners === null) {
    abort([
      `[export:api] ⛔ 无法查询端口 ${port} 的监听进程（PowerShell 查询失败）——为防误连别会话的后端，拒绝导出。`,
      '[export:api] 推荐改用 `pnpm sync:api`（自动起本仓 Host 并按 PID 校验）；或设 OPENAPI_SKIP_OWNER_CHECK=1 明确跳过。',
    ])
  }
  if (listeners.length === 0) {
    abort([
      `[export:api] 端口 ${port} 上没有任何进程在监听——后端没启动。`,
      '[export:api] 推荐直接 `pnpm sync:api`（自动起本 worktree 的 Host、导出后自停，无需手动起后端）。',
    ])
  }
  const describe = (l) =>
    `[export:api]   监听进程 PID ${l.ProcessId}：${l.Path ?? '（路径不可读）'}${l.StartTime ? `（启动于 ${l.StartTime}）` : ''}`
  if (ownerPid != null) {
    const foreign = listeners.filter((l) => l.ProcessId !== ownerPid)
    if (foreign.length > 0) {
      abort([
        `[export:api] ⛔ 拒绝导出：端口 ${port} 的监听进程不是刚启动的本仓 Host（PID ${ownerPid}）——你连到的可能是别的会话的后端！`,
        ...foreign.map(describe),
        '[export:api] 重跑 `pnpm sync:api` 即可（随机空闲端口撞车概率极低）。',
      ])
    }
    return
  }
  const expected = normalizeDir(HOST_PROJECT_DIR)
  const foreign = listeners.filter((l) => !l.Path || !normalizeDir(l.Path).startsWith(expected))
  if (foreign.length > 0) {
    abort([
      `[export:api] ⛔ 拒绝导出：端口 ${port} 的监听进程不是本仓的后端——你连到的可能是别的会话（其它 worktree / 主仓 / codex）的 Host！`,
      ...foreign.map(describe),
      `[export:api]   期望进程路径位于：${HOST_PROJECT_DIR}\\`,
      '[export:api] 处置：推荐直接 `pnpm sync:api`（自动在随机空闲端口起本仓 Host、导出后自停，不受 44362 被谁占影响）；',
      '[export:api]       或让本仓 Host 起在空闲端口后 `OPENAPI_INPUT=<其 swagger url>` 重试。',
      '[export:api] （确认就是要连非本仓后端时，设 OPENAPI_SKIP_OWNER_CHECK=1 跳过本校验。）',
    ])
  }
}

/** 导出契约快照。input / ownerPid 供 sync-api.mjs 编程调用；CLI 走环境变量与默认值。 */
export async function exportOpenapi({ input, ownerPid } = {}) {
  const INPUT = input ?? process.env.OPENAPI_INPUT ?? DEFAULT_INPUT
  assertListenerIsOurs(INPUT, ownerPid)
  const signal = AbortSignal.timeout(FETCH_TIMEOUT_MS)
  let res
  try {
    res = await fetch(INPUT, { signal })
  } catch (e) {
    abort([
      `[export:api] 无法从后端取得 swagger：${INPUT}`,
      '[export:api] 推荐直接 `pnpm sync:api`（自动起本 worktree 的 Host，无需手动启动后端）。',
      `[export:api] 原始错误：${describeFetchError(e)}`,
    ])
  }
  if (!res.ok) {
    const body = await res.text().catch(() => '')
    const lines = [`[export:api] 后端返回 ${res.status} ${res.statusText}`]
    if (res.status === 500 && /libs/i.test(body)) {
      lines.push('[export:api] 疑似 fresh worktree 缺 wwwroot/libs：在 Host 目录跑 `abp install-libs`（勿在解决方案根跑）后重启 Host。')
    }
    abort(lines)
  }
  let spec
  try {
    spec = await res.json() // 同一 signal 覆盖响应体读取：大文档传输中断/超时也带原因失败
  } catch (e) {
    abort([`[export:api] 读取 swagger 响应体失败：${describeFetchError(e)}`])
  }
  let previous
  try {
    previous = JSON.parse(await readFile(OUT, 'utf8'))
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
  }
  // 稳定对象成员顺序及文档换行；业务字段值与数组顺序仍以运行时 Swagger 为准。
  await mkdir(dirname(OUT), { recursive: true })
  await writeFile(OUT, JSON.stringify(stableOpenapiOrder(spec, previous), null, 2) + '\n', 'utf8')
  const pathCount = Object.keys(spec.paths ?? {}).length
  console.log(`[export:api] 已导出 ${pathCount} 个接口路径 → docs/api/openapi.generated.json（来源：${INPUT}）`)
}

// CLI 入口（被 sync-api.mjs import 时不执行）
const invokedAs = process.argv[1] ? resolve(process.argv[1]).toLowerCase() : ''
if (invokedAs === fileURLToPath(import.meta.url).toLowerCase()) {
  exportOpenapi().catch((e) => {
    if (!e?.alreadyReported) console.error(`[export:api] ${e.message}`)
    process.exit(1)
  })
}
