// 裸中文扫描仪表：i18n「后续期·业务正文逐域渐进」(~28 域) 唯一的进度量尺——
// 没有它「翻完了没」全靠肉眼数（见 docs/i18n-blueprint.md）。
//
// 扫 web/src/**/*.{ts,tsx} 里的 CJK 字面量（字符串/JSX 文本），排除：
//   - *.test.* / *.test.tsx 等测试文件（断言用的中文原文不算"待翻译业务正文"）
//   - 生成物（src/lib/api/generated/**、*.generated.ts——后端 DTO/契约文档数据，不进翻译范围）
//   - 注释（// 行注释 / 块注释，含 JSX 注释语法）
//   - console.* 调用行（开发期日志，不面向最终用户）
//
// 按 (dashboard)/{域} 一级目录聚合（域外文件按 components/lib 等顶层目录归组），
// 输出「文件数 + 中文字面量条数」排行表 + 总计。用 Node 内置能力（fs/path/正则），不引新依赖。
//
// 这是启发式扫描（非真 AST 解析）：CJK 判定按「连续 CJK 字符（含中文标点）游程」计数为 1 条字面量，
// 不区分它出现在字符串字面量 / JSX 文本 / 对象 key 里——用作进度趋势仪表足够，不追求逐字节精确。
//
// 用法：node scripts/scan-hardcoded-cn.mjs

