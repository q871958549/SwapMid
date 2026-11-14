import { createHash } from 'node:crypto'
import {
  existsSync,
  lstatSync,
  opendirSync,
  readdirSync,
  realpathSync,
} from 'node:fs'
import { dirname, isAbsolute, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { serializeJson } from './i18n-generation-core.mjs'
import { readRegularFileBoundedSync, writeFileAtomic } from './i18n-safe-files.mjs'

const WEB_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const REPOSITORY_ROOT = process.env.I18N_REPOSITORY_ROOT
  ? resolve(process.env.I18N_REPOSITORY_ROOT)
  : resolve(WEB_ROOT, '..')
const DOMAIN_SHARED = resolve(REPOSITORY_ROOT, 'src/SwapMid.Domain.Shared')
const RESOURCE_ROOT = resolve(DOMAIN_SHARED, 'Localization/SwapMid')
const MODULE = resolve(DOMAIN_SHARED, 'SwapMidDomainSharedModule.cs')
const BASELINE = resolve(DOMAIN_SHARED, 'I18n/user-friendly-exception-baseline.json')
const SUPPORTED_LOCALES = ['zh-Hans', 'zh-Hant', 'en']
const SOURCE_SCAN_LIMITS = Object.freeze({
  maxFiles: 5_000,
  maxDirectories: 5_000,
  maxDirectoryEntries: 20_000,
  maxFileBytes: 4 * 1024 * 1024,
  maxTotalBytes: 32 * 1024 * 1024,
})
const MAX_RESOURCE_DIRECTORY_ENTRIES = 16
const MAX_USER_FRIENDLY_EXCEPTIONS = 5_000
const MAX_RESOURCE_KEYS = 5_000
const MAX_RESOURCE_VALUE_CHARS = 20_000
const MAX_RESOURCE_TOTAL_VALUE_CHARS = 1_000_000
const MAX_ERROR_CODE_DECLARATIONS = 5_000
const MAX_LANGUAGE_DECLARATIONS = 16

function readDomainErrorCodeSources(domainShared, { maxBytes }) {
  let entries
  try {
    entries = readdirSync(domainShared, { withFileTypes: true })
  } catch (error) {
    throw new Error(`无法枚举领域错误码目录：${error.message}`)
  }
  const names = entries
    .filter((entry) => entry.isFile()
      && /^SwapMidDomainErrorCodes.*\.cs$/.test(entry.name))
    .map((entry) => entry.name)
    .sort()
  if (names.length === 0) {
    throw new Error('缺少 SwapMidDomainErrorCodes*.cs')
  }
  return names.map((name) => readRegularFileBoundedSync(resolve(domainShared, name), {
    maxBytes,
    label: `后端领域错误码常量文件 ${name}`,
  }).text).join('\n')
}

function hash(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex').slice(0, 20)
}

function assertInside(root, path, label) {
  const pathFromRoot = relative(root, path)
  if (pathFromRoot === '' || (!pathFromRoot.startsWith('..') && !isAbsolute(pathFromRoot))) {
    return
  }
  throw new Error(`${label} 越出后端源码根目录：${path}`)
}

/**
 * 枚举后端 C# 源码时拒绝符号链接和特殊文件，并在读文件前施加资源预算。
 * pull_request_target 会用受信脚本读取不受信 PR 数据，因此不能依赖普通递归 API 的隐式跟随行为。
 */
function checkedSourceScanLimits(overrides = {}) {
  const limits = { ...SOURCE_SCAN_LIMITS, ...overrides }
  for (const [name, value] of Object.entries(limits)) {
    if (!Number.isSafeInteger(value) || value <= 0) {
      throw new Error(`后端源码扫描预算 ${name} 必须是正安全整数`)
    }
  }
  return limits
}

export function collectCSharpSourceFiles(sourceRoot, overrides = {}) {
  const limits = checkedSourceScanLimits(overrides)
  const root = resolve(sourceRoot)
  const rootStat = lstatSync(root)
  if (rootStat.isSymbolicLink() || !rootStat.isDirectory()) {
    throw new Error(`后端源码根目录必须是普通目录且不得为符号链接：${root}`)
  }

  const realRoot = realpathSync(root)
  const files = []
  const budget = { files: 0, directories: 1, entries: 0, totalBytes: 0 }

  const directories = [root]
  while (directories.length > 0) {
    const directory = directories.pop()
    const directoryStat = lstatSync(directory)
    if (directoryStat.isSymbolicLink() || !directoryStat.isDirectory()) {
      throw new Error(`后端源码目录必须是普通目录且不得为符号链接：${directory}`)
    }
    const realDirectory = realpathSync(directory)
    assertInside(realRoot, realDirectory, '后端源码目录')
    const handle = opendirSync(realDirectory)
    try {
      let entry
      while ((entry = handle.readSync()) !== null) {
        budget.entries += 1
        if (budget.entries > limits.maxDirectoryEntries) {
          throw new Error(`后端源码目录项超过上限 ${limits.maxDirectoryEntries}`)
        }
        if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'Migrations') continue

        const path = resolve(directory, entry.name)
        const stat = lstatSync(path)
        if (entry.isSymbolicLink() || stat.isSymbolicLink()) {
          throw new Error(`后端源码树不得包含符号链接：${relative(realRoot, path)}`)
        }
        if (entry.isDirectory() && stat.isDirectory()) {
          budget.directories += 1
          if (budget.directories > limits.maxDirectories) {
            throw new Error(`后端源码目录数超过上限 ${limits.maxDirectories}`)
          }
          directories.push(path)
          continue
        }
        if (!entry.isFile() || !stat.isFile()) {
          throw new Error(`后端源码树不得包含特殊文件：${relative(realRoot, path)}`)
        }
        if (!entry.name.endsWith('.cs')) continue

        budget.files += 1
        budget.totalBytes += stat.size
        if (budget.files > limits.maxFiles) {
          throw new Error(`后端 C# 源文件数超过上限 ${limits.maxFiles}`)
        }
        if (stat.size > limits.maxFileBytes) {
          throw new Error(`后端 C# 源文件超过单文件上限 ${limits.maxFileBytes} 字节：${relative(realRoot, path)}`)
        }
        if (budget.totalBytes > limits.maxTotalBytes) {
          throw new Error(`后端 C# 源文件总量超过上限 ${limits.maxTotalBytes} 字节`)
        }
        files.push(path)
      }
    } finally {
      handle.closeSync()
    }
  }
  return files.sort((left, right) => left.localeCompare(right))
}

