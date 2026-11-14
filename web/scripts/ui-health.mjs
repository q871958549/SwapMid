// 界面结构健康度扫描 + 棘轮基线（docs/ui-governance-blueprint.md §3.2）。
//
// 用 TypeScript 编译器 API 解析 src/app/**/*.tsx，按「最近的含 page.tsx 的目录」归集到路由，
// 量出规范里能机检的数字：页头按钮、页头描述长度、表上方常驻块、对话框页脚主按钮、
// 页脚随正文滚动的对话框、超长按钮文案、只靠标题推断的数字列。存量写进基线，只降不升：
//
//   node scripts/ui-health.mjs                  # --check：任一路由比基线变差就失败（CI 用）
//   node scripts/ui-health.mjs --write-baseline # 修完存量后收紧基线（也会接受恶化，提交前自查 diff）
//   node scripts/ui-health.mjs --report         # 打印每项指标的超限路由
//   node scripts/ui-health.mjs --trend          # 输出一行趋势 CSV
//   node scripts/ui-health.mjs --trend --append # 把这一行写进 docs/metrics/ui-health-trend.csv（每周工作流用）
//
// 这是静态近似：运行时才拼出来的按钮数不到，由首屏契约 e2e 补（蓝图 §3.3）。
// 单条规则（调色板、原生控件、图标按钮）归 eslint swapmid/*，这里只管跨元素计数和数值。

import { execFileSync } from 'node:child_process'
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'
import { appendTrendFile, readTrendExtras } from './ui-health-trend.mjs'
import { checkUiRuleCardSync, measureMatchedRules, PAGE_RULE_BUDGET_BYTES, REPO_ROOT } from './ui-rule-card.mjs'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const APP = join(ROOT, 'src', 'app')
const BASELINE = join(ROOT, 'scripts', 'ui-health-baseline.json')
const MESSAGES = join(ROOT, 'messages', 'zh-Hans.json')
const NUMERIC_COLUMN = join(ROOT, 'src', 'components', 'data-table', 'numeric-column.ts')
const TREND = join(ROOT, '..', 'docs', 'metrics', 'ui-health-trend.csv')

/** 规范上限（docs/ui-design-principles.md）。超过上限且超过基线 = 回归。 */
export const LIMITS = Object.freeze({
  headerTextButtons: 3, // §1.2 页头文字按钮 ≤3
  headerPrimary: 1, // §1.1 每个区域 ≤1 个主按钮
  descriptionLength: 40, // §7 PageHeader 描述 ≤40 字
  aboveTableBlocks: 2, // §2.1 表上方只留一行筛选 + 一块汇总
  footerPrimary: 1, // §1.1 对话框 / 抽屉页脚 ≤1 个主按钮
  unpinnedDialogFooters: 0, // §2.4 页脚留在面板内，不随正文滚出视口
  longButtonLabels: 0, // §7 按钮 2–4 字；>6 字计入
  variantToggles: 0, // §1.1 选中态用 SegmentedControl / FilterChip，不用三元切换按钮变体
  solidDestructive: 0, // §1.3 实心红只出现在确认页脚；批量用 destructive-outline，行内用图标
  inferredNumericColumns: 0, // §3 表格：数字列由列定义声明（数字列工厂或 meta.numeric），标题推断只作兜底
})

/** 取路由内最大值的指标；其余按路由求和。 */
const MAX_METRICS = new Set(['headerTextButtons', 'headerPrimary', 'descriptionLength', 'aboveTableBlocks', 'footerPrimary', 'maxFileLines'])
/** 只进趋势、不拦截：尺寸档位上线前不拦新增任意像素值。 */
const REPORT_ONLY = ['arbitraryPx', 'maxFileLines']
const LONG_LABEL_CJK = 6

const OVERLAY = /(Dialog|DialogContent|Sheet|SheetContent|Drawer|Popover|PopoverContent|AlertDialog|Confirm|Toaster)$/
const OVERLAY_CONTENT = /^(DialogContent|SheetContent|PopoverContent|AlertDialogContent|DropdownMenuContent)$/
const BUTTONISH = /(^Button$|Button$|^ReportGuide$)/
const ICON_BUTTON = /^(IconButton|\w+GuideButton)$/

