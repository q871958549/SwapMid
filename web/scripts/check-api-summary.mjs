// 接口标题（XML <summary>）覆盖率棘轮门禁。
//
// 为什么存在：「开发者文档 · API 参考」页的接口标题来自后端方法的 XML 注释。缺失时
// gen-api-doc.mjs 会按 HTTP 方法 + 路径兜底成「查询列表 / 新建 / 更新」这类零信息文案——
// 页面看起来是正常的，缺口却既无从察觉、也无从度量（2026-08-19 实测：827 个 /api/v1
// 接口里 436 个是兜底标题，136 个 controller 里 67 个一条注释都没有）。
//
// 本脚本把缺口固化成基线，规则与 i18n 的 hardcoded-cn-baseline 一致——只允许下降：
//   1. 任何 controller 的缺失数高于基线      → 失败
//   2. 基线里没有的新 controller 必须零缺失  → 失败（新接口必须写 summary）
//   3. 优于基线                              → 通过，并提示收紧基线
//
// 用法：
//   pnpm check:api-summary        # 门禁（CI 与本地提交前）
//   pnpm api-summary:baseline     # 补完注释后收紧基线
//
// 输入：docs/api/openapi.generated.json（契约事实源见 ADR-0002）
// 基线：docs/api/summary-coverage-baseline.json
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..') // web/
const SPEC = resolve(ROOT, '../docs/api/openapi.generated.json')
const BASELINE = resolve(ROOT, '../docs/api/summary-coverage-baseline.json')

// 与 gen-api-doc.mjs 的收录面保持一致：只有 /api/v1 业务接口会上 API 参考页。
const BUSINESS_PATH = /^\/api\/v[1-9]\d*\//
const METHODS = new Set(['get', 'post', 'put', 'delete', 'patch'])
const UNCATEGORIZED = 'Uncategorized'

/** 统计每个 controller(tag) 缺 summary 的接口数。返回 { total, missing, byTag }。 */
export function collectMissingSummaries(spec) {
  let total = 0
  let missing = 0
  const byTag = {}
  const totalByTag = {}

  for (const [path, operations] of Object.entries(spec?.paths ?? {})) {
    if (!BUSINESS_PATH.test(path)) continue
    for (const [method, op] of Object.entries(operations ?? {})) {
      if (!METHODS.has(method.toLowerCase())) continue
      const tag = op?.tags?.[0] ?? UNCATEGORIZED
      total += 1
      totalByTag[tag] = (totalByTag[tag] ?? 0) + 1
      if (typeof op?.summary === 'string' && op.summary.trim().length > 0) continue
      missing += 1
      byTag[tag] = (byTag[tag] ?? 0) + 1
    }
  }

  return { total, missing, byTag, totalByTag }
}

/** 棘轮比对。返回 { regressions, newTags, improvements }，前两者非空即应失败。 */
export function compareWithBaseline(current, baseline) {
  const regressions = []
  const newTags = []
  const improvements = []
  const baseByTag = baseline?.byTag ?? {}

  for (const [tag, count] of Object.entries(current.byTag)) {
    if (!(tag in baseByTag)) {
      newTags.push({ tag, count })
      continue
    }
    if (count > baseByTag[tag]) {
      regressions.push({ tag, count, baseline: baseByTag[tag] })
    }
  }

  for (const [tag, count] of Object.entries(baseByTag)) {
    const now = current.byTag[tag] ?? 0
    if (now < count) improvements.push({ tag, count: now, baseline: count })
  }

  return { regressions, newTags, improvements }
}

function buildBaseline(current) {
  return {
    version: 1,
    说明: '每个 controller 缺少 XML <summary> 的 /api/v1 接口数。只允许下降；新 controller 必须为 0。',
    total: current.total,
    missing: current.missing,
    byTag: Object.fromEntries(
      Object.entries(current.byTag).sort(([left], [right]) => left.localeCompare(right)),
    ),
  }
}

function main() {
  if (!existsSync(SPEC)) {
    console.error('[api-summary] 缺少 docs/api/openapi.generated.json，请先运行 `pnpm export:api`。')
    process.exitCode = 1
    return
  }

  const spec = JSON.parse(readFileSync(SPEC, 'utf8'))
  const current = collectMissingSummaries(spec)
  const documented = current.total - current.missing
  const rate = current.total > 0 ? ((100 * documented) / current.total).toFixed(1) : '0.0'

  if (process.argv.includes('--write-baseline')) {
    writeFileSync(BASELINE, `${JSON.stringify(buildBaseline(current), null, 2)}\n`, 'utf8')
    console.log(`[api-summary] 已刷新基线：${current.missing} 个接口缺标题（覆盖率 ${rate}%）。`)
    return
  }

  if (!existsSync(BASELINE)) {
    console.error('[api-summary] 缺少 docs/api/summary-coverage-baseline.json，请运行 `pnpm api-summary:baseline`。')
    process.exitCode = 1
    return
  }

  const baseline = JSON.parse(readFileSync(BASELINE, 'utf8'))
  if (baseline.version !== 1 || typeof baseline.byTag !== 'object' || baseline.byTag === null) {
    console.error('[api-summary] 基线格式无效。')
    process.exitCode = 1
    return
  }

  const { regressions, newTags, improvements } = compareWithBaseline(current, baseline)

  if (newTags.length > 0) {
    console.error('\n[api-summary] 以下 controller 是新增的，接口必须写中文 XML <summary>：')
    for (const item of newTags) {
      console.error(`  ${item.tag}: ${item.count} 个接口缺标题`)
    }
  }
  if (regressions.length > 0) {
    console.error('\n[api-summary] 以下 controller 的缺失数高于基线（新接口没写 summary）：')
    for (const item of regressions) {
      console.error(`  ${item.tag}: ${item.count} 个（基线 ${item.baseline}）`)
    }
  }
  if (newTags.length > 0 || regressions.length > 0) {
    console.error(
      '\n[api-summary] 请给 AppService / Controller 方法补 `/// <summary>`（会进 Swagger，'
      + '也是 API 参考页的接口标题）。补完后跑 `pnpm api-summary:baseline` 收紧基线。\n',
    )
    process.exitCode = 1
    return
  }

  console.log(
    `[api-summary] 接口标题覆盖率 ${rate}%（${documented}/${current.total}）；`
    + `缺失 ${current.missing} 个，基线 ${baseline.missing} 个。`,
  )
  if (improvements.length > 0) {
    const fixed = improvements.reduce((sum, item) => sum + (item.baseline - item.count), 0)
    console.log(
      `[api-summary] 有 ${improvements.length} 个 controller 共补了 ${fixed} 个标题，`
      + '请运行 `pnpm api-summary:baseline` 收紧基线，防止回退。',
    )
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main()