import { createHash } from 'node:crypto'
import {
  existsSync,
  lstatSync,
  opendirSync,
  realpathSync,
} from 'node:fs'
import { dirname, isAbsolute, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { serializeJson } from './i18n-generation-core.mjs'
import { readRegularFileBoundedSync, writeFileAtomic } from './i18n-safe-files.mjs'

const ROOT = process.env.I18N_WORKSPACE_ROOT
  ? resolve(process.env.I18N_WORKSPACE_ROOT)
  : resolve(dirname(fileURLToPath(import.meta.url)), '..')
const SRC = join(ROOT, 'src')
const BASELINE = join(ROOT, 'i18n', 'hardcoded-cn-baseline.json')

const EXCLUDE_DIR_SEGMENTS = new Set(['generated', 'node_modules'])
export const SOURCE_SCAN_LIMITS = Object.freeze({
  maxFiles: 5_000,
  maxDirectories: 5_000,
  maxDirectoryEntries: 20_000,
  maxFileBytes: 4 * 1024 * 1024,
  maxTotalBytes: 32 * 1024 * 1024,
})
export const MAX_CJK_LITERALS = 50_000

/** 是否为测试文件 / 生成物文件（按文件名判断，目录级 generated 已在枚举阶段排除）。 */
export function isExcludedFile(relPath) {
  const base = relPath.split(/[\\/]/).pop() ?? ''
  if (/\.test\.(ts|tsx)$/.test(base)) return true
  if (/\.generated\.(ts|tsx)$/.test(base)) return true
  return false
}

function isGeneratedSourceFile(relPath) {
  const base = relPath.split(/[\\/]/).pop() ?? ''
  return /\.generated\.(ts|tsx)$/.test(base)
}

// 全部用 \uXXXX 码点转义、不写字面 CJK 字符：兼容表意文字区（U+F900 起）与部分统一表意文字在多数字体下
// 渲染完全相同，字面字符写法在编辑/复制中容易被悄悄替换成形似但码点不同的字符（本脚本编写过程中
// 已实测踩过一次）；转义写法把码点钉死、可逐位核对，不依赖任何特定字体渲染。
const CJK_IDEOGRAPH_RANGES = '一-鿿㐀-䶿豈-﫿' // 统一表意文字 + 扩展 A + 兼容表意文字
const CJK_PUNCT_RANGES = '　-〿＀-￯' // 中文标点 + 全角符号（含全角数字/字母/标点）
/** 连续 CJK 游程（表意字符与中文标点混排也算一条），游程须含 >= 1 个真实表意字符才计数。 */
const CJK_RUN = new RegExp(`[${CJK_IDEOGRAPH_RANGES}${CJK_PUNCT_RANGES}]+`, 'g')
const HAS_IDEOGRAPH = new RegExp(`[${CJK_IDEOGRAPH_RANGES}]`)

function assertInside(root, path, label) {
  const fromRoot = relative(root, path)
  if (fromRoot === '' || (!fromRoot.startsWith('..') && !isAbsolute(fromRoot))) return
  throw new Error(`${label}越出前端源码根目录：${path}`)
}

function compareText(left, right) {
  return left < right ? -1 : left > right ? 1 : 0
}

function checkedLimits(overrides) {
  const limits = { ...SOURCE_SCAN_LIMITS, ...overrides }
  for (const [name, value] of Object.entries(limits)) {
    if (!Number.isSafeInteger(value) || value <= 0) throw new Error(`源码扫描预算 ${name} 必须是正安全整数`)
  }
  return limits
}

function assertSameFile(pathStat, descriptorStat, path) {
  if (!pathStat.isFile() || !descriptorStat.isFile()) throw new Error(`前端源码树不得包含特殊文件：${path}`)
  if (pathStat.dev !== descriptorStat.dev || pathStat.ino !== descriptorStat.ino) {
    throw new Error(`前端源码文件在扫描期间发生替换：${path}`)
  }
}

/**
 * 迭代枚举 src 下的输入树；拒绝符号链接/特殊文件，并在读取 PR 数据前施加确定的资源预算。
 * 返回值含已由 O_NOFOLLOW + fstat 校验后读取的 .ts/.tsx 文本，避免枚举与读取之间再次产生竞态窗口。
 */
export function collectSourceFiles(sourceRoot, overrides = {}) {
  const limits = checkedLimits(overrides)
  const root = resolve(sourceRoot)
  const rootStat = lstatSync(root)
  if (rootStat.isSymbolicLink() || !rootStat.isDirectory()) {
    throw new Error(`前端源码根目录必须是普通目录且不得为符号链接：${root}`)
  }

  const realRoot = realpathSync(root)
  const directories = [realRoot]
  const files = []
  const budget = { files: 0, directories: 1, entries: 0, totalBytes: 0 }

  while (directories.length > 0) {
    const directory = directories.pop()
    const directoryStat = lstatSync(directory)
    if (directoryStat.isSymbolicLink() || !directoryStat.isDirectory()) {
      throw new Error(`前端源码目录必须是普通目录且不得为符号链接：${directory}`)
    }
    const realDirectory = realpathSync(directory)
    assertInside(realRoot, realDirectory, '前端源码目录')

    const handle = opendirSync(realDirectory)
    const entries = []
    try {
      let entry
      while ((entry = handle.readSync()) !== null) {
        budget.entries += 1
        if (budget.entries > limits.maxDirectoryEntries) {
          throw new Error(`前端源码目录项超过上限 ${limits.maxDirectoryEntries}`)
        }
        entries.push(entry.name)
      }
    } finally {
      handle.closeSync()
    }
    entries.sort(compareText)

    const childDirectories = []
    for (const name of entries) {
      const path = resolve(realDirectory, name)
      const pathStat = lstatSync(path)
      if (pathStat.isSymbolicLink()) throw new Error(`前端源码树不得包含符号链接：${relative(realRoot, path)}`)
      const realPath = realpathSync(path)
      assertInside(realRoot, realPath, '前端源码路径')

      if (pathStat.isDirectory()) {
        budget.directories += 1
        if (budget.directories > limits.maxDirectories) {
          throw new Error(`前端源码目录数超过上限 ${limits.maxDirectories}`)
        }
        if (!EXCLUDE_DIR_SEGMENTS.has(name)) childDirectories.push(realPath)
        continue
      }
      if (!pathStat.isFile()) throw new Error(`前端源码树不得包含特殊文件：${relative(realRoot, path)}`)

      const relativePath = relative(realRoot, path)
      // 生成物不进翻译扫描，也不占单文件预算——api-doc-data.generated.ts 已破 4MiB。
      if (isGeneratedSourceFile(relativePath)) continue

      budget.files += 1
      if (budget.files > limits.maxFiles) throw new Error(`前端源码文件数超过上限 ${limits.maxFiles}`)
      if (pathStat.size > limits.maxFileBytes) {
        throw new Error(`前端源码文件超过单文件上限 ${limits.maxFileBytes} 字节：${relativePath}`)
      }
      const remainingBytes = limits.maxTotalBytes - budget.totalBytes
      if (remainingBytes < 0 || pathStat.size > remainingBytes) {
        throw new Error(`前端源码文件总量超过上限 ${limits.maxTotalBytes} 字节`)
      }
      let actualBytes = pathStat.size
      if (/\.(ts|tsx)$/.test(name)) {
        const maxBytes = Math.min(limits.maxFileBytes, remainingBytes)
        const label = `前端源码文件 ${relativePath}`
        let content
        try {
          content = readRegularFileBoundedSync(path, { maxBytes, label })
        } catch (error) {
          if (maxBytes < limits.maxFileBytes && error.message === `${label}不得超过 ${maxBytes} 字节`) {
            throw new Error(`前端源码文件总量超过上限 ${limits.maxTotalBytes} 字节`)
          }
          throw error
        }
        const currentPathStat = lstatSync(path)
        assertSameFile(pathStat, currentPathStat, relativePath)
        actualBytes = content.bytes
        files.push({ abs: realPath, rel: relative(realRoot, realPath), text: content.text })
      }
      budget.totalBytes += actualBytes
    }
    for (let index = childDirectories.length - 1; index >= 0; index -= 1) {
      directories.push(childDirectories[index])
    }
  }

  return files.sort((left, right) => compareText(left.rel, right.rel))
}

/** 去掉块注释（含 JSX 注释语法的注释体）；跨行安全（[\s\S]）。 */
function stripBlockComments(text) {
  return text.replace(/\/\*[\s\S]*?\*\//g, '')
}

/**
 * 按行处理：去掉行注释（// 之后到行尾），并整行跳过 console.* 调用——
 * 用「前导字符属于语句边界（行首/空白/`;{}(),`）」这一启发式避免误伤 `https://` 之类的字符串内容。
 */
function stripLineCommentsAndConsole(text) {
  return text
    .replace(/\r\n?/g, '\n') // CRLF/CR 先归一化：残留 \r 是行终止符，`.` 越不过去，`.*$` 会整条失配、行注释剥不掉
    .split('\n')
    .map((line) => {
      if (/console\s*\.\s*\w+\s*\(/.test(line)) return ''
      return line.replace(/(^|[\s;{}(),])\/\/.*$/, '$1')
    })
    .join('\n')
}

/** 统计一个文件里的 CJK 字面量条数（游程数，游程须含 >= 1 个真表意字符）。 */
export function findCjkLiterals(rawText, maxLiterals = MAX_CJK_LITERALS) {
  if (!Number.isSafeInteger(maxLiterals) || maxLiterals < 0) {
    throw new Error('裸中文字面量预算必须是非负安全整数')
  }
  const cleaned = stripLineCommentsAndConsole(stripBlockComments(rawText))
  const literals = []
  CJK_RUN.lastIndex = 0
  let match
  while ((match = CJK_RUN.exec(cleaned)) !== null) {
    if (!HAS_IDEOGRAPH.test(match[0])) continue
    if (literals.length >= maxLiterals) {
      throw new Error(`裸中文字面量超过本次剩余额度 ${maxLiterals}`)
    }
    literals.push(match[0])
  }
  return literals
}

/** 按「(dashboard)/{域}」一级目录聚合；域外文件按 src 下顶层目录归组。 */
function groupOf(relPath) {
  const parts = relPath.split(/[\\/]/) // e.g. ['app','(dashboard)','enterprise-management','employees','page.tsx']
  if (parts[0] === 'app') {
    // parts.length===3 时 parts[2] 已是文件名本身（如 layout.tsx/error.tsx 直接挂在 (dashboard)/ 下，
    // 非某个业务域目录）——归一个 _root 分组，不要把文件名误当域名。
    if (parts[1] === '(dashboard)' && parts[2]) {
      return parts.length > 3 ? `(dashboard)/${parts[2]}` : '(dashboard)/_root'
    }
    if (parts[1] === '(auth)') return 'app/(auth)'
    if (parts[1]) return `app/${parts[1]}`
    return 'app'
  }
  return parts[0] ?? 'other'
}

function main() {
  const files = collectSourceFiles(SRC).filter((file) => !isExcludedFile(file.rel))

  /** @type {Map<string, { fileCount: number; literalCount: number }>} */
  const groups = new Map()
  let totalFiles = 0
  let totalLiterals = 0
  const literalCounts = new Map()
  const literalSamples = new Map()

  for (const { rel, text } of files) {
    const literals = findCjkLiterals(text, MAX_CJK_LITERALS - totalLiterals)
    const count = literals.length
    if (count === 0) continue
    for (const literal of literals) {
      const hash = createHash('sha256').update(literal, 'utf8').digest('hex').slice(0, 20)
      literalCounts.set(hash, (literalCounts.get(hash) ?? 0) + 1)
      literalSamples.set(hash, literal)
    }

    const group = groupOf(rel)
    const entry = groups.get(group) ?? { fileCount: 0, literalCount: 0 }
    entry.fileCount += 1
    entry.literalCount += count
    groups.set(group, entry)

    totalFiles += 1
    totalLiterals += count
  }

  const rows = [...groups.entries()].sort((a, b) => b[1].literalCount - a[1].literalCount)

  const groupColWidth = Math.max(6, ...rows.map(([g]) => g.length))
  const header = `${'分组/域'.padEnd(groupColWidth)}  文件数  中文字面量条数`
  console.log(header)
  console.log('-'.repeat(header.length))
  for (const [group, { fileCount, literalCount }] of rows) {
    console.log(`${group.padEnd(groupColWidth)}  ${String(fileCount).padStart(4)}  ${String(literalCount).padStart(6)}`)
  }
  console.log('-'.repeat(header.length))
  console.log(`${'总计'.padEnd(groupColWidth)}  ${String(totalFiles).padStart(4)}  ${String(totalLiterals).padStart(6)}`)
  console.log(`\n（共扫描 ${files.length} 个 .ts/.tsx 文件，其中 ${totalFiles} 个含裸中文字面量）`)

  const currentBaseline = {
    version: 1,
    total: totalLiterals,
    literals: Object.fromEntries([...literalCounts].sort(([left], [right]) => left.localeCompare(right))),
  }

  if (process.argv.includes('--write-baseline')) {
    writeFileAtomic(
      BASELINE,
      serializeJson(currentBaseline, { label: 'i18n/hardcoded-cn-baseline.json' }),
    )
    console.log(`\n[hardcoded-cn] 已刷新基线：${totalLiterals} 条。`)
    return
  }

  if (process.argv.includes('--check')) {
    if (!existsSync(BASELINE)) {
      console.error('\n[hardcoded-cn] 缺少 i18n/hardcoded-cn-baseline.json。')
      process.exitCode = 1
      return
    }
    const baseline = JSON.parse(
      readRegularFileBoundedSync(BASELINE, {
        maxBytes: SOURCE_SCAN_LIMITS.maxFileBytes,
        label: '裸中文扫描基线',
      }).text,
    )
    if (baseline.version !== 1 || !baseline.literals) {
      console.error('\n[hardcoded-cn] 基线格式无效。')
      process.exitCode = 1
      return
    }

    const increases = [...literalCounts]
      .filter(([hash, count]) => count > (baseline.literals[hash] ?? 0))
      .map(([hash, count]) => ({
        hash,
        added: count - (baseline.literals[hash] ?? 0),
        sample: literalSamples.get(hash),
      }))
    if (increases.length > 0) {
      console.error(`\n[hardcoded-cn] 检测到 ${increases.length} 类新增裸中文；请改用 next-intl：`)
      for (const item of increases.slice(0, 30)) {
        console.error(`  +${item.added}  ${item.sample}  (${item.hash})`)
      }
      process.exitCode = 1
      return
    }
    console.log(`\n[hardcoded-cn] 未新增裸中文；当前 ${totalLiterals} 条，基线 ${baseline.total} 条。`)
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main()
