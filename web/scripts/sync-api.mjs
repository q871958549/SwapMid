// 一键契约同步：安全导出契约快照 + 前端类型 + API 文档数据 + 离线门户。
//
//   pnpm sync:api                       # 全量：自动起本 worktree 的 Host → 导出 → 停 → 离线生成三件
//   pnpm sync:api --no-build            # 跳过 dotnet build（确认 bin 已是最新构建时提速）
//   pnpm verify:api                     # = sync:api --verify：不构建门户，最后断言产物无未提交 diff
//   OPENAPI_INPUT=<url> pnpm sync:api   # 外接模式：不起 Host，连指定后端（仍强制来源校验）
//
// 为什么要「自己起 Host」（2026-07-27 机制化）：本机多 worktree 并行，固定端口 44362 上
// 监听的常常是**别的会话**的 Host——「起 Host → 探测通 → 导出」的朴素流程在端口被先占时，
// 自己的 Host 绑定失败退出、探测打到别人进程上依然 200，契约事实源就此被**别人的契约**
// 污染（已实际/险些发生三次）。本脚本改为：在 OS 分配的随机空闲端口起**本仓刚构建**的
// Host、导出后立即停——从结构上消灭「连到别人」的窗口；导出前另按监听进程 PID 双重校验
// （见 export-openapi.mjs）。Host 启动失败会立即前台报错并附日志尾部，不再淹没在后台。
//
// 实现说明：
// - 起 Host 用 `dotnet exec <bin/.../Host.dll> --urls https://127.0.0.1:<随机端口>`，
//   不走 `dotnet run`：①无 launchSettings 参与，URL 覆盖链路简单可靠（Program.cs 是
//   CreateBuilder(args)，--urls 生效，另设 ASPNETCORE_URLS 双保险）；②子进程即 Host
//   本体，kill 即净，不会留孤儿进程占端口。cwd 设为 Host 项目目录（ContentRoot=项目目录，
//   读 appsettings(.Development).json 与 dotnet run 一致），ASPNETCORE_ENVIRONMENT=
//   Development（否则生产弱密钥守卫拒启）。
// - gen 链（gen:api / gen:api-doc / docs:api）一律从快照离线生成，并显式剔除
//   OPENAPI_INPUT——该变量只作用于「导出」一步。历史上 export 与 gen:api 共用它，
//   直连 URL 生成会把固定 baseUrl 写进 types.gen.ts 污染产物（两会话各踩一次），
//   现 openapi-ts.config.ts 已不读它，此处剔除是防回归的双保险。
import { spawn, spawnSync } from 'node:child_process'
import { readdirSync, existsSync, statSync } from 'node:fs'
import { resolve, dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer } from 'node:net'
import { exportOpenapi } from './export-openapi.mjs'

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0' // 本地自签 TLS，仅本进程（同 export-openapi.mjs）

const WEB = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const ROOT = resolve(WEB, '..')
const HOST_DIR = resolve(ROOT, 'src', 'SwapMid.HttpApi.Host')
const HOST_DLL_NAME = 'SwapMid.HttpApi.Host.dll'

const argv = process.argv.slice(2)
const VERIFY = argv.includes('--verify')
const NO_BUILD = argv.includes('--no-build')
const IN_MEMORY = argv.includes('--in-memory')
// 资源队列只运行本机计算；离线门户是派生物，可另在队列外按需下载/构建。
const SKIP_PORTAL = argv.includes('--skip-portal')

const READY_TIMEOUT_MS = 300_000
const POLL_INTERVAL_MS = 1_500
// 单次探测上限：/health/live 不跑任何依赖检查、毫秒级返回；卡满此值即视为本次失败并记录原因重试。
const PROBE_TIMEOUT_MS = 10_000

let hostProc = null
let hostSpawnError = null
const hostLogTail = []

function log(msg) {
  console.log(`[sync:api] ${msg}`)
}

function stopHost() {
  if (hostProc && hostProc.exitCode === null && !hostProc.killed) {
    hostProc.kill()
    log('已停止临时 Host。')
  }
  hostProc = null
}
process.on('exit', stopHost)
process.on('SIGINT', () => {
  stopHost()
  process.exit(130)
})

function collectTail(chunk) {
  for (const line of chunk.toString().split(/\r?\n/)) {
    if (!line.trim()) continue
    hostLogTail.push(line)
    if (hostLogTail.length > 120) hostLogTail.shift()
  }
}

function dumpTail() {
  if (hostLogTail.length === 0) return
  console.error('[sync:api] ── Host 日志尾部 ──')
  for (const line of hostLogTail.slice(-60)) console.error(`[host] ${line}`)
}

/** 统一失败出口：详情就地打印，抛带标记的错误交 main 收尾（finally 会停 Host）。 */
function fail(lines) {
  for (const l of lines) console.error(`[sync:api] ${l}`)
  const err = new Error(lines[0])
  err.alreadyReported = true
  throw err
}

