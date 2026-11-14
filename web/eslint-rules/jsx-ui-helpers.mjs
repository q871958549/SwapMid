/**
 * UI 结构类 ESLint 规则共用的 ESTree JSX 静态分析辅助函数。
 *
 * 口径与 `scripts/ui-health.mjs` 保持一致：
 * - 互斥三元表达式 `cond ? branchA : branchB` 按分路取 `Math.max`，不把不同步骤的主按钮/占流块误累加；
 * - `IconButton`、`*GuideButton`、`size="icon*"` 归为纯图标按钮，不计入可见文字按钮额度；
 * - `DropdownButton` 的 `items` 收纳菜单项与内嵌弹层 `*Content` 不计入外层区域可见按钮额度。
 */

export const OVERLAY_TAG = /(Dialog|DialogContent|Sheet|SheetContent|Drawer|Popover|PopoverContent|AlertDialog|Confirm|Toaster)$/
export const OVERLAY_CONTENT_TAG = /^(DialogContent|SheetContent|PopoverContent|AlertDialogContent|DropdownMenuContent)$/
export const BUTTONISH_TAG = /(^Button$|Button$|^ReportGuide$)/
export const ICON_BUTTON_TAG = /^(IconButton|\w+GuideButton)$/

export function jsxTagName(node) {
  if (!node || node.type !== 'JSXElement') return null
  const nameNode = node.openingElement?.name
  if (!nameNode) return null
  if (nameNode.type === 'JSXIdentifier') return nameNode.name
  if (nameNode.type === 'JSXMemberExpression' && nameNode.object.type === 'JSXIdentifier' && nameNode.property.type === 'JSXIdentifier') {
    return `${nameNode.object.name}.${nameNode.property.name}`
  }
  return null
}

export function jsxAttr(node, attrName) {
  if (!node || node.type !== 'JSXElement') return null
  return node.openingElement.attributes.find(
    (a) => a.type === 'JSXAttribute' && a.name?.type === 'JSXIdentifier' && a.name.name === attrName,
  ) ?? null
}

export function jsxAttrString(node, attrName) {
  const a = jsxAttr(node, attrName)
  if (!a) return null
  if (!a.value) return ''
  if (a.value.type === 'Literal') return typeof a.value.value === 'string' ? a.value.value : '{expr}'
  if (a.value.type === 'JSXExpressionContainer') {
    const expr = a.value.expression
    if (expr.type === 'Literal' && typeof expr.value === 'string') return expr.value
    if (expr.type === 'TemplateLiteral' && expr.expressions.length === 0) {
      return expr.quasis[0]?.value?.cooked ?? expr.quasis[0]?.value?.raw ?? ''
    }
  }
  return '{expr}'
}

export function isTruthySlotAttr(attrNode) {
  if (!attrNode) return false
  if (!attrNode.value) return true
  if (attrNode.value.type === 'JSXExpressionContainer') {
    const expr = attrNode.value.expression
    if (expr.type === 'JSXEmptyExpression') return false
    if (expr.type === 'Literal' && (expr.value === null || expr.value === false)) return false
    if (expr.type === 'Identifier' && expr.name === 'undefined') return false
  }
  return true
}

function addCounts(a, b) {
  return { text: a.text + b.text, icon: a.icon + b.icon, primary: a.primary + b.primary }
}

function maxCounts(a, b) {
  return {
    text: Math.max(a.text, b.text),
    icon: Math.max(a.icon, b.icon),
    primary: Math.max(a.primary, b.primary),
  }
}

const ZERO = Object.freeze({ text: 0, icon: 0, primary: 0 })

/**
 * 统计 AST 子树在同一渲染态下可见的文字按钮、图标按钮与主按钮数量。
 * `allowRootOverlayContent` 为 true 时允许遍历根自身（例如根就是 ScrollableDialogContent 的 footer 属性）。
 */