export function readCSharpSource(path, budget, overrides = {}) {
  const limits = checkedSourceScanLimits(overrides)
  const remaining = limits.maxTotalBytes - budget.totalBytes
  if (remaining < 0) {
    throw new Error(`后端 C# 源文件实际读取总量超过上限 ${limits.maxTotalBytes} 字节`)
  }
  const maxBytes = Math.min(limits.maxFileBytes, remaining)
  const label = `后端 C# 源文件 ${path}`
  let result
  try {
    result = readRegularFileBoundedSync(path, { maxBytes, label })
  } catch (error) {
    if (maxBytes < limits.maxFileBytes && error.message === `${label}不得超过 ${maxBytes} 字节`) {
      throw new Error(`后端 C# 源文件实际读取总量超过上限 ${limits.maxTotalBytes} 字节`)
    }
    throw error
  }
  budget.totalBytes += result.bytes
  return result.text
}

function normalized(expression) {
  return expression.replace(/\s+/g, ' ').trim()
}

/**
 * 提取 UserFriendlyException 的首层构造参数文本。解析括号、字符串与注释，
 * 避免 string.Join(...) 或插值表达式中的右括号截断。
 */
export function extractUserFriendlyExceptionArguments(
  source,
  maxResults = MAX_USER_FRIENDLY_EXCEPTIONS,
) {
  if (!Number.isSafeInteger(maxResults) || maxResults < 0) {
    throw new Error('UserFriendlyException 提取额度必须是非负安全整数')
  }
  const marker = 'new UserFriendlyException'
  const result = []
  let searchFrom = 0

  while (true) {
    const markerIndex = source.indexOf(marker, searchFrom)
    if (markerIndex < 0) break
    const open = source.indexOf('(', markerIndex + marker.length)
    if (open < 0) break

    let depth = 1
    let quote = null
    let verbatim = false
    let lineComment = false
    let blockComment = false
    let index = open + 1
    for (; index < source.length && depth > 0; index += 1) {
      const char = source[index]
      const next = source[index + 1]

      if (lineComment) {
        if (char === '\n') lineComment = false
        continue
      }
      if (blockComment) {
        if (char === '*' && next === '/') {
          blockComment = false
          index += 1
        }
        continue
      }
      if (quote) {
        if (verbatim && char === '"' && next === '"') {
          index += 1
          continue
        }
        if ((!verbatim && char === '\\') || (quote === "'" && char === '\\')) {
          index += 1
          continue
        }
        if (char === quote) {
          quote = null
          verbatim = false
        }
        continue
      }
      if (char === '/' && next === '/') {
        lineComment = true
        index += 1
      } else if (char === '/' && next === '*') {
        blockComment = true
        index += 1
      } else if (char === '"' || char === "'") {
        quote = char
        verbatim = char === '"' && (source[index - 1] === '@' || source[index - 2] === '@')
      } else if (char === '(') {
        depth += 1
      } else if (char === ')') {
        depth -= 1
      }
    }

    if (depth === 0) {
      if (result.length >= maxResults) {
        throw new Error(`UserFriendlyException 总数超过上限 ${MAX_USER_FRIENDLY_EXCEPTIONS}`)
      }
      result.push(normalized(source.slice(open + 1, index - 1)))
    }
    searchFrom = Math.max(index, open + 1)
  }
  return result
}