const isCjk = (ch) => /[一-鿿]/.test(ch)
/** 汉字计 1，其余字符计 0.5（与规范「N 个汉字」口径一致）。 */
export function textWidth(text) {
  let n = 0
  for (const ch of text) n += isCjk(ch) ? 1 : 0.5
  return Math.floor(n)
}
const cjkCount = (text) => [...text].filter(isCjk).length

function opening(node) {
  if (ts.isJsxElement(node)) return node.openingElement
  if (ts.isJsxSelfClosingElement(node)) return node
  return null
}
function tagName(node) {
  const o = opening(node)
  return o ? o.tagName.getText() : null
}
function attr(node, name) {
  const o = opening(node)
  return o?.attributes.properties.find((a) => ts.isJsxAttribute(a) && a.name.getText() === name) ?? null
}
/** 属性值：字面量返回字符串；表达式返回 '{expr}'；无值返回 ''；没有该属性返回 null。 */
function attrString(node, name) {
  const a = attr(node, name)
  if (!a) return null
  if (!a.initializer) return ''
  if (ts.isStringLiteral(a.initializer)) return a.initializer.text
  const e = ts.isJsxExpression(a.initializer) ? a.initializer.expression : null
  if (e && (ts.isStringLiteral(e) || ts.isNoSubstitutionTemplateLiteral(e))) return e.text
  return '{expr}'
}
function attrExpression(node, name) {
  const a = attr(node, name)
  return a?.initializer && ts.isJsxExpression(a.initializer) ? a.initializer.expression ?? null : null
}

/** 属性初始值的源码文本（`cn('a', x && 'b')` 原样返回），用来在表达式里找字面量类名。 */
function attrSource(node, name) {
  return attr(node, name)?.initializer?.getText() ?? ''
}

