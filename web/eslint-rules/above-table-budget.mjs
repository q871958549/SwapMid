/**
 * 表格上方常驻块预算守卫（自动拦截）
 *
 * 规范（docs/ui-design-principles.md §2.1；docs/ui-governance-blueprint.md §3.2）：
 * 列表 / 报表页必须确保 1366×768 首屏能看见表头与数据行。
 * 包住 `<DataTable>` 的非弹层容器里，在 `<DataTable>` 之前最多只允许 2 个占文档流的常驻块
 * （即 1 行筛选条 `FilterBar` + 1 块有界汇总面板；推荐直接使用 `<ListPageShell>` 的具名插槽 `header` / `filters` / `notice` / `summary`）。
 * 口径说明、群通知话术、开关配置组应收入 `PageHeader.help` 或 `Dialog` / `Sheet`。
 */

import { containsJsxTag, countAboveTableFlowBlocks, jsxTagName, OVERLAY_TAG } from './jsx-ui-helpers.mjs'

export const MAX_ABOVE_TABLE_BLOCKS = 2

function isInsideOverlay(node) {
  let cur = node.parent
  while (cur) {
    if (cur.type === 'JSXElement') {
      const tag = jsxTagName(cur)
      if (tag && OVERLAY_TAG.test(tag)) return true
    }
    cur = cur.parent
  }
  return false
}

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description: '非弹层列表容器在 DataTable 之前的常驻占流块不超过 2 个（推荐使用 ListPageShell 插槽）',
    },
    messages: {
      overBudget:
        'DataTable 上方堆叠了 {{count}} 个常驻块（上限 {{max}} 个：一行筛选 + 一块有界汇总）：请迁移至 <ListPageShell> 插槽（header / filters / notice / summary），并将口径说明或配置面板收进 PageHeader.help 或 Dialog / Sheet，避免挤占首屏表格高度。详见 docs/ui-design-principles.md §2.1。',
    },
    schema: [],
  },
  create(context) {
    function checkContainer(node) {
      const tag = node.type === 'JSXElement' ? jsxTagName(node) : null
      if (tag === 'DataTable' || (tag && OVERLAY_TAG.test(tag))) return
      if (isInsideOverlay(node)) return

      const children = node.children ?? []
      const tableChildIdx = children.findIndex((child) => containsJsxTag(child, /^DataTable$/))
      if (tableChildIdx <= 0) return

      let blocks = 0
      for (const sibling of children.slice(0, tableChildIdx)) {
        blocks += countAboveTableFlowBlocks(sibling)
      }

      if (blocks > MAX_ABOVE_TABLE_BLOCKS) {
        context.report({
          node: node.type === 'JSXElement' ? node.openingElement : node,
          messageId: 'overBudget',
          data: { count: String(blocks), max: String(MAX_ABOVE_TABLE_BLOCKS) },
        })
      }
    }

    return {
      JSXElement: checkContainer,
      JSXFragment: checkContainer,
    }
  },
}

const plugin = { rules: { 'above-table-budget': rule } }

export default plugin
