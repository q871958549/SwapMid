import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { loadI18nAuditRegistry } from './i18n-audit-registry.mjs'
import { auditDynamicKeyAllowlist } from './i18n-dynamic-key-audit.mjs'
import { isMaintainedTranslationKey, serializeJson } from './i18n-generation-core.mjs'
import { findIcuArgumentDrift, flattenMessages } from './i18n-message-contracts.mjs'
import {
  I18N_JSON_FILE_MAX_BYTES,
  readRegularFileBounded,
  writeFileAtomic,
} from './i18n-safe-files.mjs'
import { collectSourceFiles } from './scan-hardcoded-cn.mjs'
import {
  analyzeI18nReferencesInSource,
  createI18nStaticBatchBudget,
  inspectStaticPropertyValuesInSource,
} from './i18n-static-references.mjs'

const locales = ['zh-Hans', 'zh-Hant', 'en']
const workspaceRoot = process.env.I18N_WORKSPACE_ROOT
  ? resolve(process.env.I18N_WORKSPACE_ROOT)
  : fileURLToPath(new URL('..', import.meta.url))
const { dynamicKeyAllowlist, translatorBindingAllowlist } = await loadI18nAuditRegistry(workspaceRoot)

const fixedDynamicKeySourceContracts = Object.freeze([
  Object.freeze({
    sourceFile: 'components/layout/nav-config.ts',
    property: 'titleKey',
    namespace: 'Nav',
    selector: Object.freeze({
      file: 'lib/nav-utils.ts',
      functionName: 'translateNavTitle',
      variable: 't',
      method: 'plain',
      namespace: 'Nav',
      expression: 'item.titleKey',
    }),
  }),
  Object.freeze({
    sourceFile: 'components/layout/nav-config.ts',
    property: 'descriptionKey',
    namespace: 'Nav',
    selector: Object.freeze({
      file: 'lib/nav-utils.ts',
      functionName: 'translateNavDescription',
      variable: 't',
      method: 'plain',
      namespace: 'Nav',
      expression: 'domain.descriptionKey',
    }),
  }),
])

const sourceRoot = resolve(workspaceRoot, 'src')
const MAX_SOURCE_DIAGNOSTICS = 20_000
const MAX_LOG_ITEMS_PER_SECTION = 200

async function loadLocale(locale) {
  const file = resolve(workspaceRoot, `messages/${locale}.json`)
  return JSON.parse(
    (
      await readRegularFileBounded(file, {
        maxBytes: I18N_JSON_FILE_MAX_BYTES,
        label: `前端 ${locale} 词典`,
      })
    ).text,
  )
}

async function analyzeSourceReferences(knownKeys) {
  const missing = []
  const expandedDynamicReferences = []
  const unresolvedDynamicReferences = []
  const usedTranslatorBindingIds = new Set()
  const usedKeys = new Set()
  const inferredTranslatorReferences = []
  const fixedSourceContents = new Map()
  const batchBudget = createI18nStaticBatchBudget()
  let diagnostics = 0
  const files = collectSourceFiles(sourceRoot)
    // 负向回归测试会故意触发 MissingMessage；门禁只扫描会进入生产 bundle 的源码。
    .filter(file => !/\.(?:test|spec)\.(?:ts|tsx)$/.test(file.rel))
  for (const { rel, text: source } of files) {
    const relativeFile = rel.replaceAll('\\', '/')
    if (fixedDynamicKeySourceContracts.some(contract => contract.sourceFile === relativeFile)
      || /^components\/layout\/nav-groups\/[^/]+\.ts$/.test(relativeFile)) {
      fixedSourceContents.set(relativeFile, source)
    }
    const analysis = analyzeI18nReferencesInSource(
      source,
      knownKeys,
      relativeFile,
      translatorBindingAllowlist,
      batchBudget,
    )
    const fileDiagnostics = analysis.missing.length
      + analysis.expandedDynamicReferences.length
      + analysis.unresolvedDynamicReferences.length
      + analysis.inferredTranslatorReferences.length
    if (fileDiagnostics > MAX_SOURCE_DIAGNOSTICS - diagnostics) {
      throw new Error(`i18n 源码诊断对象总数超过上限 ${MAX_SOURCE_DIAGNOSTICS}`)
    }
    diagnostics += fileDiagnostics
    missing.push(...analysis.missing)
    expandedDynamicReferences.push(...analysis.expandedDynamicReferences)
    unresolvedDynamicReferences.push(...analysis.unresolvedDynamicReferences)
    for (const id of analysis.usedTranslatorBindingIds) usedTranslatorBindingIds.add(id)
    for (const key of analysis.usedKeys) usedKeys.add(key)
    inferredTranslatorReferences.push(...analysis.inferredTranslatorReferences)
  }
  return {
    missing,
    expandedDynamicReferences,
    unresolvedDynamicReferences,
    usedTranslatorBindingIds,
    usedKeys,
    inferredTranslatorReferences,
    fixedSourceContents,
  }
}

