import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const WEB_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const DEFAULT_REPO_ROOT = resolve(WEB_ROOT, '..')

export const TERMINOLOGY_RULES = Object.freeze([
  {
    id: 'no-cn-middleware-for-hub',
    pattern: /(?:数据交换|集成|資料交換|整合)[\u4e00-\u9fa5与和及與、]{0,6}(?:中间件|中間件)/g,
    message: '禁止将 SwapMid（中台 / Platform）误称为「中间件」，请统一使用「企业级数据交换与集成中台」。',
  },
  {
    id: 'no-cn-literal-swap-mid',
    pattern: /(?:交换中间|交換中間)/g,
    message: '禁止使用字面拆译「交换中间」，请统一使用「数据交换与集成中台」。',
  },
  {
    id: 'no-en-data-swap',
    pattern: /\bdata\s+swap\b/gi,
    message: 'Forbidden Chinglish "data swap"; use "data exchange" or "Enterprise Data Exchange & Integration Platform".',
  },
  {
    id: 'no-en-integration-middleware',
    pattern: /\b(?:enterprise\s+)?(?:data\s+exchange|integration)(?:\s*(?:&|and)\s*(?:data\s+exchange|integration))?\s+middleware\b/gi,
    message: 'Do not describe SwapMid as "middleware" (中间件 ≠ 中台); use "Platform" or "Control Plane".',
  },
  {
    id: 'no-en-middle-platform',
    pattern: /\bmiddle\s+platform\b/gi,
    message: 'Avoid literal Chinglish "middle platform"; use "Enterprise Data Exchange & Integration Platform" or "Control Plane".',
  },
  {
    id: 'no-en-management-center',
    pattern: /\bplatform\s+management\s+center\b/gi,
    message: 'Avoid literal Chinglish "Platform Management Center"; use "Enterprise Data Exchange & Integration Platform".',
  },
])

function collectMarkdownFiles(dirPath) {
  if (!existsSync(dirPath)) return []
  const files = []
  for (const entry of readdirSync(dirPath, { withFileTypes: true })) {
    const fullPath = join(dirPath, entry.name)
    if (entry.isDirectory()) {
      files.push(...collectMarkdownFiles(fullPath))
    } else if (entry.isFile() && entry.name.endsWith('.md')) {
      files.push(fullPath)
    }
  }
  return files
}

export function collectTerminologyTargets(repoRoot = DEFAULT_REPO_ROOT) {
  const fixed = [
    'README.md',
    'README.zh-CN.md',
    'AGENTS.md',
    'charts/swapmid/Chart.yaml',
    'web/messages/zh-Hans.json',
    'web/messages/zh-Hant.json',
    'web/messages/en.json',
    'src/SwapMid.Domain.Shared/Localization/SwapMid/zh-Hans.json',
    'src/SwapMid.Domain.Shared/Localization/SwapMid/zh-Hant.json',
    'src/SwapMid.Domain.Shared/Localization/SwapMid/en.json',
  ]
    .map(p => resolve(repoRoot, p))
    .filter(p => existsSync(p))

  const docs = collectMarkdownFiles(resolve(repoRoot, 'docs'))
  const examples = collectMarkdownFiles(resolve(repoRoot, 'examples'))

  return Array.from(new Set([...fixed, ...docs, ...examples])).sort()
}

export function scanTerminologyText(content, fileLabel) {
  const violations = []
  const lines = content.split(/\r?\n/)
  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i]
    for (const rule of TERMINOLOGY_RULES) {
      rule.pattern.lastIndex = 0
      const match = rule.pattern.exec(line)
      if (match) {
        violations.push({
          file: fileLabel,
          line: i + 1,
          ruleId: rule.id,
          matched: match[0],
          message: rule.message,
        })
      }
    }
  }
  return violations
}

export function runTerminologyCheck(repoRoot = DEFAULT_REPO_ROOT) {
  const targets = collectTerminologyTargets(repoRoot)
  const violations = []
  for (const absPath of targets) {
    const rel = relative(repoRoot, absPath).replace(/\\/g, '/')
    const text = readFileSync(absPath, 'utf8')
    violations.push(...scanTerminologyText(text, rel))
  }
  return { checkedFileCount: targets.length, violations }
}

const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)
if (isMain) {
  const { checkedFileCount, violations } = runTerminologyCheck()
  if (violations.length > 0) {
    console.error(`[terminology] 发现 ${violations.length} 处术语/错译违规（共检查 ${checkedFileCount} 个文件）：`)
    for (const v of violations) {
      console.error(`  - ${v.file}:${v.line} [${v.ruleId}] "${v.matched}" -> ${v.message}`)
    }
    process.exit(1)
  }
  console.log(`[terminology] 术语与翻译一致性门禁通过（已检查 ${checkedFileCount} 个对外文档与三语资源文件）。`)
}
