/**
 * 禁用 Tailwind 调色板颜色类（自动拦截）
 *
 * 状态只用语义色：`Badge` 的 success / warning / destructive 变体、`text-destructive`、`muted`；
 * 类别用 `chart-1` … `chart-5`；强调只用 `primary`。`bg-blue-500`、`text-emerald-700` 这类
 * 调色板类不随主题 token 走，同一屏会长出一套彩虹。
 *
 * 只认带色阶数字的彩色色相（blue-500、red-50 …）；gray / slate / zinc / neutral / stone、
 * white / black、token 类（bg-primary、text-chart-2、bg-success/15）不受影响。
 *
 * 规范 §0.2 的故意例外（考评打分五档、排产沙盘、开发者文档方法色、全屏驾驶舱等）和
 * `components/ui` 原语层（token 的实现处）在 eslint.config.mjs 里按目录关闭本规则。
 *
 * 为什么焊进 lint：2026-11 审计批次 3/4 已清空业务页存量，条文挡不住回潮。
 * 详见 docs/ui-design-principles.md §0.2 与 docs/ui-governance-blueprint.md。
 */

const HUES = 'blue|indigo|violet|sky|emerald|green|amber|yellow|red|orange|purple|pink|teal|cyan|lime|rose|fuchsia'
const PREFIXES = 'bg|text|border|ring|from|to|via|fill|stroke|outline|decoration|divide|shadow|accent|caret'
const PALETTE = new RegExp(`(?<![\\w-])(?:[\\w-]+:)*(?:${PREFIXES})(?:-[xytrblse])?-(?:${HUES})-\\d{2,3}\\b`, 'g')

/** 返回字符串里命中的调色板类（去重）。导出给单测用。 */
export function findPaletteClasses(text) {
  return [...new Set(text.match(PALETTE) ?? [])]
}

const rule = {
  meta: {
    type: 'problem',
    docs: { description: '禁用 bg-blue-500 一类调色板颜色类，状态与类别改用语义 token' },
    messages: {
      palette: '调色板类 {{classes}} 不随主题走：状态用 Badge 的 success / warning / destructive 变体或 text-destructive，类别用 chart-1…chart-5。见 docs/ui-design-principles.md §0.2。',
    },
    schema: [],
  },
  create(context) {
    function check(node, text) {
      if (typeof text !== 'string' || !text.includes('-')) return
      const hits = findPaletteClasses(text)
      if (hits.length) context.report({ node, messageId: 'palette', data: { classes: hits.join(' ') } })
    }
    return {
      Literal(node) { check(node, node.value) },
      TemplateElement(node) { check(node, node.value.cooked ?? node.value.raw) },
    }
  },
}

const plugin = { rules: { 'no-palette-classes': rule } }

export default plugin