function logLimited(items, render, logger = console.error) {
  for (const item of items.slice(0, MAX_LOG_ITEMS_PER_SECTION)) logger(render(item))
  if (items.length > MAX_LOG_ITEMS_PER_SECTION) {
    logger(`  ... 另有 ${items.length - MAX_LOG_ITEMS_PER_SECTION} 条已省略（门禁仍失败）`)
  }
}

function formatLimited(items) {
  const shown = items.slice(0, MAX_LOG_ITEMS_PER_SECTION)
  return shown.join(', ')
    + (items.length > shown.length ? `, ... 另有 ${items.length - shown.length} 项已省略` : '')
}

function sameDynamicSelector(entry, selector) {
  return entry.file === selector.file
    && entry.functionName === selector.functionName
    && entry.variable === selector.variable
    && entry.method === selector.method
    && entry.namespace === selector.namespace
    && entry.expression === selector.expression
}

function auditFixedDynamicKeySources(sourceContents) {
  const errors = []
  for (const contract of fixedDynamicKeySourceContracts) {
    const sourceFiles = contract.property === 'titleKey' && contract.sourceFile === 'components/layout/nav-config.ts'
      ? [
          contract.sourceFile,
          ...[...sourceContents.keys()]
            .filter(file => /^components\/layout\/nav-groups\/[^/]+\.ts$/.test(file))
            .sort(),
        ]
      : [contract.sourceFile]
    if (sourceFiles.some(file => !sourceContents.has(file))) {
      errors.push(`缺少固定动态键来源文件：${contract.sourceFile}`)
      continue
    }
    const entries = dynamicKeyAllowlist.filter(entry => sameDynamicSelector(entry, contract.selector))
    if (entries.length !== 1) {
      errors.push(
        `固定动态键来源 ${contract.sourceFile}#${contract.property} 必须精确对应 1 条注册表契约，实际 ${entries.length} 条`,
      )
      continue
    }
    const inspected = sourceFiles.map(file => inspectStaticPropertyValuesInSource(
      sourceContents.get(file),
      contract.property,
      file,
    ))
    const nonStaticCount = inspected.reduce((sum, item) => sum + item.nonStaticCount, 0)
    const nonStaticLines = inspected.flatMap(item => item.nonStaticLines)
    if (nonStaticCount > 0) {
      errors.push(
        `${contract.sourceFile} 的 ${contract.property} 必须全部为静态字符串；`
          + `非静态行：${formatLimited(nonStaticLines)}`
          + (nonStaticCount > nonStaticLines.length
            ? `（另有 ${nonStaticCount - nonStaticLines.length} 行已省略）`
            : ''),
      )
    }
    const actual = inspected.flatMap(item => item.values).map(key => `${contract.namespace}.${key}`).sort()
    const expected = entries[0].fullKeys
    const actualSet = new Set(actual)
    const expectedSet = new Set(expected)
    const missingFromRegistry = actual.filter(key => !expectedSet.has(key))
    const staleInRegistry = expected.filter(key => !actualSet.has(key))
    if (missingFromRegistry.length > 0 || staleInRegistry.length > 0) {
      errors.push(
        `固定动态键来源 ${contract.sourceFile}#${contract.property} 与注册表不一致；`
        + `未登记 [${formatLimited(missingFromRegistry)}]；失效 [${formatLimited(staleInRegistry)}]`,
      )
    }
  }
  return errors
}

function auditTranslatorBindings(usedIds) {
  const errors = []
  const ids = new Set()
  for (const binding of translatorBindingAllowlist) {
    if (!binding.id || ids.has(binding.id)) errors.push(`translator 参数登记 id 重复或为空：${binding.id || '<empty>'}`)
    ids.add(binding.id)
    if (typeof binding.reason !== 'string' || binding.reason.trim().length < 10) {
      errors.push(`translator 参数登记理由过短：${binding.id}`)
    }
    if (!usedIds.has(binding.id)) errors.push(`失效的 translator 参数登记：${binding.id}`)
  }
  return errors
}

const auditInputMode = process.argv.includes('--validate-audit-input')
const pruneMode = process.argv.includes('--prune')
if (auditInputMode && pruneMode) throw new Error('--validate-audit-input 与 --prune 不得同时使用')

const localeCatalogs = new Map()
const localeMessages = new Map()
for (const locale of auditInputMode ? locales.slice(0, 1) : locales) {
  const catalog = await loadLocale(locale)
  localeCatalogs.set(locale, catalog)
  localeMessages.set(locale, flattenMessages(catalog))
}

const baselineLocale = locales[0]
const baselineMessages = localeMessages.get(baselineLocale)
const baseline = new Set(baselineMessages.keys())
let hasFailure = false

