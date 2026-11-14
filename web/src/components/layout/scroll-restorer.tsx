'use client'

import { useEffect, useRef } from 'react'
import { usePathname } from 'next/navigation'
import { useAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import {
  authenticatedBrowserStoragePolicies,
  type AuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'

// P1 滚动位置恢复（见 docs/page-switch-keepalive-solution.md §四）：dashboard 布局的滚动容器是
// 单一共享 <main>（切页只换 children，容器本身不重新挂载），浏览器原生并不会替我们记住各路径的
// scrollTop，需要手动记录 / 恢复。
//
// 记录：main 上挂节流（~150ms）scroll 监听，写当前 identity 的 sessionStorage envelope；
// 与页签持久化同生命周期，F5 后仍可恢复，不同账号/模拟身份互不复用。
// 恢复：usePathname 变化后读存量值，用 requestAnimationFrame 双帧设置 main.scrollTop
// （内容异步渲染、高度未就绪时设置会被浏览器钳制到当前可滚动范围，双帧尽力而为；找不到存量则
// 显式置 0，保持现状行为）。
//
// 竞态：程序化设置 scrollTop 本身会触发 scroll 事件，若不加防护，记录逻辑会把「恢复过程中因高度
// 未就绪被钳制的中间值」当成用户滚动写回 sessionStorage，污染掉本该保留的目标值。恢复期间的
// scroll 事件必须完全忽略（既不更新 pending，也不起 timer）；双帧结束时再清一次残留 pending/timer，
// 防止「双 rAF 已完成、旧 150ms timer 才到」后越过 restoringRef 屏障。
const THROTTLE_MS = 150
const MAX_ENTRIES = 100

interface ScrollPosition {
  top: number
  touchedAt: number
}

type ScrollMap = Record<string, ScrollPosition>

function isScrollMap(value: unknown): value is ScrollMap {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  const entries = Object.entries(value)
  if (entries.length > MAX_ENTRIES) return false
  return entries.every(([pathname, position]) => {
    if (!pathname.startsWith('/') || pathname.length > 2_048) return false
    if (typeof position !== 'object' || position === null || Array.isArray(position)) return false
    const candidate = position as Partial<ScrollPosition>
    return typeof candidate.top === 'number'
      && Number.isFinite(candidate.top)
      && candidate.top >= 0
      && typeof candidate.touchedAt === 'number'
      && Number.isFinite(candidate.touchedAt)
      && candidate.touchedAt > 0
  })
}

function readMap(storage: AuthenticatedBrowserStorage): ScrollMap {
  return storage.read(
    authenticatedBrowserStoragePolicies.scroll,
    isScrollMap,
  ) ?? {}
}

function writePosition(
  storage: AuthenticatedBrowserStorage,
  map: ScrollMap,
  pathname: string,
  top: number,
): void {
  map[pathname] = { top: Math.max(0, top), touchedAt: Date.now() }

  const keys = Object.keys(map)
  if (keys.length > MAX_ENTRIES) {
    keys
      .sort((a, b) => map[a].touchedAt - map[b].touchedAt)
      .slice(0, keys.length - MAX_ENTRIES)
      .forEach((key) => delete map[key])
  }

  storage.write(authenticatedBrowserStoragePolicies.scroll, map)
}

export function ScrollRestorer() {
  const pathname = usePathname()
  const browserStorage = useAuthenticatedBrowserStorage()
  const restoringRef = useRef(false)

  useEffect(() => {
    const main = document.querySelector<HTMLElement>('[data-scroll-container]')
    if (!main) return undefined

    let throttleTimer: number | null = null
    let pendingScrollTop: number | null = null

    const flush = () => {
      throttleTimer = null
      if (restoringRef.current) {
        pendingScrollTop = null
        return
      }
      if (pendingScrollTop === null) return
      const top = pendingScrollTop
      pendingScrollTop = null
      writePosition(browserStorage, readMap(browserStorage), pathname, top)
    }

    const handleScroll = () => {
      if (restoringRef.current) return
      pendingScrollTop = main.scrollTop
      if (throttleTimer !== null) return
      throttleTimer = window.setTimeout(flush, THROTTLE_MS)
    }

    main.addEventListener('scroll', handleScroll, { passive: true })

    // 进入新路径：先置抑制标记，再尝试恢复存量 scrollTop（无存量则置 0，保持现状行为）。
    restoringRef.current = true
    const saved = readMap(browserStorage)[pathname]
    const target = typeof saved?.top === 'number' ? saved.top : 0
    main.scrollTop = target

    let cleanupRaf2: (() => void) | undefined
    const raf1 = requestAnimationFrame(() => {
      main.scrollTop = target
      const raf2 = requestAnimationFrame(() => {
        main.scrollTop = target
        if (throttleTimer !== null) {
          window.clearTimeout(throttleTimer)
          throttleTimer = null
        }
        pendingScrollTop = null
        restoringRef.current = false
      })
      cleanupRaf2 = () => cancelAnimationFrame(raf2)
    })

    return () => {
      main.removeEventListener('scroll', handleScroll)
      if (throttleTimer !== null) window.clearTimeout(throttleTimer)
      cancelAnimationFrame(raf1)
      cleanupRaf2?.()
      // 卸载前（路由再次切换）把节流窗口内尚未落盘的最新滚动位置补写一次，
      // 避免「滚动后立刻切走」丢失最后一段还没到节流点的滚动。
      if (!restoringRef.current && pendingScrollTop !== null) {
        const top = pendingScrollTop
        pendingScrollTop = null
        writePosition(browserStorage, readMap(browserStorage), pathname, top)
      }
    }
  }, [browserStorage, pathname])

  return null
}
