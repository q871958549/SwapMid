/**
 * 焦点环只读令牌（自动拦截）
 *
 * 全系统只有一个焦点环：宽度 `--focus-ring-width`、颜色 `--focus-ring-color`（红按钮和报错字段用
 * `--focus-ring-color-destructive`），定义在 src/app/globals.css。组件只决定环画在哪：默认外扩，
 * 输入类字段加 `ring-inset` 收进框内。
 *
 * 在 focus / focus-visible / focus-within（含 group- / peer- / has- 写法）变体下，拦自写的 ring 宽度、
 * ring 颜色、ring-offset 和 outline 样式；放行三个令牌写法，以及 ring-inset、ring-0、outline-none、outline-hidden。
 * 不在焦点变体下的 ring（选中态、装饰描边）不归本规则管。
 *
 * 为什么焊进 lint：焦点环的宽度和颜色原先散在各共享件和业务代码里，3px 半透明、2px 实色、1px、外扩加间隙各写各的。
 * Meta astryx 的焦点偏移也曾在 1px / 2px / 3px 之间漂，最后靠令牌加 lint 收口（docs/ecosystem-adoption-guide.md）。
 * 存量记在 eslint-suppressions.json，只降不升。详见 docs/ui-design-principles.md §8「焦点」。
 */

/** 焦点环令牌写法：宽度、颜色、红色变体。 */
export const FOCUS_RING_TOKENS = Object.freeze([
  'ring-(length:--focus-ring-width)',
  'ring-(--focus-ring-color)',
  'ring-(--focus-ring-color-destructive)',
])
const ALLOWED = new Set([...FOCUS_RING_TOKENS, 'ring-inset', 'ring-0', 'outline-none', 'outline-hidden'])
const FOCUS_VARIANT = /^(?:(?:group|peer)-)?focus(?:-visible|-within)?(?:\/[\w-]+)?$|^has-\[:focus(?:-visible|-within)?\]$/
const RING_OR_OUTLINE = /^(?:ring|inset-ring|outline)(?:-|$)/

/** 按顶层冒号拆出变体链与工具类；方括号、圆括号里的冒号（`ring-(length:…)`、`has-[:focus]`）不拆。 */
function splitVariants(token) {
  const parts = []
  let depth = 0
  let start = 0
  for (let i = 0; i < token.length; i++) {
    const ch = token[i]
    if (ch === '[' || ch === '(') depth++
    else if (ch === ']' || ch === ')') depth--
    else if (ch === ':' && depth === 0) {
      parts.push(token.slice(start, i))
      start = i + 1
    }
  }
  parts.push(token.slice(start))
  return parts
}

/** 返回字符串里自写的焦点环类（去重）。导出给单测用。 */
export function findCustomFocusRings(text) {
  const hits = new Set()
  for (const token of text.split(/\s+/)) {
    if (!token.includes(':')) continue
    const parts = splitVariants(token)
    const utility = parts.pop().replace(/^!|!$/g, '').replace(/^-/, '')
    if (!RING_OR_OUTLINE.test(utility) || ALLOWED.has(utility)) continue
    if (parts.some((variant) => FOCUS_VARIANT.test(variant))) hits.add(token)
  }
  return [...hits]
}

const rule = {
  meta: {
    type: 'problem',
    docs: { description: '焦点环的宽度和颜色只读 --focus-ring-* 令牌，组件只决定外扩还是 ring-inset' },
    messages: {
      custom: '焦点环 {{classes}} 自写了宽度、颜色或样式。改读令牌：focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)，输入类字段再加 ring-inset。见 docs/ui-design-principles.md §8。',
    },
    schema: [],
  },
  create(context) {
    function check(node, text) {
      if (typeof text !== 'string' || !text.includes(':')) return
      const hits = findCustomFocusRings(text)
      if (hits.length) context.report({ node, messageId: 'custom', data: { classes: hits.join(' ') } })
    }
    return {
      Literal(node) { check(node, node.value) },
      TemplateElement(node) { check(node, node.value.cooked ?? node.value.raw) },
    }
  },
}

const plugin = { rules: { 'focus-ring-token': rule } }

export default plugin