function readResource(locale) {
  const path = resolve(RESOURCE_ROOT, `${locale}.json`)
  const resource = JSON.parse(
    readRegularFileBoundedSync(path, {
      maxBytes: SOURCE_SCAN_LIMITS.maxFileBytes,
      label: `后端本地化资源 ${locale}.json`,
    }).text,
  )
  if (resource === null || Array.isArray(resource) || typeof resource !== 'object') {
    throw new Error(`${locale}.json 根节点必须是对象`)
  }
  const texts = resource.texts ?? resource.Texts
  if (
    resource.culture !== locale
    || !texts
    || typeof texts !== 'object'
    || Array.isArray(texts)
  ) {
    throw new Error(`${locale}.json 必须使用小写 culture/texts，且 culture 必须等于 ${locale}`)
  }
  let keyCount = 0
  let totalValueChars = 0
  for (const key in texts) {
    if (!Object.hasOwn(texts, key)) continue
    keyCount += 1
    if (keyCount > MAX_RESOURCE_KEYS) {
      throw new Error(`${locale}.json 本地化资源键不得超过 ${MAX_RESOURCE_KEYS}`)
    }
    const value = texts[key]
    if (typeof value !== 'string') {
      throw new Error(`${locale}.json 本地化资源值必须是字符串：${key}`)
    }
    if (value.length > MAX_RESOURCE_VALUE_CHARS) {
      throw new Error(`${locale}.json 单条本地化资源不得超过 ${MAX_RESOURCE_VALUE_CHARS} 字符：${key}`)
    }
    if (value.length > MAX_RESOURCE_TOTAL_VALUE_CHARS - totalValueChars) {
      throw new Error(`${locale}.json 本地化资源文本总量不得超过 ${MAX_RESOURCE_TOTAL_VALUE_CHARS} 字符`)
    }
    totalValueChars += value.length
  }
  return texts
}

