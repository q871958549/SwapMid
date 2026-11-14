/**
 * 图标按钮可访问名护栏（自动拦截）
 *
 * `<Button size="icon">` 是纯图标按钮（只含 <Icon/>、无文本），屏幕阅读器读到的是**空按钮**
 * （违反 WCAG 4.1.2 Name/Role/Value）。必须配可访问名：`aria-label`（或 `title`/`aria-labelledby`）。
 *
 * 为什么焊进 lint：列表页 ⋯ 操作菜单、编辑/删除/搜索等图标按钮遍布全站，靠人工记得加
 * aria-label 不可持续（本轮就补了一批遗漏）。规则不会漂、文档会——把它变成 CI 拦截。
 *
 * 聚焦 `size="icon"`（最明确的「纯图标按钮」信号）。带 `{...spread}` 的 Button 可能动态携带
 * aria-label，跳过不误报。size="sm"+p-0 等其它图标写法不在此规则范围（需要时再扩）。
 *
 * 详见 web/AGENTS.md「操作按钮无障碍」。
 */

const LABEL_ATTRS = new Set(['aria-label', 'aria-labelledby', 'title'])

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description: '图标按钮（<Button size="icon">）必须有可访问名（aria-label / title / aria-labelledby）',
    },
    messages: {
      needLabel:
        '图标按钮无可访问名：<Button size="icon"> 只有图标、屏幕阅读器读为空按钮，必须加 aria-label（或 title），如「操作」「编辑」「删除」。详见 web/AGENTS.md「操作按钮无障碍」。',
    },
    schema: [],
  },
  create(context) {
    return {
      JSXOpeningElement(node) {
        if (node.name.type !== 'JSXIdentifier' || node.name.name !== 'Button') return
        let sizeIcon = false
        let hasLabel = false
        let hasSpread = false
        for (const a of node.attributes) {
          if (a.type === 'JSXSpreadAttribute') { hasSpread = true; continue }
          if (a.type !== 'JSXAttribute' || !a.name) continue
          const name = a.name.name
          if (name === 'size' && a.value && a.value.type === 'Literal' && a.value.value === 'icon') sizeIcon = true
          if (LABEL_ATTRS.has(name)) hasLabel = true
        }
        // 有 {...spread} 时可能动态携带 aria-label，跳过避免误报。
        if (sizeIcon && !hasLabel && !hasSpread) {
          context.report({ node, messageId: 'needLabel' })
        }
      },
    }
  },
}

const plugin = { rules: { 'icon-button-label': rule } }

export default plugin
