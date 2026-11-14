/**
 * 页头操作区预算守卫（自动拦截）
 *
 * 规范（docs/ui-design-principles.md §1.1、§1.2；docs/ui-governance-blueprint.md §3.2）：
 * 1. 页头可见文字按钮 ≤ 3 个，第 4 个起收进 `moreActions` 或 `DropdownButton`「更多」；
 * 2. 页头主按钮（`CreateButton` 或默认 `variant` 的 `Button`）≤ 1 个，其余用 `variant="outline"`。
 *
 * 纯图标按钮（`IconButton`、`*GuideButton`、`size="icon*"`）与 `DropdownButton` 内部菜单项不计入文字按钮额度；
 * 互斥三元分支按单渲染态取最大值。
 */

import { countButtonsInNode, isTruthySlotAttr, jsxAttr, jsxTagName } from './jsx-ui-helpers.mjs'

export const MAX_HEADER_TEXT_BUTTONS = 3
export const MAX_HEADER_PRIMARY_BUTTONS = 1

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description: 'PageHeader 可见文字按钮不超过 3 个、主按钮不超过 1 个；低频动作收进 moreActions / DropdownButton',
    },
    messages: {
      tooManyButtons:
        'PageHeader 有 {{count}} 个可见文字按钮（上限 {{max}} 个）：请使用 primaryAction + secondaryActions（≤2）并将低频动作收进 moreActions。详见 docs/ui-design-principles.md §1.2。',
      multiplePrimary:
        'PageHeader 有 {{count}} 个主按钮（上限 1 个）：页头只保留一个主操作（primaryAction），其余次级操作请使用 variant="outline"。详见 docs/ui-design-principles.md §1.1。',
    },
    schema: [],
  },
  create(context) {
    return {
      JSXElement(node) {
        if (jsxTagName(node) !== 'PageHeader') return

        let textButtons = 0
        let primaryButtons = 0
        for (const slot of ['action', 'extra', 'primaryAction', 'secondaryActions']) {
          const attrNode = jsxAttr(node, slot)
          if (!attrNode) continue
          const counts = countButtonsInNode(attrNode)
          textButtons += counts.text
          primaryButtons += counts.primary
        }
        if (isTruthySlotAttr(jsxAttr(node, 'moreActions'))) {
          textButtons += 1
        }

        if (textButtons > MAX_HEADER_TEXT_BUTTONS) {
          context.report({
            node: node.openingElement,
            messageId: 'tooManyButtons',
            data: { count: String(textButtons), max: String(MAX_HEADER_TEXT_BUTTONS) },
          })
        }
        if (primaryButtons > MAX_HEADER_PRIMARY_BUTTONS) {
          context.report({
            node: node.openingElement,
            messageId: 'multiplePrimary',
            data: { count: String(primaryButtons) },
          })
        }
      },
    }
  },
}

const plugin = { rules: { 'page-header-actions': rule } }

export default plugin
