/**
 * 禁用浏览器原生对话框与原生日期/下拉控件（自动拦截）
 *
 * - `window.confirm/alert/prompt`：样式和语言不跟平台走，也写不清对象和后果。
 *   用 `ConfirmDialog` / `DeleteConfirm`，结果提示用 toast。
 * - `<select>`：用 `Select`（固定枚举）或 `Combobox`（库表数据、可搜索）。
 * - `<input type="date|datetime-local|month">`：用 `DatePicker` / `DateTimePicker` / `MonthPicker`。
 *   `type="time"` 平台暂无替代控件，不在范围内。
 *
 * 为什么焊进 lint：2026-11 审计批次 3/4 已把存量清零（办公用品、报关详情、计划模板、样品对账），
 * 只靠规范条文会回潮。详见 docs/ui-design-principles.md §1.3、§5 与 docs/ui-governance-blueprint.md。
 */

const DIALOG_METHODS = new Set(['confirm', 'alert', 'prompt'])
const DATE_TYPES = new Set(['date', 'datetime-local', 'month'])

const rule = {
  meta: {
    type: 'problem',
    docs: { description: '禁用 window.confirm/alert/prompt、原生 <select> 与原生日期输入' },
    messages: {
      dialog: '不要用 window.{{name}}：用 ConfirmDialog / DeleteConfirm 写清对象和后果，结果提示用 toast。见 docs/ui-design-principles.md §5。',
      select: '不要用原生 <select>：固定枚举用 Select，库表数据用 Combobox。见 .claude/rules/frontend-controls.md。',
      date: '不要用原生 <input type="{{type}}">：用 DatePicker / DateTimePicker / MonthPicker。见 .claude/rules/frontend-controls.md。',
    },
    schema: [],
  },
  create(context) {
    return {
      CallExpression(node) {
        const callee = node.callee
        if (callee.type !== 'MemberExpression' || callee.computed) return
        if (callee.object.type !== 'Identifier' || callee.object.name !== 'window') return
        if (callee.property.type !== 'Identifier' || !DIALOG_METHODS.has(callee.property.name)) return
        context.report({ node, messageId: 'dialog', data: { name: callee.property.name } })
      },
      JSXOpeningElement(node) {
        if (node.name.type !== 'JSXIdentifier') return
        if (node.name.name === 'select') {
          context.report({ node, messageId: 'select' })
          return
        }
        if (node.name.name !== 'input') return
        for (const a of node.attributes) {
          if (a.type !== 'JSXAttribute' || a.name?.name !== 'type' || a.value?.type !== 'Literal') continue
          if (DATE_TYPES.has(a.value.value)) context.report({ node, messageId: 'date', data: { type: a.value.value } })
        }
      },
    }
  },
}

const plugin = { rules: { 'no-native-dialog-controls': rule } }

export default plugin