function checkSourceResourceContract() {
  const actualFiles = []
  const handle = opendirSync(RESOURCE_ROOT)
  try {
    let entry
    while ((entry = handle.readSync()) !== null) {
      if (actualFiles.length >= MAX_RESOURCE_DIRECTORY_ENTRIES) {
        throw new Error(`后端本地化资源目录项超过上限 ${MAX_RESOURCE_DIRECTORY_ENTRIES}`)
      }
      if (entry.isSymbolicLink() || !entry.isFile()) {
        throw new Error(`后端本地化资源目录只允许普通 JSON 文件：${entry.name}`)
      }
      actualFiles.push(entry.name)
    }
  } finally {
    handle.closeSync()
  }
  actualFiles.sort()
  const expectedFiles = SUPPORTED_LOCALES.map((locale) => `${locale}.json`).sort()
  if (actualFiles.join('\0') !== expectedFiles.join('\0')) {
    throw new Error(`后端本地化资源只允许 ${expectedFiles.join(', ')}；实际为 ${actualFiles.join(', ')}`)
  }

  const sourceKeys = Object.keys(readResource('zh-Hans')).sort()

  const constantsSource = readDomainErrorCodeSources(DOMAIN_SHARED, {
    maxBytes: SOURCE_SCAN_LIMITS.maxFileBytes,
  })
  const codes = new Set()
  const errorCodePattern = /public\s+const\s+string\s+\w+\s*=\s*"(SwapMid:[^"]+)"/g
  let errorCodeMatch
  let errorCodeDeclarations = 0
  while ((errorCodeMatch = errorCodePattern.exec(constantsSource)) !== null) {
    errorCodeDeclarations += 1
    if (errorCodeDeclarations > MAX_ERROR_CODE_DECLARATIONS) {
      throw new Error(`后端领域错误码声明不得超过 ${MAX_ERROR_CODE_DECLARATIONS}`)
    }
    codes.add(errorCodeMatch[1])
  }
  const localizedCodes = new Set(sourceKeys.filter((key) => key.startsWith('SwapMid:')))
  const missing = [...codes].filter((code) => !localizedCodes.has(code)).sort()
  const orphaned = [...localizedCodes].filter((code) => !codes.has(code)).sort()
  if (missing.length > 0 || orphaned.length > 0) {
    const formatCodes = (items) => {
      const shown = items.slice(0, 200)
      return shown.join(', ')
        + (items.length > shown.length ? `, ... 另有 ${items.length - shown.length} 项已省略` : '')
    }
    throw new Error(
      `领域错误码本地化不完整；缺少 [${formatCodes(missing)}]；`
        + `无常量来源 [${formatCodes(orphaned)}]`,
    )
  }

  const moduleSource = readRegularFileBoundedSync(MODULE, {
    maxBytes: SOURCE_SCAN_LIMITS.maxFileBytes,
    label: '后端本地化模块声明文件',
  }).text
  const languages = []
  const languagePattern = /new\s+LanguageInfo\("([^"]+)"/g
  let languageMatch
  while ((languageMatch = languagePattern.exec(moduleSource)) !== null) {
    if (languages.length >= MAX_LANGUAGE_DECLARATIONS) {
      throw new Error(`后端语言声明不得超过 ${MAX_LANGUAGE_DECLARATIONS}`)
    }
    languages.push(languageMatch[1])
  }
  if (languages.join('\0') !== SUPPORTED_LOCALES.join('\0')) {
    throw new Error(`后端声明语言必须且只能为 ${SUPPORTED_LOCALES.join(' / ')}`)
  }
  if (!moduleSource.includes('.Add<SwapMidResource>("zh-Hans")')) {
    throw new Error('后端默认资源文化必须为 zh-Hans')
  }

  return { sourceKeys, errorCodeCount: codes.size }
}

function checkAuxiliaryResources() {
  // 平台后台默认以简中为事实源；繁中/英文文件结构与读取边界仍须有效。
  for (const locale of SUPPORTED_LOCALES.slice(1)) {
    readResource(locale)
  }
}