// 与 components/ui/dialog.tsx 的 pinsDialogFooter 同一口径：DialogContent 默认把页脚钉在面板底部，
// 调用方改了内边距或 overflow 就退出钉底。overflow-hidden 视作自管布局（正文在内层滚动），不算漏钉。
const UNPINS_FOOTER = /(?:^|[\s'"`])(?:[\w-]+:)*(?:p[xytrbl]?-|overflow-(?:visible|y-hidden)\b)/
const SELF_SCROLLING = /(?:^|[\s'"`])overflow-hidden\b/

/** variant 表达式里所有可能取到的字面量（穿过三元、逻辑与或、括号）。 */
function variantValues(node) {
  const a = attr(node, 'variant')
  if (!a?.initializer) return []
  if (ts.isStringLiteral(a.initializer)) return [a.initializer.text]
  const out = []
  const collect = (e) => {
    if (!e) return
    if (ts.isStringLiteral(e) || ts.isNoSubstitutionTemplateLiteral(e)) out.push(e.text)
    else if (ts.isConditionalExpression(e)) { collect(e.whenTrue); collect(e.whenFalse) }
    else if (ts.isBinaryExpression(e)) { collect(e.left); collect(e.right) }
    else if (ts.isParenthesizedExpression(e)) collect(e.expression)
  }
  collect(ts.isJsxExpression(a.initializer) ? a.initializer.expression : null)
  return out
}

const EMPHASIS = new Set(['default', 'secondary'])
const PLAIN = new Set(['outline', 'ghost'])
/** 确认后果的页脚：实心红只允许出现在这里。 */
const CONFIRM_FOOTER = /^(DialogFooter|AlertDialogFooter|SheetFooter)$/
/** 带 `footer` 插槽的弹层内容组件（如 ScrollableDialogContent），插槽渲染成钉底页脚，与 DialogFooter 同等对待。 */
const FOOTER_SLOT_OWNER = /(DialogContent|SheetContent)$/
const isFooterSlot = (node) => ts.isJsxAttribute(node) && node.name.getText() === 'footer'
  && FOOTER_SLOT_OWNER.test(node.parent.parent.tagName.getText())

/**
 * 抽出 numeric-column.ts 里的 `const NUMERIC_TITLE* = /…/` 正则——与 DataTable 运行时同一份标题判定，这里不复制。
 * 一条都抽不到说明那边换了写法，直接报错，免得指标悄悄归零。
 */
export function numericTitlePatterns(source) {
  const patterns = [...source.matchAll(/^const NUMERIC_TITLE\w* = \/(.+)\/([a-z]*)\r?$/gm)].map((m) => new RegExp(m[1], m[2]))
  if (!patterns.length) throw new Error('numeric-column.ts 里找不到 NUMERIC_TITLE* 正则，请同步更新 scripts/ui-health.mjs')
  return patterns
}
let defaultNumericPatterns
const loadNumericPatterns = () => (defaultNumericPatterns ??= numericTitlePatterns(readFileSync(NUMERIC_COLUMN, 'utf8')))

/** 对象字面量里的同名属性赋值（不认展开、简写）。 */
const objProp = (obj, name) => obj.properties.find((p) => ts.isPropertyAssignment(p) && p.name.getText() === name)

function contains(node, re, depth = 0, stopAtOverlay = false) {
  if (!node || depth > 80) return false
  const t = tagName(node)
  if (t && re.test(t)) return true
  if (stopAtOverlay && t && OVERLAY.test(t)) return false
  let found = false
  ts.forEachChild(node, (c) => { if (!found && contains(c, re, depth + 1, stopAtOverlay)) found = true })
  return found
}

function isDesktopHidden(node) {
  const cls = attrString(node, 'className')
  if (!cls || cls === '{expr}') return false
  return /(?:^|\s)(?:sm|md|lg):hidden(?:\s|$)/.test(cls)
}

/** 计算单个子节点或表达式在同一渲染态占用的文档流块数（互斥三元取分路最大值）。 */
function flowBlocksIn(node, depth = 0) {
  if (!node || depth > 40) return 0
  if (ts.isJsxText(node)) return node.text.trim() ? 1 : 0
  if (ts.isJsxExpression(node)) return node.expression ? flowBlocksIn(node.expression, depth + 1) : 0
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node)) {
    return flowBlocksIn(node.expression, depth + 1)
  }
  if (node.kind === ts.SyntaxKind.NullKeyword || node.kind === ts.SyntaxKind.FalseKeyword || node.kind === ts.SyntaxKind.TrueKeyword) {
    return 0
  }
  if (ts.isIdentifier(node) && node.text === 'undefined') return 0
  if (ts.isStringLiteral(node) && !node.text) return 0
  if (ts.isConditionalExpression(node)) {
    return Math.max(flowBlocksIn(node.whenTrue, depth + 1), flowBlocksIn(node.whenFalse, depth + 1))
  }
  if (ts.isBinaryExpression(node)) {
    const op = node.operatorToken.kind
    if (op === ts.SyntaxKind.AmpersandAmpersandToken) return flowBlocksIn(node.right, depth + 1)
    if (op === ts.SyntaxKind.BarBarToken || op === ts.SyntaxKind.QuestionQuestionToken) {
      return Math.max(flowBlocksIn(node.left, depth + 1), flowBlocksIn(node.right, depth + 1))
    }
  }
  if (ts.isJsxFragment(node)) {
    let sum = 0
    for (const c of node.children) sum += flowBlocksIn(c, depth + 1)
    return sum
  }
  if (ts.isJsxElement(node) || ts.isJsxSelfClosingElement(node)) {
    const t = tagName(node)
    if (!t || t === 'PageHeader' || OVERLAY.test(t) || isDesktopHidden(node)) return 0
    return 1
  }
  return 1
}

const ZERO_BUTTONS = Object.freeze({ text: 0, icon: 0, primary: 0 })
const addButtons = (a, b) => ({ text: a.text + b.text, icon: a.icon + b.icon, primary: a.primary + b.primary })
const maxButtons = (a, b) => ({ text: Math.max(a.text, b.text), icon: Math.max(a.icon, b.icon), primary: Math.max(a.primary, b.primary) })

function buttonsIn(node, depth = 0, allowRootOverlayContent = true) {
  if (!node || depth > 80) return ZERO_BUTTONS
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node)) {
    return buttonsIn(node.expression, depth + 1, allowRootOverlayContent)
  }
  if (ts.isJsxExpression(node)) {
    return node.expression ? buttonsIn(node.expression, depth + 1, false) : ZERO_BUTTONS
  }
  if (ts.isConditionalExpression(node)) {
    return maxButtons(buttonsIn(node.whenTrue, depth + 1, false), buttonsIn(node.whenFalse, depth + 1, false))
  }
  if (ts.isBinaryExpression(node)) {
    const op = node.operatorToken.kind
    if (op === ts.SyntaxKind.AmpersandAmpersandToken) return buttonsIn(node.right, depth + 1, false)
    if (op === ts.SyntaxKind.BarBarToken || op === ts.SyntaxKind.QuestionQuestionToken) {
      return maxButtons(buttonsIn(node.left, depth + 1, false), buttonsIn(node.right, depth + 1, false))
    }
  }
  const t = tagName(node)
  if (!allowRootOverlayContent && t && OVERLAY_CONTENT.test(t)) return ZERO_BUTTONS
  let acc = ZERO_BUTTONS
  if (t && BUTTONISH.test(t)) {
    const size = attrString(node, 'size')
    const variant = attrString(node, 'variant')
    const icon = ICON_BUTTON.test(t) || size === 'icon' || /^icon/.test(size ?? '')
    acc = {
      text: icon ? 0 : 1,
      icon: icon ? 1 : 0,
      primary: !icon && (t === 'CreateButton' || (t === 'Button' && (variant === null || variant === 'default'))) ? 1 : 0,
    }
    // 「更多」里的菜单项是收纳区，不算可见按钮。
    if (t === 'DropdownButton') return acc
  }
  if (ts.isJsxSelfClosingElement(node)) return acc
  if (ts.isJsxElement(node) || ts.isJsxFragment(node)) {
    for (const c of node.children) acc = addButtons(acc, buttonsIn(c, depth + 1, false))
    return acc
  }
  ts.forEachChild(node, (c) => { acc = addButtons(acc, buttonsIn(c, depth + 1, false)) })
  return acc
}

