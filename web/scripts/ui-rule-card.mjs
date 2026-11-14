import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url))
export const REPO_ROOT = resolve(SCRIPT_DIR, '..', '..')
export const PAGE_RULE_BUDGET_BYTES = 20 * 1024

export const BEGIN_MARKER = '<!-- BEGIN:ui-hard-rule-card -->'
export const END_MARKER = '<!-- END:ui-hard-rule-card -->'

export const HARD_RULE_ITEMS = [
  '- **一屏一个主角**：列表在 1366×768、1440×900、1920×1080 首屏必须看见表头与至少一行或空状态。列表页用 `ListPageShell` 插槽（`header` / `filters` / `notice` / `summary`）+ `FilterBar` 扁平筛选条；表上方最多一行 `shrink-0` 筛选 + 一块 `max-h-[28vh]` 内滚汇总（`swapmid/above-table-budget`、`ui-health: aboveTableBlocks`），根容器保持 flex 高度链（`swapmid/datatable-list-root`）。',
  '- **按钮与层级**：每区最多 1 个主按钮（`swapmid/page-header-actions`、`swapmid/single-primary-per-region`、`ui-health: headerPrimary / footerPrimary`）。页头文字按钮 ≤3，第 4 个起进 `PageHeader.moreActions` 或 `DropdownButton`（`swapmid/page-header-actions`）。选中态用 `SegmentedControl` / `FilterChip`（`ui-health: variantToggles`）；实心红只在确认页脚，批量用 `destructive-outline`（`ui-health: solidDestructive`）。',
  '- **对话框、尺寸档与控件**：对话框页脚钉在面板底部（`ui-health: unpinnedDialogFooters`，长表单用 `ScrollableDialog` / `CrudFormDialog`）；自定义 `max-w-*` 必带 `sm:` 前缀（`swapmid/dialog-max-width-prefix`）。禁用 `window.confirm`、原生 `<select>`、`type="date"`（`swapmid/no-native-dialog-controls`）：库表下拉用 `Combobox`（`Select` 仅限静态枚举），空状态用 `EmptyState`。',
  '- **文案、配色与焦点**：页头描述 ≤40 字（`ui-health: descriptionLength`），按钮 2–4 字（>6 字拦：`ui-health: longButtonLabels`）。纯图标按钮用 `IconButton`（`swapmid/icon-button-label`）；数字列用 `numberColumn` / `moneyColumn` / `percentColumn`（`ui-health: inferredNumericColumns`）。状态色走 Badge 语义变体（`swapmid/no-palette-classes`）；焦点环只读 `--focus-ring-*` 令牌（`swapmid/focus-ring-token`）。',
  '- **脚手架、CRUD 与验证**：写表单优先 `useCrudForm` + `CrudFormDialog`（`swapmid/prefer-crud-form`）；服务端分页走 `useCrudResource`（顶层表 `urlSync: true`），写后失效 Router Cache（`reloadAndRefresh` / `useMutateAndRefresh`）。提交前通过 eslint `swapmid/*` 与 `pnpm check:ui`。',
].join('\n')

export function buildAgentsCardBlock() {
  return [
    BEGIN_MARKER,
    HARD_RULE_ITEMS,
    END_MARKER,
  ].join('\n')
}

export function replaceAgentsBlock(agentsContent) {
  const start = agentsContent.indexOf(BEGIN_MARKER)
  const end = agentsContent.indexOf(END_MARKER)
  if (start === -1 || end === -1 || end < start) {
    throw new Error(`web/AGENTS.md 缺少 ${BEGIN_MARKER} ... ${END_MARKER} 标记块`)
  }
  const normalized = agentsContent.replace(/\r\n/g, '\n')
  const nStart = normalized.indexOf(BEGIN_MARKER)
  const nEnd = normalized.indexOf(END_MARKER) + END_MARKER.length
  return `${normalized.slice(0, nStart)}${buildAgentsCardBlock()}${normalized.slice(nEnd)}`
}

export function utf8NoCrBytes(text) {
  return Buffer.byteLength(text.replace(/\r/g, ''), 'utf8')
}

export function parseRulePaths(markdown) {
  const normalized = markdown.replace(/\r\n/g, '\n')
  const match = normalized.match(/^---\n([\s\S]*?)\n---\n/)
  if (!match) return []
  const paths = []
  let inPaths = false
  for (const line of match[1].split('\n')) {
    if (/^paths:\s*$/.test(line)) {
      inPaths = true
      continue
    }
    if (inPaths) {
      const item = line.match(/^\s*-\s*['"]?([^'"]+?)['"]?\s*$/)
      if (item) paths.push(item[1])
      else if (/^\S/.test(line)) inPaths = false
    }
  }
  return paths
}

export function globToRegExp(glob) {
  let out = '^'
  for (let i = 0; i < glob.length; i++) {
    const ch = glob[i]
    if (ch === '*') {
      if (glob[i + 1] === '*') {
        if (glob[i + 2] === '/') {
          out += '(?:.+/)?'
          i += 2
        } else {
          out += '.*'
          i += 1
        }
      } else {
        out += '[^/]*'
      }
    } else if ('\\.^$+?|()[]{}'.includes(ch)) {
      out += `\\${ch}`
    } else {
      out += ch
    }
  }
  return new RegExp(`${out}$`)
}

export function measureMatchedRules(repoRoot = REPO_ROOT) {
  const agentsPath = join(repoRoot, 'web', 'AGENTS.md')
  if (!existsSync(agentsPath)) return { totalBytes: 0, matched: [] }
  const content = readFileSync(agentsPath, 'utf8')
  const bytes = utf8NoCrBytes(content)
  return { totalBytes: bytes, matched: [{ file: 'web/AGENTS.md', bytes }] }
}

export function checkUiRuleCardSync(repoRoot = REPO_ROOT) {
  const agentsPath = join(repoRoot, 'web', 'AGENTS.md')
  const actual = existsSync(agentsPath) ? readFileSync(agentsPath, 'utf8').replace(/\r\n/g, '\n') : ''
  try {
    const expected = replaceAgentsBlock(actual)
    return actual === expected ? [] : ['web/AGENTS.md']
  } catch (error) {
    return [`web/AGENTS.md（${error.message}）`]
  }
}

export function writeUiRuleCards(repoRoot = REPO_ROOT) {
  const agentsPath = join(repoRoot, 'web', 'AGENTS.md')
  const agentsActual = readFileSync(agentsPath, 'utf8')
  writeFileSync(agentsPath, replaceAgentsBlock(agentsActual))
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.includes('--write')) {
    writeUiRuleCards(REPO_ROOT)
    console.log('✓ 已同步界面硬规则卡')
  } else {
    const mismatches = checkUiRuleCardSync(REPO_ROOT)
    const { totalBytes, matched } = measureMatchedRules(REPO_ROOT)
    if (mismatches.length) {
      console.error(`✗ 界面硬规则卡未同步（运行 node scripts/ui-rule-card.mjs --write）：${mismatches.join(', ')}`)
      process.exit(1)
    }
    console.log(`✓ 界面硬规则卡一致；规则注入 ${totalBytes} 字节（上限 ${PAGE_RULE_BUDGET_BYTES}）：`)
    for (const item of matched) console.log(`  ${item.file}: ${item.bytes} bytes`)
    if (totalBytes > PAGE_RULE_BUDGET_BYTES) {
      console.error(`✗ 规则注入体积超出预算：${totalBytes} > ${PAGE_RULE_BUDGET_BYTES}`)
      process.exit(1)
    }
  }
}