function checkResources() {
  const sourceContract = checkSourceResourceContract()
  checkAuxiliaryResources()
  return {
    keyCount: sourceContract.sourceKeys.length,
    errorCodeCount: sourceContract.errorCodeCount,
  }
}

function currentUserFriendlyInventory() {
  const counts = new Map()
  const samples = new Map()
  let total = 0
  const readBudget = { totalBytes: 0 }
  for (const path of collectCSharpSourceFiles(resolve(REPOSITORY_ROOT, 'src'))) {
    const source = readCSharpSource(path, readBudget)
    const remaining = MAX_USER_FRIENDLY_EXCEPTIONS - total
    for (const argument of extractUserFriendlyExceptionArguments(source, remaining)) {
      const identity = hash(argument)
      counts.set(identity, (counts.get(identity) ?? 0) + 1)
      samples.set(identity, `${relative(REPOSITORY_ROOT, path)}: ${argument.slice(0, 160)}`)
      total += 1
    }
  }
  return { counts, samples, total }
}

function checkUserFriendlyBaseline(writeBaseline) {
  const inventory = currentUserFriendlyInventory()
  const payload = {
    version: 1,
    total: inventory.total,
    expressions: Object.fromEntries([...inventory.counts].sort(([a], [b]) => a.localeCompare(b))),
  }
  if (writeBaseline) {
    writeFileAtomic(
      BASELINE,
      serializeJson(payload, { label: '后端 user-friendly-exception-baseline.json' }),
    )
    console.log(`[backend-i18n] 已记录 ${inventory.total} 处历史 UserFriendlyException 基线。`)
    return inventory.total
  }

  if (!existsSync(BASELINE)) throw new Error('缺少后端 UserFriendlyException 基线')
  const baseline = JSON.parse(
    readRegularFileBoundedSync(BASELINE, {
      maxBytes: SOURCE_SCAN_LIMITS.maxFileBytes,
      label: 'UserFriendlyException 基线',
    }).text,
  )
  const increases = [...inventory.counts]
    .filter(([identity, count]) => count > (baseline.expressions?.[identity] ?? 0))
    .map(([identity, count]) => ({
      added: count - (baseline.expressions?.[identity] ?? 0),
      sample: inventory.samples.get(identity),
    }))
  if (increases.length > 0) {
    throw new Error(
      '检测到新增 UserFriendlyException；新业务异常必须使用已本地化的 BusinessException 错误码：\n' +
        increases
          .slice(0, 20)
          .map(({ added, sample }) => `  +${added} ${sample}`)
          .join('\n'),
    )
  }
  return inventory.total
}

function main() {
  const userFriendlyOnly = process.argv.includes('--user-friendly-only')
  const validateInput = process.argv.includes('--validate-input')
  if (userFriendlyOnly && validateInput) {
    throw new Error('--user-friendly-only 与 --validate-input 不得同时使用')
  }
  const resourceStats = userFriendlyOnly
    ? null
    : validateInput
      ? checkSourceResourceContract()
      : checkResources()
  const count = checkUserFriendlyBaseline(process.argv.includes('--write-baseline'))
  if (userFriendlyOnly) {
    console.log(`[backend-i18n] 历史 UserFriendlyException ${count} 处且无新增。`)
    return
  }
  if (validateInput) {
    console.log(
      `[backend-i18n] 不可生成输入契约有效：${resourceStats.sourceKeys.length} 个简中资源键，`
      + `${resourceStats.errorCodeCount} 个领域错误码；历史 UserFriendlyException ${count} 处且无新增。`,
    )
    return
  }
  console.log(
    `[backend-i18n] 契约有效：${resourceStats.keyCount} 个简中资源键，` +
      `${resourceStats.errorCodeCount} 个领域错误码；历史 UserFriendlyException ${count} 处且无新增。`,
  )
}

try {
  if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main()
} catch (error) {
  console.error(`[backend-i18n] ${error.message}`)
  process.exitCode = 1
}