/** 文件里 `const t = useTranslations('Ns')` 一类绑定：变量名 → 命名空间。 */
function translatorNamespaces(source) {
  const map = new Map()
  const re = /(?:const|let)\s+(\w+)\s*=\s*(?:await\s+)?(?:useTranslations|getTranslations)\(\s*['"]([\w.]+)['"]/g
  for (const m of source.matchAll(re)) map.set(m[1], m[2])
  return map
}

export function createLookup(messages) {
  return (namespace, key) => {
    let cur = messages
    for (const part of (namespace ? `${namespace}.${key}` : key).split('.')) {
      if (cur && typeof cur === 'object' && part in cur) cur = cur[part]
      else return null
    }
    return typeof cur === 'string' ? cur : null
  }
}

/**
 * 分析单个 tsx 源码，返回文件级指标。`lookup(namespace, key)` 用于解析 t('key') 文案；
 * `numericPatterns` 默认取 numeric-column.ts 的标题正则。
 */
export function analyzeSource(source, fileName = 'file.tsx', lookup = () => null, numericPatterns = loadNumericPatterns()) {
  const sf = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
  const translators = translatorNamespaces(source)
  const text = (e) => {
    if (!e) return null
    if (ts.isStringLiteral(e) || ts.isNoSubstitutionTemplateLiteral(e)) return e.text
    if (ts.isCallExpression(e) && ts.isIdentifier(e.expression) && translators.has(e.expression.text)) {
      const key = e.arguments[0]
      if (key && ts.isStringLiteral(key)) return lookup(translators.get(e.expression.text), key.text)
    }
    return null
  }
  const lines = source.split('\n')
  // 合理例外在元素上方两行内写 `ui-health-allow <指标>: 理由`，例如「预检后主次互换」不是选中态。
  const allowed = (node, metric) => {
    const line = sf.getLineAndCharacterOfPosition(node.getStart(sf)).line
    return lines.slice(Math.max(0, line - 2), line).some((l) => l.includes(`ui-health-allow ${metric}`))
  }
  const m = {
    headerTextButtons: 0, headerPrimary: 0, descriptionLength: 0, aboveTableBlocks: 0,
    footerPrimary: 0, unpinnedDialogFooters: 0, longButtonLabels: 0, variantToggles: 0, solidDestructive: 0, inferredNumericColumns: 0,
    arbitraryPx: (source.match(/(?<![\w-])(?:[\w-]+:)*-?(?:w|h|size|min-w|max-w|min-h|max-h|text|top|left|right|bottom|inset|gap|p[xytrbl]?|m[xytrbl]?)-\[\d+(?:\.\d+)?px\]/g) ?? []).length,
    maxFileLines: source.split('\n').length,
    longLabels: [],
  }

  // 列标题按 DataTable 的 resolveColumnTitle 同序取：meta.title → header 文案 → header 里 DataTableColumnHeader 的 title。
  // 拼接、三元、变量标题解析不出就不计（静态近似）。
  const headerTitle = (e) => {
    let found = null
    const find = (n) => {
      if (found !== null) return
      if (tagName(n) === 'DataTableColumnHeader') {
        const init = attr(n, 'title')?.initializer
        found = (init && ts.isStringLiteral(init) ? init.text : text(attrExpression(n, 'title'))) ?? ''
        return
      }
      ts.forEachChild(n, find)
    }
    find(e)
    return found || null
  }
  /** 列定义只靠标题推断才右对齐：没有 meta.numeric，也没有 numberRange 筛选，标题却命中数字词。 */
  const isInferredNumericColumn = (obj) => {
    const header = objProp(obj, 'header')
    if (!header || !(objProp(obj, 'accessorKey') || objProp(obj, 'accessorFn') || objProp(obj, 'id'))) return false
    if (obj.properties.some(ts.isSpreadAssignment)) return false
    const meta = objProp(obj, 'meta')?.initializer
    // meta 是变量或带展开时看不到内容，按「可能已声明」放过，宁漏勿误。
    if (meta && (!ts.isObjectLiteralExpression(meta) || meta.properties.some(ts.isSpreadAssignment))) return false
    if (meta && objProp(meta, 'numeric')) return false
    if (meta && objProp(meta, 'filterVariant')?.initializer.getText().includes('numberRange')) return false
    const metaTitle = meta && objProp(meta, 'title')
    const title = (metaTitle ? text(metaTitle.initializer) : null) ?? text(header.initializer) ?? headerTitle(header.initializer)
    return !!title && numericPatterns.some((re) => re.test(title.trim()))
  }

  function visit(node, inOverlay, inConfirm) {
    if (ts.isObjectLiteralExpression(node) && isInferredNumericColumn(node)) m.inferredNumericColumns++
    const t = tagName(node)
    if (t === 'PageHeader') {
      let textButtons = 0
      let primary = 0
      // 旧的自由插槽与 PageHeader 结构化插槽都计入；moreActions 渲染成一个「更多」按钮，里面的菜单项不计。
      for (const slot of ['action', 'extra', 'primaryAction', 'secondaryActions']) {
        const r = buttonsIn(attrExpression(node, slot))
        textButtons += r.text
        primary += r.primary
      }
      if (attr(node, 'moreActions')) textButtons++
      m.headerTextButtons = Math.max(m.headerTextButtons, textButtons)
      m.headerPrimary = Math.max(m.headerPrimary, primary)
      const d = attr(node, 'description')
      const desc = d?.initializer && (ts.isStringLiteral(d.initializer) ? d.initializer.text : text(attrExpression(node, 'description')))
      if (desc) m.descriptionLength = Math.max(m.descriptionLength, textWidth(desc))
    }
    if (t === 'DialogFooter' || t === 'SheetFooter') m.footerPrimary = Math.max(m.footerPrimary, buttonsIn(node).primary)
    const dialogClass = t === 'DialogContent' ? attrSource(node, 'className') : ''
    if (t === 'DialogContent' && contains(node, /^DialogFooter$/) && UNPINS_FOOTER.test(dialogClass) && !SELF_SCROLLING.test(dialogClass)) {
      m.unpinnedDialogFooters++
    }
    if ((t === 'Button' || t === 'CreateButton') && ts.isJsxElement(node)) {
      let label = ''
      for (const c of node.children) {
        if (ts.isJsxText(c)) label += c.text.trim()
        else if (ts.isJsxExpression(c)) label += text(c.expression) ?? ''
      }
      if (cjkCount(label) > LONG_LABEL_CJK) { m.longButtonLabels++; m.longLabels.push(label) }
    }
    const confirm = inConfirm || (t !== null && CONFIRM_FOOTER.test(t)) || isFooterSlot(node)
    if (t === 'Button') {
      const values = variantValues(node)
      // 用主按钮色表达选中态：一边是 default / secondary，另一边是 outline / ghost。
      if (values.some((v) => EMPHASIS.has(v)) && values.some((v) => PLAIN.has(v)) && !allowed(node, 'variantToggles')) m.variantToggles++
      if (values.includes('destructive') && !confirm && !allowed(node, 'solidDestructive')) m.solidDestructive++
    }
    const overlay = inOverlay || (t !== null && OVERLAY.test(t))
    // 表上方常驻块：容器的直接子节点里有含 DataTable 的一支，数它前面占文档流的兄弟（互斥分支取最大值）。
    if (!overlay && (ts.isJsxElement(node) || ts.isJsxFragment(node)) && t !== 'DataTable') {
      const kids = node.children
      const idx = kids.findIndex((k) => contains(k, /^DataTable$/, 0, true))
      if (idx > 0) {
        let blocks = 0
        for (const k of kids.slice(0, idx)) blocks += flowBlocksIn(k)
        m.aboveTableBlocks = Math.max(m.aboveTableBlocks, blocks)
      }
    }
    ts.forEachChild(node, (c) => visit(c, overlay, confirm))
  }
  visit(sf, false, false)
  return m
}

function walk(dir, acc = []) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name)
    if (e.isDirectory()) walk(p, acc)
    else if (e.name.endsWith('.tsx') && !/\.(test|spec)\.tsx$/.test(e.name)) acc.push(p)
  }
  return acc
}

