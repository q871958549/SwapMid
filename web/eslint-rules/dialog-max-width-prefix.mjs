/**
 * 弹窗宽度类必须带响应式前缀（自动拦截）
 *
 * `DialogContent` / `SheetContent` 的基类里自带 `sm:max-w-lg` / `sm:max-w-sm`。若调用方写**裸**
 * `max-w-5xl`（无 `sm:` 前缀），二者在 twMerge 里属**不同分组**、不会互相覆盖，两个类同时存在；
 * 级联后基类的响应式变体胜出，弹窗被压回 512px（Sheet 则被压窄）——**你写的宽度类等于没写**。
 *
 * 为什么焊进 lint：这个坑已经三犯（标签打印弹窗、对账批次详情弹窗），规约写进
 * `.claude/rules/frontend-controls.md` 也拦不住新代码——第三次是生产用户实测才发现的
 * （差异明细「说明」列被挤成一个汉字宽、文字竖排）。规则不会漂、文档会。
 *
 * 只拦 `max-w-*`：其余尺寸类（`max-h-*` / `w-*`）在基类里没有响应式变体，不存在同款冲突。
 * 模板字符串与变量拼接的 className 跳过（静态分析无法可靠判定），带 `{...spread}` 的同理。
 *
 * 详见 `.claude/rules/frontend-controls.md`「弹窗内嵌轻量表格规范」第 3 条。
 */

const GUARDED_COMPONENTS = new Set(['DialogContent', 'SheetContent', 'AlertDialogContent'])

/** 匹配裸 max-w-*（前面不能紧跟 `:`，那是 sm:/md:/lg: 等变体前缀）。 */
const BARE_MAX_W = /(^|\s)max-w-[a-z0-9[\]/.-]+/i

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description: 'DialogContent / SheetContent 的 max-w-* 必须带响应式前缀（如 sm:），否则被基类覆盖',
    },
    messages: {
      needPrefix:
        '弹窗宽度类 "{{cls}}" 缺少响应式前缀：它与 {{component}} 基类自带的 sm:max-w-* 在 twMerge 中属不同分组、双类并存，级联后基类胜出，弹窗会被压回默认宽度——这个类等于没写。改成 "sm:{{cls}}"。此坑已三犯，详见 .claude/rules/frontend-controls.md。',
    },
    schema: [],
  },
  create(context) {
    return {
      JSXOpeningElement(node) {
        if (node.name.type !== 'JSXIdentifier' || !GUARDED_COMPONENTS.has(node.name.name)) return

        for (const attr of node.attributes) {
          if (attr.type !== 'JSXAttribute' || !attr.name || attr.name.name !== 'className') continue
          // 只分析字面量 className；模板字符串 / cn(...) 调用等动态值无法可靠判定，跳过不误报
          if (!attr.value || attr.value.type !== 'Literal' || typeof attr.value.value !== 'string') continue

          const match = BARE_MAX_W.exec(attr.value.value)
          if (match) {
            context.report({
              node: attr,
              messageId: 'needPrefix',
              data: { cls: match[0].trim(), component: node.name.name },
            })
          }
        }
      },
    }
  },
}

const plugin = { rules: { 'dialog-max-width-prefix': rule } }

export default plugin
