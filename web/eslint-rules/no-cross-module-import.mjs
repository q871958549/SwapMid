/**
 * 模块边界护栏（自动拦截）
 *
 * 禁止 `(dashboard)/{A}` 业务模块的文件直接 import `(dashboard)/{B}`（A≠B）的内部文件——
 * 跨业务模块复用一律下沉到共享层（`@/components`、`@/lib`、`@/stores`、`@/hooks`）。
 *
 * 为什么：23 个业务模块平铺在 `app/(dashboard)/` 下，靠目录约定隔离、编译器零强制。
 * 当前零跨模块耦合（很干净），但随模块膨胀 + 多人/多会话并行，迟早有人图省事直接
 * import 别模块的内部组件，耦合会像后端 EnterpriseManagement（11 域入边 + 循环依赖，
 * 见 ADR-0009）那样无序生长、越来越难逆转。**趁零违规时上锁，成本最低。**
 *
 * 同时覆盖 `@/app/(dashboard)/…` 别名 import 与相对路径 import，避免改写路径即可绕过护栏。
 * 同模块内 import 与下沉到 `@/components`、`@/lib` 等共享层的 import 不拦。
 *
 * 详见 web/AGENTS.md「模块边界」。
 */

import path from 'node:path'

// 从路径里取 (dashboard) 后的第一段模块名；不在 (dashboard) 下返回 null。
function moduleOf(p) {
  const m = String(p).replace(/\\/g, '/').match(/\/app\/\(dashboard\)\/([^/]+)/)
  return m ? m[1] : null
}

// 从 import 源取目标模块名：支持 `@/app/(dashboard)/X/...` 与相对路径。
function targetModuleOf(src, filename) {
  const normalizedSource = String(src).replace(/\\/g, '/')
  if (normalizedSource.startsWith('@/')) {
    const resolvedAlias = path.posix.normalize(`/src/${normalizedSource.slice(2)}`)
    return moduleOf(resolvedAlias)
  }

  if (!normalizedSource.startsWith('.')) return null

  const normalizedFilename = String(filename).replace(/\\/g, '/')
  const resolved = path.posix.normalize(`${path.posix.dirname(normalizedFilename)}/${normalizedSource}`)
  return moduleOf(resolved)
}

const rule = {
  meta: {
    type: 'problem',
    docs: {
      description:
        '禁止 (dashboard) 业务模块间深 import；跨模块复用请走 @/components 或 @/lib 共享层',
    },
    messages: {
      crossModule:
        '跨业务模块 import：本文件属 (dashboard)/{{from}} 模块，不应直接 import (dashboard)/{{to}} 的内部文件。跨模块复用请把公共部分下沉到 @/components 或 @/lib 共享层（保持模块自治，防耦合无序生长）。详见 web/AGENTS.md「模块边界」。',
    },
    schema: [],
  },
  create(context) {
    const filename = context.filename ?? context.getFilename()
    const mod = moduleOf(filename)
    if (!mod) return {} // 不在任何 (dashboard) 业务模块内（如 layout / 共享组件），不校验。

    function checkSource(sourceNode) {
      if (!sourceNode || typeof sourceNode.value !== 'string') return
      const target = targetModuleOf(sourceNode.value, filename)
      if (target && target !== mod) {
        context.report({ node: sourceNode, messageId: 'crossModule', data: { from: mod, to: target } })
      }
    }

    return {
      ImportDeclaration(node) {
        checkSource(node.source)
      },
      ExportNamedDeclaration(node) {
        checkSource(node.source)
      },
      ExportAllDeclaration(node) {
        checkSource(node.source)
      },
      ImportExpression(node) {
        checkSource(node.source)
      },
    }
  },
}

const plugin = { rules: { 'no-cross-module-import': rule } }

export default plugin