function walkAllCode(dir, acc = []) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name)
    if (e.isDirectory()) walkAllCode(p, acc)
    else if (/\.(tsx|ts)$/.test(e.name) && !/\.(test|spec)\.(tsx|ts)$/.test(e.name)) acc.push(p)
  }
  return acc
}

/**
 * 查找文件直接所属的页面路由：
 * 只有直接位于 appDir 下的文件才归属根路由 '/'；子目录若直到 appDir 仍未遇到子级 page.tsx，返回 null 交由引用图归集。
 */
export function directRouteOf(file, appDir = APP, hasPage = (d) => existsSync(join(d, 'page.tsx'))) {
  const fileDir = dirname(file)
  if (fileDir === appDir) return hasPage(appDir) ? '/' : null
  let dir = fileDir
  while (dir.startsWith(appDir) && dir !== appDir) {
    if (hasPage(dir)) return relative(appDir, dir).replaceAll('\\', '/')
    dir = dirname(dir)
  }
  return null
}

function resolveLocalImport(fromFile, spec, srcRoot) {
  let base = null
  if (spec.startsWith('.')) base = resolve(dirname(fromFile), spec)
  else if (spec.startsWith('@/')) base = resolve(srcRoot, spec.slice(2))
  if (!base) return null
  for (const cand of [base, `${base}.tsx`, `${base}.ts`, join(base, 'index.tsx'), join(base, 'index.ts')]) {
    if (existsSync(cand) && !cand.endsWith('.d.ts')) return cand
  }
  return null
}

