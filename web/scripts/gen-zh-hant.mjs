// 兼容旧命令；完整三语同步统一由 sync-i18n.mjs 负责。
console.warn('[gen-zh-hant] 已并入统一生成器，正在执行 pnpm i18n:sync。')
await import('./sync-i18n.mjs')
