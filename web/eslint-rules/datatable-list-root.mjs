/**
 * 列表页根容器铁律（自动拦截）
 *
 * 渲染 <DataTable> 的「返回根」必须是 fragment <>、<ListPageShell>、或带 flex+flex-col
 * 的容器（如 className="flex min-h-0 flex-1 flex-col"）。用 <div className="space-y-*">
 * 或裸 <div> 当根，会断开从 dashboard layout 下来的 flex 高度链，DataTable 的
 * flex-1/min-h-0 算不出有界高度 → 撑满所有行不内滚 → 分页沉底 / 列表溢出页面。
 * 数据矮时不显形、变长才爆，反复复现，故用 lint 硬拦。
 *
 * 详见 web/AGENTS.md「列表页根容器铁律」。
 *
 * 实现要点：只校验「真正包住 <DataTable> 的那个返回根」——从 DataTable 元素所在的返回根
 * 出发判断，因此子组件（弹窗/Field 等）里用 space-y 不会误报；fragment 根天然合规。
 */

function jsxTagName(node) {
  if (!node || node.type !== 'JSXElement') return null
  const n = node.openingElement.name
  return n.type === 'JSXIdentifier' ? n.name : null
}

// 该 JSX 子树里是否含 <DataTable>（含条件/逻辑渲染与表达式容器，深度有界防失控）。
function containsDataTable(node, depth = 0) {
  if (!node || depth > 40) return false
  switch (node.type) {
    case 'JSXElement':
      if (jsxTagName(node) === 'DataTable') return true
      return node.children.some((c) => containsDataTable(c, depth + 1))
    case 'JSXFragment':
      return node.children.some((c) => containsDataTable(c, depth + 1))
    case 'JSXExpressionContainer':
      return containsDataTable(node.expression, depth + 1)
    case 'ConditionalExpression':
      return containsDataTable(node.consequent, depth + 1) || containsDataTable(node.alternate, depth + 1)
    case 'LogicalExpression':
      return containsDataTable(node.left, depth + 1) || containsDataTable(node.right, depth + 1)
    default:
      return false
  }
}

// node 是否处于「被 return / 箭头函数体」的根位置（含三元/逻辑分支被返回的情形）。
function isReturnedRoot(node) {
  const p = node.parent
  if (!p) return false
  if (p.type === 'ReturnStatement' && p.argument === node) return true
  if (p.type === 'ArrowFunctionExpression' && p.body === node) return true
  if (p.type === 'ConditionalExpression' || p.type === 'LogicalExpression') return isReturnedRoot(p)
  return false
}

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description:
        '渲染 DataTable 的列表页返回根必须维持 flex 高度链（fragment / flex-col 容器 / ListPageShell），禁止 space-y-* 或裸 div',
    },
    messages: {
      badRoot:
        '列表页根容器断开了 flex 高度链：渲染 <DataTable> 的返回根必须是 fragment <>、<ListPageShell>，或含 flex+flex-col 的容器（如 className="flex min-h-0 flex-1 flex-col"）。用 space-y-* 或裸 <div> 会让表格撑满内容高、分页沉底/列表溢出页面（数据矮时不显形，变长才爆）。详见 web/AGENTS.md「列表页根容器铁律」。',
    },
    schema: [],
  },
  create(context) {
    const sourceCode = context.sourceCode ?? context.getSourceCode()

    function check(node) {
      if (!isReturnedRoot(node) || !containsDataTable(node)) return

      const tag = jsxTagName(node)
      // 根就是 <DataTable> 自身（无包裹）或 <ListPageShell> —— 合规。
      if (tag === 'DataTable' || tag === 'ListPageShell') return

      const classAttr = node.openingElement.attributes.find(
        (a) => a.type === 'JSXAttribute' && a.name && a.name.name === 'className',
      )
      const clsText = classAttr && classAttr.value ? sourceCode.getText(classAttr.value) : ''
      const hasFlexCol = /\bflex\b/.test(clsText) && /\bflex-col\b/.test(clsText)
      if (hasFlexCol) return

      context.report({ node: node.openingElement, messageId: 'badRoot' })
    }

    // 只需校验 JSXElement 根；fragment 根天然合规，无需监听。
    return { JSXElement: check }
  },
}

const plugin = { rules: { 'datatable-list-root': rule } }

export default plugin