if (!pruneMode && !auditInputMode) {
  for (const locale of locales.slice(1)) {
    const current = new Set(localeMessages.get(locale).keys())
    const missing = [...baseline].filter(key => isMaintainedTranslationKey(key) && !current.has(key)).sort()
    const extra = [...current].filter(key => isMaintainedTranslationKey(key) && !baseline.has(key)).sort()

    if (missing.length === 0 && extra.length === 0) continue
    hasFailure = true
    console.error(`\n${locale} 与 ${baselineLocale} 的产品查询翻译键不一致：`)
    logLimited(missing, key => `  缺少  ${key}`)
    logLimited(extra, key => `  多出  ${key}`)
  }
}

if (!pruneMode && !auditInputMode) {
  const argumentDrift = findIcuArgumentDrift(localeMessages, baselineLocale)
    .filter(item => isMaintainedTranslationKey(item.key))
  if (argumentDrift.length > 0) {
    hasFailure = true
    console.error('\n产品查询三语 ICU 参数集合不一致：')
    logLimited(
      argumentDrift,
      item => `  ${item.key}  ${item.locale}=[${item.actual.join(', ')}] `
        + `${baselineLocale}=[${item.expected.join(', ')}]`,
    )
  }
}

const sourceReferences = await analyzeSourceReferences(baseline)
if (sourceReferences.missing.length > 0) {
  hasFailure = true
  console.error('\n代码引用了不存在的静态翻译键：')
  logLimited(sourceReferences.missing, (item) => {
    const kind = item.dynamic ? '动态展开' : '静态'
    return `  ${item.file}:${item.line}  [${kind}] ${item.variable}(${item.namespace}).${item.key}`
  })
}

for (const entry of dynamicKeyAllowlist) {
  for (const key of entry.fullKeys ?? []) sourceReferences.usedKeys.add(key)
}
const allowlistErrors = auditDynamicKeyAllowlist(
  sourceReferences.unresolvedDynamicReferences,
  dynamicKeyAllowlist,
  baseline,
)
allowlistErrors.push(...auditTranslatorBindings(sourceReferences.usedTranslatorBindingIds))
allowlistErrors.push(...auditFixedDynamicKeySources(sourceReferences.fixedSourceContents))
if (allowlistErrors.length > 0) {
  hasFailure = true
  console.error('\n动态翻译键审计失败：')
  logLimited(allowlistErrors, error => `  ${error}`)
}

if (hasFailure) process.exit(1)

if (auditInputMode) {
  console.log(
    `i18n PR 引用审计输入有效：${baseline.size} 个简中叶子键，`
    + `${dynamicKeyAllowlist.length} 条动态键契约，`
    + `${translatorBindingAllowlist.length} 条 translator 绑定。`,
  )
  process.exit(0)
}

const unused = [...baseline].filter(key => !sourceReferences.usedKeys.has(key)).sort()
if (pruneMode) {
  function deleteNestedKey(root, fullKey) {
    const parts = fullKey.split('.')
    const parents = []
    let current = root
    for (const part of parts.slice(0, -1)) {
      if (!current || typeof current !== 'object' || !(part in current)) return
      parents.push([current, part])
      current = current[part]
    }
    delete current[parts.at(-1)]
    for (const [parent, part] of parents.reverse()) {
      if (Object.keys(parent[part]).length > 0) break
      delete parent[part]
    }
  }

  const sourceCatalog = localeCatalogs.get(baselineLocale)
  for (const key of unused) deleteNestedKey(sourceCatalog, key)
  const sourcePath = resolve(workspaceRoot, `messages/${baselineLocale}.json`)
  const sourceContent = serializeJson(sourceCatalog, { label: `messages/${baselineLocale}.json` })
  writeFileAtomic(sourcePath, sourceContent)
  console.log(`[i18n] 已从简中事实源清理 ${unused.length} 个无引用键；请继续运行 pnpm i18n:sync。`)
  process.exit(0)
}

if (unused.length > 0) {
  hasFailure = true
  console.error(`\n词典含 ${unused.length} 个无代码引用的键；运行 pnpm i18n:prune 后再同步生成物：`)
  logLimited(unused, key => `  ${key}`)
}

if (process.argv.includes('--report-unused')) {
  console.log(`\n未被静态分析确认引用的翻译键：${unused.length}`)
  logLimited(unused, key => `  ${key}`, console.log)
}

if (hasFailure) process.exit(1)

console.log(
  `i18n 契约有效：${locales.join(' / ')}，共 ${baseline.size} 个叶子键；`
  + `动态键自动展开 ${sourceReferences.expandedDynamicReferences.length} 处，`
  + `helper namespace 自动推断 ${sourceReferences.inferredTranslatorReferences.length} 处，`
  + `显式审计豁免 ${dynamicKeyAllowlist.length} 处。`,
)