/** 为无自身 page.tsx 的子模块文件建立「被哪些真实页面路由引用」的映射，避免冒泡污染根路由 '/'。 */
export function resolveConsumerRoutes(files, sourcesByFile, appDir = APP) {
  const srcRoot = resolve(appDir, '..')
  const direct = new Map()
  const reverseImports = new Map()
  for (const file of files) {
    direct.set(file, directRouteOf(file, appDir))
    const src = sourcesByFile.get(file) ?? ''
    for (const m of src.matchAll(/(?:from|import)\s*['"]([^'"]+)['"]/g)) {
      const target = resolveLocalImport(file, m[1], srcRoot)
      if (target && sourcesByFile.has(target)) {
        let list = reverseImports.get(target)
        if (!list) { list = []; reverseImports.set(target, list) }
        list.push(file)
      }
    }
  }
  const memo = new Map()
  const consumersOf = (file, visiting = new Set()) => {
    const own = direct.get(file)
    if (own) return [own]
    if (memo.has(file)) return memo.get(file)
    if (visiting.has(file)) return []
    visiting.add(file)
    const out = new Set()
    for (const importer of reverseImports.get(file) ?? []) {
      for (const r of consumersOf(importer, visiting)) out.add(r)
    }
    visiting.delete(file)
    const sorted = [...out].sort((a, b) => a.localeCompare(b))
    memo.set(file, sorted)
    return sorted
  }
  return { direct, consumersOf }
}

