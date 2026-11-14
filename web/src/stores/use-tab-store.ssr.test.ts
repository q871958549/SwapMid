// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { useTabStore } from './use-tab-store'

// 模拟 server bundle 环境（无 sessionStorage 的 Node）：TabBar 渲染期直读
// useTabStore.persist.onFinishHydration / hasHydrated（见 tab-bar.tsx 的水合标志），
// 而 zustand persist 遇 storage 缺失会不挂 persist API——一旦 storage 工厂的 server
// noop 兜底（见 use-tab-store.ts）被移除，带会话硬刷 dashboard 任意路由即 SSR 500
//（2026-07-20 事故；build 期因 layout force-dynamic 不预渲染，tsc/build 全程不显形）。
// 本文件钉死「server 端 persist API 恒存在」，是该事故的唯一回归网。
describe('use-tab-store（server 环境，无 sessionStorage）', () => {
  it('persist API 存在，TabBar 渲染期读取不炸', () => {
    // 前提自检：确实处于无 storage 的 node 环境（若未来 vitest/node 提供了 sessionStorage，
    // 本用例即失去意义，须换别的方式模拟 server bundle）
    expect(typeof sessionStorage).toBe('undefined')

    expect(useTabStore.persist).toBeDefined()
    expect(typeof useTabStore.persist.onFinishHydration).toBe('function')
    expect(typeof useTabStore.persist.hasHydrated).toBe('function')
  })

  // ⚠️ 本用例断言的是「渲染前」状态，须排在下方 rehydrate 用例之前（rehydrate 后标志会翻 true）。
  it('server 端水合标志渲染前为 false，与 TabBar 的 getServerHydratedSnapshot 一致', () => {
    expect(useTabStore.persist.hasHydrated()).toBe(false)
  })

  it('server 端 rehydrate 走 noop storage 不抛错、状态保持默认值', async () => {
    await useTabStore.persist.rehydrate() // 不抛即过（真实 SSR 不会调到，此为误调加固）
    expect(useTabStore.getState().tabs).toHaveLength(1)
    expect(useTabStore.getState().activeKey).toBe('/dashboard')
  })
})
