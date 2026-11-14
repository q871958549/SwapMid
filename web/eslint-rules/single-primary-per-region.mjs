/**
 * 单区域单主按钮守卫（自动拦截）
 *
 * 规范（docs/ui-design-principles.md §1.1；docs/ui-governance-blueprint.md §3.2）：
 * 对话框页脚（`DialogFooter`、`SheetFooter`、`AlertDialogFooter`、弹层 `footer` 插槽）
 * 及表格批量操作栏（`DataTable` 的 `bulkActions`）在同一渲染态下最多包含 1 个主按钮（默认 `variant` 的 `Button` 或 `CreateButton`）。
 * 页头主按钮由 `swapmid/page-header-actions` 检查，此处不重复报告。
 *
 * 互斥三元分支（如多步向导 `step === 1 ? <Button>下一步</Button> : <Button>提交</Button>`）按分路取最大值，不误判。
 */

import { countButtonsInNode, jsxTagName } from './jsx-ui-helpers.mjs'

const FOOTER_TAGS = new Set(['DialogFooter', 'SheetFooter', 'AlertDialogFooter'])
const FOOTER_SLOT_OWNERS = /(DialogContent|SheetContent|CrudFormDialog)$/

function extractFunctionReturnedNode(expr) {
  if (!expr) return null
  if (expr.type === 'ArrowFunctionExpression' || expr.type === 'FunctionExpression') {
    if (expr.body.type !== 'BlockStatement') return expr.body
    for (const stmt of expr.body.body) {
      if (stmt.type === 'ReturnStatement' && stmt.argument) return stmt.argument
    }
  }
  return null
}

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description: 'DialogFooter / SheetFooter / AlertDialogFooter / 弹层 footer 插槽 / DataTable bulkActions 内同一渲染态主按钮不超过 1 个',
    },
    messages: {
      multiplePrimary:
        '{{region}} 内同一渲染态出现 {{count}} 个主按钮（上限 1 个）：只保留最重要的一项为主按钮，其余请改为 variant="outline"、"secondary" 或 "destructive-outline"。详见 docs/ui-design-principles.md §1.1。',
    },
    schema: [],
  },
  create(context) {
    return {
      JSXElement(node) {
        const tag = jsxTagName(node)
        if (!tag || !FOOTER_TAGS.has(tag)) return
        const counts = countButtonsInNode(node, 0, true)
        if (counts.primary > 1) {
          context.report({
            node: node.openingElement,
            messageId: 'multiplePrimary',
            data: { region: tag, count: String(counts.primary) },
          })
        }
      },
      JSXAttribute(node) {
        if (!node.name || node.name.type !== 'JSXIdentifier') return
        const parentOpening = node.parent
        const ownerName = parentOpening?.type === 'JSXOpeningElement' && parentOpening.name.type === 'JSXIdentifier'
          ? parentOpening.name.name
          : null
        if (!ownerName) return

        if (node.name.name === 'footer' && FOOTER_SLOT_OWNERS.test(ownerName)) {
          const counts = countButtonsInNode(node, 0, true)
          if (counts.primary > 1) {
            context.report({
              node,
              messageId: 'multiplePrimary',
              data: { region: `${ownerName}.footer`, count: String(counts.primary) },
            })
          }
        }

        if (node.name.name === 'bulkActions' && ownerName === 'DataTable' && node.value?.type === 'JSXExpressionContainer') {
          const bodyNode = extractFunctionReturnedNode(node.value.expression)
          if (!bodyNode) return
          const counts = countButtonsInNode(bodyNode, 0, true)
          if (counts.primary > 1) {
            context.report({
              node,
              messageId: 'multiplePrimary',
              data: { region: 'DataTable.bulkActions', count: String(counts.primary) },
            })
          }
        }
      },
    }
  },
}

const plugin = { rules: { 'single-primary-per-region': rule } }

export default plugin