/** 扫描全部路由，返回 { route: metrics }（按路由名排序）。 */
export function scanRoutes() {
  const lookup = createLookup(JSON.parse(readFileSync(MESSAGES, 'utf8')))
  const allCodeFiles = walkAllCode(APP)
  const sourcesByFile = new Map(allCodeFiles.map((f) => [f, readFileSync(f, 'utf8')]))
  const { direct, consumersOf } = resolveConsumerRoutes(allCodeFiles, sourcesByFile, APP)
  const routes = {}
  for (const file of walk(APP)) {
    const ownRoute = direct.get(file)
    const consumerList = ownRoute ? [ownRoute] : consumersOf(file)
    // 框架约定壳文件（如 (dashboard)/layout.tsx）无单个 page 引用时归回 '/'
    const targetRoutes = consumerList.length ? consumerList : ['/']
    const fm = analyzeSource(sourcesByFile.get(file), file, lookup)
    targetRoutes.forEach((route, idx) => {
      const agg = (routes[route] ??= { longLabels: [] })
      for (const [k, v] of Object.entries(fm)) {
        if (k === 'longLabels') {
          if (idx === 0) agg.longLabels.push(...v)
        } else if (MAX_METRICS.has(k)) {
          agg[k] = Math.max(agg[k] ?? 0, v)
        } else if (idx === 0) {
          // 求和类指标对跨路由复用的共享文件只记入首个消费路由一次，保持全站文件级总数不翻倍
          agg[k] = (agg[k] ?? 0) + v
        } else {
          agg[k] = agg[k] ?? 0
        }
      }
    })
  }
  return Object.fromEntries(Object.entries(routes).sort(([a], [b]) => a.localeCompare(b)))
}

/** 只保留超过上限的指标，作为基线。 */
export function toBaseline(routes) {
  const out = {}
  for (const [route, m] of Object.entries(routes)) {
    const over = Object.fromEntries(Object.keys(LIMITS).filter((k) => m[k] > LIMITS[k]).map((k) => [k, m[k]]))
    if (Object.keys(over).length) out[route] = over
  }
  return out
}

/** 与基线比较：regressions 必须为空；improvements 提示可以收紧基线。 */
export function compare(routes, baseline) {
  const regressions = []
  const improvements = []
  for (const [route, m] of Object.entries(routes)) {
    for (const [k, limit] of Object.entries(LIMITS)) {
      const allowed = Math.max(limit, baseline[route]?.[k] ?? limit)
      if (m[k] > allowed) regressions.push({ route, metric: k, value: m[k], allowed })
      else if (baseline[route]?.[k] !== undefined && m[k] < baseline[route][k]) improvements.push({ route, metric: k, value: m[k], baseline: baseline[route][k] })
    }
  }
  for (const route of Object.keys(baseline)) if (!(route in routes)) improvements.push({ route, metric: '*', value: 0, baseline: 'removed' })
  return { regressions, improvements }
}

const HINTS = {
  headerTextButtons: '第 4 个起放进 DropdownButton「更多」（规范 §1.2）',
  headerPrimary: '页头只留一个主按钮，其余用 outline（§1.1）',
  descriptionLength: '描述一行 ≤40 字，其余放 Popover / ReportGuide（§7）',
  aboveTableBlocks: '表上方只留一行筛选和一块汇总；说明进 Popover，配置进 Dialog / Sheet（§2.1）',
  footerPrimary: '对话框页脚只留一个主按钮（§1.1）',
  unpinnedDialogFooters: 'DialogContent 默认钉住页脚；改了内边距或 overflow 就会退出，自定义布局请用 overflow-hidden + 内层滚动区（§2.4）',
  longButtonLabels: '按钮 2–4 个字，对象说明放 tooltip 或菜单（§7）',
  variantToggles: '选中态用 SegmentedControl（互斥）或 FilterChip（开关），别用 variant={选中 ? \'default\' : \'outline\'}（§1.1）',
  solidDestructive: '实心红只留给确认框页脚；批量危险动作用 variant="destructive-outline"，行内用灰色图标（§1.3）',
  inferredNumericColumns: '数字列用 numberColumn / moneyColumn / percentColumn，或在列定义写 meta.numeric；标题推断只作兜底（§3 表格）',
}

function readBaseline() {
  if (!existsSync(BASELINE)) return {}
  return JSON.parse(readFileSync(BASELINE, 'utf8')).routes ?? {}
}

/** 本地日期（工作流设 TZ=Asia/Shanghai；用 UTC 会在周一凌晨写成周日）。 */
const localDate = (now = new Date()) => `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`