/** 跑一条生成命令（离线步骤）：env 中剔除 OPENAPI_INPUT，见文件头。 */
function run(cmd) {
  log(`$ ${cmd}`)
  const env = { ...process.env }
  delete env.OPENAPI_INPUT
  const r = spawnSync(cmd, { shell: true, stdio: 'inherit', cwd: WEB, env })
  if (r.status !== 0) fail([`\`${cmd}\` 失败（退出码 ${r.status}）。`])
}

/** 由 OS 分配一个空闲端口（高位随机，天然避开 44362 这类共享默认端口）。 */
function getFreePort() {
  return new Promise((resolvePort, reject) => {
    const srv = createServer()
    srv.once('error', reject)
    srv.listen(0, '127.0.0.1', () => {
      const { port } = srv.address()
      srv.close(() => resolvePort(port))
    })
  })
}

function findHostDll() {
  const debugDir = join(HOST_DIR, 'bin', 'Debug')
  if (!existsSync(debugDir)) return null
  let best = null
  for (const tfm of readdirSync(debugDir)) {
    const candidate = join(debugDir, tfm, HOST_DLL_NAME)
    if (!existsSync(candidate)) continue
    const mtime = statSync(candidate).mtimeMs
    if (!best || mtime > best.mtime) best = { path: candidate, mtime }
  }
  return best?.path ?? null
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

/** fetch 异常的可读描述：undici 把真正原因（错误码 + 消息）放在 cause 里，外层只有 "fetch failed"。 */
function describeFetchError(e) {
  const cause = e?.cause
  const code = cause?.code ?? e?.name
  const detail = cause?.message ?? e?.message ?? String(e)
  return code ? `${code}: ${detail}` : detail
}

/** 连接被拒 = Kestrel 尚未监听，属启动期预期噪声；其余异常（超时/TLS/连接重置等）都要露出来。 */
const isConnRefused = (e) => e?.cause?.code === 'ECONNREFUSED'

// 就绪探测打 /health/live（不执行任何依赖检查、零响应体），不再直接拉 swagger：
// 2026-11-05 实测 1294 个接口时 swagger 单次生成 20s+（Swashbuckle 每个请求都重新生成、不缓存），
// 用它探活会把每轮都变成一次重型生成；此前 `catch {}` 又静默吞掉一切异常，客户端侧失败时只能
// 干等满 300s、拿不到任何原因。现在探活轻量化，swagger 只在导出时真正拉取一次。
async function waitUntilReady(probeUrl) {
  const started = Date.now()
  let badResponses = 0
  let lastNote = 0
  let lastError = null
  let lastReportedError = null
  for (;;) {
    if (hostSpawnError) {
      fail([`无法启动 dotnet 进程：${hostSpawnError.message}`])
    }
    if (hostProc.exitCode !== null) {
      dumpTail()
      const tailText = hostLogTail.join('\n')
      const extra = []
      if (/AddressBinder|already in use|EADDRINUSE/i.test(tailText)) {
        extra.push('随机空闲端口罕见撞车（挑选到绑定之间被抢）——直接重跑一次即可。')
      }
      if (/libs folder/i.test(tailText)) {
        extra.push('fresh worktree 缺 wwwroot/libs：在 Host 目录跑 `abp install-libs` 后重跑（勿在解决方案根跑）。')
      }
      fail([`Host 进程启动失败（退出码 ${hostProc.exitCode}），见上方日志尾部。`, ...extra])
    }
    let res = null
    try {
      res = await fetch(probeUrl, { signal: AbortSignal.timeout(PROBE_TIMEOUT_MS) })
    } catch (e) {
      if (!isConnRefused(e)) {
        lastError = describeFetchError(e)
        // 同一原因只打印一次，避免每 1.5s 刷屏；原因变化时再打印。
        if (lastError !== lastReportedError) {
          log(`就绪探测失败（非连接被拒，继续重试）：${lastError}`)
          lastReportedError = lastError
        }
      }
    }
    if (res) {
      if (res.status === 200) {
        await res.body?.cancel?.()
        log(`Host 就绪（耗时 ${Math.round((Date.now() - started) / 1000)}s），开始导出。`)
        return
      }
      const body = await res.text().catch(() => '')
      badResponses += 1
      if (badResponses >= 3) {
        dumpTail()
        const extra = []
        if (res.status === 500 && /libs/i.test(body)) {
          extra.push('疑似 fresh worktree 缺 wwwroot/libs：在 Host 目录跑 `abp install-libs` 后重跑（勿在解决方案根跑）。')
        }
        fail([`Host 已监听但就绪探测返回 ${res.status}（连续 ${badResponses} 次）。`, ...extra])
      }
    }
    if (Date.now() - started > READY_TIMEOUT_MS) {
      dumpTail()
      fail([
        `等待 Host 就绪超时（${READY_TIMEOUT_MS / 1000}s）。`,
        lastError ? `最后一次探测错误：${lastError}` : '期间探测始终是「连接被拒」——Host 一直没开始监听，见上方日志尾部。',
      ])
    }
    if (Date.now() - lastNote > 15_000) {
      log(`等待 Host 就绪…（已 ${Math.round((Date.now() - started) / 1000)}s）`)
      lastNote = Date.now()
    }
    await sleep(POLL_INTERVAL_MS)
  }
}

/** 自托管模式：构建并在随机空闲端口起本 worktree 的 Host，等到其开始处理 HTTP。 */
async function startLocalHost() {
  if (!NO_BUILD) {
    log('dotnet build Host（增量；确认已是最新构建时可加 --no-build 跳过）…')
    const r = spawnSync('dotnet', ['build', HOST_DIR, '--nologo'], { stdio: 'inherit' })
    if (r.status !== 0) {
      fail([
        'Host 构建失败。若报「文件正由另一进程使用」= 本 worktree 的 Host 正在运行、占着 bin：',
        '  ① 停掉它后重跑；或 ② 直接用它：OPENAPI_INPUT=<其 swagger url> pnpm sync:api（外接模式，会校验其归属）。',
      ])
    }
  }
  const dll = findHostDll()
  if (!dll) {
    fail([`找不到 ${HOST_DLL_NAME}（bin/Debug 下无构建产物）——去掉 --no-build 重跑。`])
  }
  const port = await getFreePort()
  const baseUrl = `https://127.0.0.1:${port}`
  hostProc = spawn('dotnet', ['exec', dll, '--urls', baseUrl], {
    cwd: HOST_DIR,
    env: { ...cleanedEnv(), ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: baseUrl },
    windowsHide: true,
  })
  hostProc.once('error', (e) => {
    hostSpawnError = e
  })
  hostProc.stdout.on('data', collectTail)
  hostProc.stderr.on('data', collectTail)
  log(`已在随机空闲端口起本 worktree 的 Host：${baseUrl}（PID ${hostProc.pid}）`)
  // 聚合文档只供契约快照/类型生成；Swagger UI 仍按 v1/v2 分开展示。
  const swaggerUrl = `${baseUrl}/swagger/all/swagger.json`
  await waitUntilReady(`${baseUrl}/health/live`)
  return { url: swaggerUrl, pid: hostProc.pid }
}

function cleanedEnv() {
  const env = { ...process.env }
  delete env.OPENAPI_INPUT
  return env
}

async function main() {
  try {
    run('node --test scripts/openapi-stable-order.test.mjs')
    if (IN_MEMORY) {
      log(`${VERIFY ? '校验' : '导出'}内存契约宿主快照（RuntimeSwaggerContractTests，免外部数据库）…`)
      const testProj = resolve(ROOT, 'test', 'SwapMid.HttpApi.Tests', 'SwapMid.HttpApi.Tests.csproj')
      const testArgs = ['test', testProj, '--nologo', '--filter', 'FullyQualifiedName~RuntimeSwaggerContractTests']
      if (NO_BUILD) testArgs.push('--no-build')
      const env = { ...cleanedEnv() }
      if (VERIFY) {
        delete env.SWAPMID_UPDATE_OPENAPI_SNAPSHOT
      } else {
        env.SWAPMID_UPDATE_OPENAPI_SNAPSHOT = '1'
      }
      const r = spawnSync('dotnet', testArgs, { stdio: 'inherit', cwd: ROOT, env })
      if (r.status !== 0) fail([`RuntimeSwaggerContractTests 失败（退出码 ${r.status}）。`])
    } else {
      let input = process.env.OPENAPI_INPUT
      let ownerPid
      if (input) {
        log(`外接模式：使用已运行的后端 ${input}（不自起 Host；将校验其进程归属）。`)
        log('注意：外接的 Host 必须已重启到最新代码，否则导出的是旧契约（stale 由 verify:api / CI 兜底）。')
      } else {
        const started = await startLocalHost()
        input = started.url
        ownerPid = started.pid
      }
      await exportOpenapi({ input, ownerPid })
      stopHost() // 导出即用完 Host；后续 gen 链全部从快照离线生成
    }
    run('pnpm gen:api')
    run('pnpm gen:api-doc')
    if (VERIFY) {
      run('node scripts/verify-api-clean.mjs')
      log('verify:api 通过：契约快照 / 类型 / 文档数据与提交一致。')
    } else {
      if (SKIP_PORTAL) {
        log('已跳过离线门户；需要时另运行 pnpm docs:api。')
      } else {
        run('pnpm docs:api')
      }
      log('sync:api 完成：契约快照 + 类型 + 文档数据已全部刷新。')
    }
  } catch (e) {
    if (!e?.alreadyReported) console.error(`[sync:api] ${e.message}`)
    process.exitCode = 1
  } finally {
    stopHost()
  }
}

main()
