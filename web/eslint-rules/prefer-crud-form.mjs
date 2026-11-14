/**
 * 优先用 CRUD 共享件（引导，warn 级）
 *
 * 手写 `setFormError` 表明在手写「新建/编辑对话框停留型反馈」的样板（AGENTS「写操作反馈 5 点」之一），
 * 而 `useCrudForm` + `<CrudFormDialog>` 已把这 5 点全部内建。若文件手写了 `setFormError`
 * 却没引入 `useCrudForm`，提示改用共享件——既减样板，又统一范式、防止迁移成果回退。
 *
 * 精确锚点（低误报）：仅在「出现 setFormError 标识符」且「未 import use-crud-form」时提示。
 * 已迁页用 useCrudForm（不手写 formError）天然不报；本规则当前只命中尚未迁的 users 页。
 *
 * warn 级、可 `// eslint-disable-next-line swapmid/prefer-crud-form` 豁免：
 * 确有共享件不适配的特殊表单（如自定义多步 footer 向导）可豁免。
 * 详见 web/AGENTS.md「CRUD 共享件与文件复杂度」。
 */

const rule = {
  meta: {
    type: 'suggestion',
    docs: {
      description:
        '手写 setFormError 但未用 useCrudForm 时，引导改用 CRUD 共享件（useCrudForm + CrudFormDialog，写操作反馈 5 点已内建）',
    },
    messages: {
      preferCrudForm:
        '检测到手写 setFormError（新建/编辑对话框反馈样板），但未引入 useCrudForm。优先用 useCrudForm + <CrudFormDialog>（@/lib/crud/use-crud-form）——写操作反馈 5 点已内建，无需手写 formError。范本见 enterprise-management/{enterprises,positions,employees}。确有共享件不适配的特殊表单可对本行加 eslint-disable。详见 web/AGENTS.md「CRUD 共享件与文件复杂度」。',
    },
    schema: [],
  },
  create(context) {
    let importedCrudForm = false
    let firstFormErrorNode = null

    return {
      ImportDeclaration(node) {
        const src = node.source && node.source.value
        if (typeof src === 'string' && src.includes('crud/use-crud-form')) {
          importedCrudForm = true
        }
      },
      Identifier(node) {
        if (node.name === 'setFormError' && !firstFormErrorNode) {
          firstFormErrorNode = node
        }
      },
      'Program:exit'() {
        if (firstFormErrorNode && !importedCrudForm) {
          context.report({ node: firstFormErrorNode, messageId: 'preferCrudForm' })
        }
      },
    }
  },
}

const plugin = { rules: { 'prefer-crud-form': rule } }

export default plugin