function trendRow(routes) {
  const date = localDate()
  let commit = ''
  try { commit = execFileSync('git', ['rev-parse', '--short=9', 'HEAD'], { cwd: ROOT }).toString().trim() } catch { /* 非 git 环境 */ }
  const all = Object.values(routes)
  const overRoutes = (k) => all.filter((m) => m[k] > LIMITS[k]).length
  const sum = (k) => all.reduce((s, m) => s + (m[k] ?? 0), 0)
  const extras = readTrendExtras(ROOT)
  return [date, commit, all.length, Object.keys(toBaseline(routes)).length,
    overRoutes('headerTextButtons'), overRoutes('descriptionLength'), overRoutes('aboveTableBlocks'), overRoutes('footerPrimary'),
    sum('unpinnedDialogFooters'), sum('longButtonLabels'), sum('arbitraryPx'), all.filter((m) => m.maxFileLines > 500).length,
    sum('variantToggles'), sum('solidDestructive'), sum('inferredNumericColumns'), extras.swapmidSuppressions, extras.contractRoutes, extras.ruleBytes].join(',')
}
export const TREND_HEADER = 'date,commit,routes,violating_routes,header_buttons_over,description_over,above_table_over,footer_primary_over,unpinned_dialog_footers,long_button_labels,arbitrary_px,files_over_500_lines,variant_toggles,solid_destructive,inferred_numeric_columns,swapmid_suppressions,contract_routes,rule_bytes'

function main(argv) {
  const routes = scanRoutes()
  if (argv.includes('--write-baseline')) {
    const routesOut = toBaseline(routes)
    const doc = { schemaVersion: 1, generatedBy: 'node scripts/ui-health.mjs --write-baseline', limits: LIMITS, routes: routesOut }
    writeFileSync(BASELINE, `${JSON.stringify(doc, null, 2)}\n`)
    console.log(`✓ 已写基线：${Object.keys(routesOut).length} 个路由有存量超限 → ${relative(ROOT, BASELINE)}`)
    return 0
  }
  if (argv.includes('--trend')) {
    if (argv.includes('--append')) {
      const row = trendRow(routes)
      const appended = appendTrendFile(TREND, TREND_HEADER, row)
      console.log(appended ? `✓ 已追加趋势行：${row}` : '趋势文件末行已是当前提交，没有追加。')
      return 0
    }
    if (argv.includes('--header')) console.log(TREND_HEADER)
    console.log(trendRow(routes))
    return 0
  }
  if (argv.includes('--report')) {
    for (const k of [...Object.keys(LIMITS), ...REPORT_ONLY]) {
      const limit = LIMITS[k] ?? (k === 'maxFileLines' ? 500 : 0)
      const over = Object.entries(routes).filter(([, m]) => m[k] > limit).sort(([, a], [, b]) => b[k] - a[k])
      console.log(`\n${k}（上限 ${limit}）：${over.length} 个路由`)
      for (const [route, m] of over.slice(0, 15)) console.log(`  ${m[k]}\t${route}`)
    }
    return 0
  }
  const cardMismatches = checkUiRuleCardSync(REPO_ROOT)
  if (cardMismatches.length) {
    console.error(`✗ 界面硬规则卡三处不一致（请运行 node scripts/ui-rule-card.mjs --write）：${cardMismatches.join(', ')}`)
    return 1
  }
  const { totalBytes: ruleBytes } = measureMatchedRules(REPO_ROOT)
  if (ruleBytes > PAGE_RULE_BUDGET_BYTES) {
    console.error(`✗ 触碰业务页时注入的规则体积 ${ruleBytes} 字节超过上限 ${PAGE_RULE_BUDGET_BYTES} 字节（20KB），请将实现细节或事故记录下沉至窄路径规则或 docs/。`)
    return 1
  }
  const { regressions, improvements } = compare(routes, readBaseline())
  if (regressions.length) {
    console.error(`✗ 界面健康度有 ${regressions.length} 项比基线变差：`)
    for (const r of regressions) console.error(`- ${r.route}：${r.metric} = ${r.value}（允许 ${r.allowed}）。${HINTS[r.metric]}`)
    console.error('确属例外时与评审确认后运行 node scripts/ui-health.mjs --write-baseline，并在 PR 里说明。')
    return 1
  }
  console.log(`✓ 界面健康度：${Object.keys(routes).length} 个路由均未超过基线`)
  if (improvements.length) console.log(`  有 ${improvements.length} 项已低于基线，可运行 --write-baseline 收紧。`)
  return 0
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2))
}