export function countButtonsInNode(node, depth = 0, allowRootOverlayContent = false) {
  if (!node || depth > 80) return ZERO

  switch (node.type) {
    case 'JSXText':
    case 'JSXEmptyExpression':
    case 'Literal':
    case 'Identifier':
      return ZERO
    case 'JSXAttribute':
      return countButtonsInNode(node.value, depth + 1, true)
    case 'JSXExpressionContainer':
      return countButtonsInNode(node.expression, depth + 1)
    case 'ChainExpression':
    case 'TSAsExpression':
    case 'TSTypeAssertion':
    case 'TSNonNullExpression':
      return countButtonsInNode(node.expression, depth + 1)
    case 'SequenceExpression':
      return countButtonsInNode(node.expressions.at(-1), depth + 1)
    case 'ConditionalExpression':
      return maxCounts(
        countButtonsInNode(node.consequent, depth + 1),
        countButtonsInNode(node.alternate, depth + 1),
      )
    case 'LogicalExpression':
      if (node.operator === '&&') return countButtonsInNode(node.right, depth + 1)
      return maxCounts(
        countButtonsInNode(node.left, depth + 1),
        countButtonsInNode(node.right, depth + 1),
      )
    case 'ArrayExpression': {
      let acc = ZERO
      for (const el of node.elements) {
        if (el) acc = addCounts(acc, countButtonsInNode(el, depth + 1))
      }
      return acc
    }
    case 'JSXFragment': {
      let acc = ZERO
      for (const child of node.children) {
        acc = addCounts(acc, countButtonsInNode(child, depth + 1))
      }
      return acc
    }
    case 'JSXElement': {
      const tag = jsxTagName(node)
      if (!allowRootOverlayContent && tag && OVERLAY_CONTENT_TAG.test(tag)) {
        return ZERO
      }
      let acc = ZERO
      if (tag && BUTTONISH_TAG.test(tag)) {
        const size = jsxAttrString(node, 'size')
        const variant = jsxAttrString(node, 'variant')
        const icon = ICON_BUTTON_TAG.test(tag) || size === 'icon' || /^icon/.test(size ?? '')
        const primary = !icon && (tag === 'CreateButton' || (tag === 'Button' && (variant === null || variant === 'default')))
        acc = {
          text: icon ? 0 : 1,
          icon: icon ? 1 : 0,
          primary: primary ? 1 : 0,
        }
        // DropdownButton 的 items 是收纳菜单，不计入外层可见按钮额度
        if (tag === 'DropdownButton') return acc
      }
      for (const child of node.children) {
        acc = addCounts(acc, countButtonsInNode(child, depth + 1))
      }
      return acc
    }
    default:
      return ZERO
  }
}

/** 子树中是否包含指定标签正则的 JSX 元素（不跨入弹层内容）。 */
export function containsJsxTag(node, tagRegex, depth = 0) {
  if (!node || depth > 60) return false
  switch (node.type) {
    case 'JSXElement': {
      const tag = jsxTagName(node)
      if (tag && tagRegex.test(tag)) return true
      if (tag && OVERLAY_TAG.test(tag)) return false
      return node.children.some((c) => containsJsxTag(c, tagRegex, depth + 1))
    }
    case 'JSXFragment':
      return node.children.some((c) => containsJsxTag(c, tagRegex, depth + 1))
    case 'JSXExpressionContainer':
      return containsJsxTag(node.expression, tagRegex, depth + 1)
    case 'ConditionalExpression':
      return (
        containsJsxTag(node.consequent, tagRegex, depth + 1) ||
        containsJsxTag(node.alternate, tagRegex, depth + 1)
      )
    case 'LogicalExpression':
      return (
        containsJsxTag(node.left, tagRegex, depth + 1) ||
        containsJsxTag(node.right, tagRegex, depth + 1)
      )
    case 'ChainExpression':
    case 'TSAsExpression':
    case 'TSTypeAssertion':
    case 'TSNonNullExpression':
      return containsJsxTag(node.expression, tagRegex, depth + 1)
    default:
      return false
  }
}

/** 节点是否在桌面端被 `sm:hidden` / `md:hidden` / `lg:hidden` 隐藏（窄屏卡片替代视图不挤占桌面表格高度）。 */
function isDesktopHidden(node) {
  const cls = jsxAttrString(node, 'className')
  if (!cls || cls === '{expr}') return false
  return /(?:^|\s)(?:sm|md|lg):hidden(?:\s|$)/.test(cls)
}

/** 计算单个子节点或表达式在表格前占用的文档流块数（互斥三元取分路最大值）。 */
export function countAboveTableFlowBlocks(node, depth = 0) {
  if (!node || depth > 40) return 0
  switch (node.type) {
    case 'JSXText':
      return node.value.trim() ? 1 : 0
    case 'JSXEmptyExpression':
      return 0
    case 'Literal':
      return node.value === null || node.value === false || node.value === '' ? 0 : 1
    case 'Identifier':
      return node.name === 'undefined' ? 0 : 1
    case 'JSXExpressionContainer':
      return countAboveTableFlowBlocks(node.expression, depth + 1)
    case 'ConditionalExpression':
      return Math.max(
        countAboveTableFlowBlocks(node.consequent, depth + 1),
        countAboveTableFlowBlocks(node.alternate, depth + 1),
      )
    case 'LogicalExpression':
      if (node.operator === '&&') return countAboveTableFlowBlocks(node.right, depth + 1)
      return Math.max(
        countAboveTableFlowBlocks(node.left, depth + 1),
        countAboveTableFlowBlocks(node.right, depth + 1),
      )
    case 'JSXFragment': {
      let sum = 0
      for (const child of node.children) sum += countAboveTableFlowBlocks(child, depth + 1)
      return sum
    }
    case 'JSXElement': {
      const tag = jsxTagName(node)
      if (!tag || tag === 'PageHeader' || OVERLAY_TAG.test(tag) || isDesktopHidden(node)) return 0
      return 1
    }
    default:
      return 1
  }
}
