// 界面健康度趋势文件的写入（docs/ui-governance-blueprint.md §3.6）。
//
// ui-health.mjs 负责量路由；这里只管趋势行里不来自路由扫描的三列，以及把一行并进 CSV：
//   swapmid_suppressions  swapmid/* 规则的豁免数 = eslint-suppressions.json 里的计数 + 源码行内 eslint-disable 注释
//   contract_routes   首屏 / 交互契约 e2e（e2e/*-contract.spec.ts）覆盖的去重路由数
//   rule_bytes        改一个典型业务页面时 .claude/rules/*.md 按路径注入的总字节数（UTF-8 去 CR）
//
// 每周由 .github/workflows/governance-metrics.yml 调 `node scripts/ui-health.mjs --trend --append`。

import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { measureMatchedRules } from './ui-rule-card.mjs'

const DISABLE_COMMENT = /eslint-disable(?:-next-line|-line)?[ \t]+([^\r\n]*)/g
const CONTRACT_SPEC = /-contract\.spec\.ts$/
const ROUTE_LITERAL = /\bpath:\s*['"`](\/[^'"`]*)['"`]/g

/** 一段源码里行内豁免掉的 swapmid/* 规则数；`-- 理由` 之后的文字不计。 */
export function countInlineSwapMidDisables(source) {
  let count = 0
  for (const [, rules] of source.matchAll(DISABLE_COMMENT)) {
    count += (rules.split(' -- ')[0].match(/\bswapmid\/[\w-]+/g) ?? []).length
  }
  return count
}

/** eslint-suppressions.json（已解析）里 swapmid/* 规则的豁免计数之和。 */
export function countBulkSwapMidSuppressions(suppressions) {
  let count = 0
  for (const rules of Object.values(suppressions)) {
    for (const [rule, entry] of Object.entries(rules)) if (rule.startsWith('swapmid/')) count += entry.count ?? 0
  }
  return count
}

/** 契约 spec 里写成字面量的路由，去掉查询串后去重计数。 */
export function countContractRoutes(specSources) {
  const routes = new Set()
  for (const source of specSources) {
    for (const [, path] of source.matchAll(ROUTE_LITERAL)) routes.add(path.split('?')[0])
  }
  return routes.size
}

/**
 * 把一行并进趋势 CSV 文本。表头多了列时旧行补空列；末行已是同一提交就不重复写。
 * 返回 { text, appended }。
 */
export function mergeTrend(existing, header, row) {
  const width = header.split(',').length
  const lines = existing.split(/\r?\n/).filter((line) => line.trim() !== '')
  const data = lines.slice(1).map((line) => {
    const cells = line.split(',')
    while (cells.length < width) cells.push('')
    return cells.join(',')
  })
  const commit = row.split(',')[1]
  const appended = !(commit && data.at(-1)?.split(',')[1] === commit)
  if (appended) data.push(row)
  return { text: `${[header, ...data].join('\n')}\n`, appended }
}

function walkSources(dir, acc = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name)
    if (entry.isDirectory()) walkSources(full, acc)
    else if (/\.(ts|tsx|mts|mjs)$/.test(entry.name)) acc.push(full)
  }
  return acc
}

/** 从 web 根目录读出趋势行的三列附加指标。 */
export function readTrendExtras(root) {
  const suppressionsFile = join(root, 'eslint-suppressions.json')
  const bulk = existsSync(suppressionsFile) ? countBulkSwapMidSuppressions(JSON.parse(readFileSync(suppressionsFile, 'utf8'))) : 0
  const inline = walkSources(join(root, 'src')).reduce((sum, file) => sum + countInlineSwapMidDisables(readFileSync(file, 'utf8')), 0)
  const e2e = join(root, 'e2e')
  const specs = existsSync(e2e) ? readdirSync(e2e).filter((name) => CONTRACT_SPEC.test(name)).map((name) => readFileSync(join(e2e, name), 'utf8')) : []
  const { totalBytes: ruleBytes } = measureMatchedRules(resolve(root, '..'))
  return { swapmidSuppressions: bulk + inline, contractRoutes: countContractRoutes(specs), ruleBytes }
}

/** 把一行写进趋势文件；返回是否真的多了一行。 */
export function appendTrendFile(file, header, row) {
  const { text, appended } = mergeTrend(existsSync(file) ? readFileSync(file, 'utf8') : '', header, row)
  writeFileSync(file, text)
  return appended
}
