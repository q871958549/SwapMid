'use client'

import { BACKGROUND_REQUEST_HEADER } from '@/lib/session-availability'
import { findNavDomainKeyByPath, findNavGroupByPath, findSidebarHighlightItem } from '@/lib/nav-utils'

/**
 * 业务域使用量度量（蓝图 P2-0）：进入导航页面时上报「域 / 组 / 项」，后端只写一条结构化日志进 Seq。
 * 只报导航项，不带查询参数；归属按主归属算。用侧栏高亮同款解析：项目详情这类不在导航里的子路由
 * 归到它点亮的父项（项目总览），否则这部分访问会整片漏算。
 */
export interface NavUsage {
  domain: string
  group: string
  href: string
}

export function navUsageOf(pathname: string): NavUsage | null {
  const item = findSidebarHighlightItem(pathname)
  if (!item) return null
  const domain = findNavDomainKeyByPath(item.href)
  const group = findNavGroupByPath(item.href)?.key
  return domain && group ? { domain, group, href: item.href } : null
}

// 同一页在短时间内重复触发（页签恢复、重渲染）只算一次
const DEDUPE_MS = 5_000
let last: { href: string; at: number } | null = null

export function reportNavUsage(pathname: string, now = Date.now()): void {
  const usage = navUsageOf(pathname)
  if (!usage) return
  if (last && last.href === usage.href && now - last.at < DEDUPE_MS) return
  last = { href: usage.href, at: now }
  // 度量是旁路：失败不提示、不重试，也不能拖慢导航
  void fetch('/api/proxy/nav-usage', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', [BACKGROUND_REQUEST_HEADER]: '1' },
    body: JSON.stringify(usage),
    keepalive: true,
  }).catch(() => {})
}
