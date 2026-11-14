'use client'

// 前端未捕获运行时错误与错误边界诊断归集（10s 去重 + 长度截断 + 绝不二次抛错）。
export interface ClientErrorReport {
  kind: string
  message?: string
  stack?: string
  digest?: string
  buildId?: string
}

const MAX_MESSAGE = 1000
const MAX_STACK = 4000
const MAX_URL = 500
const DEDUP_WINDOW_MS = 10_000
const lastReportAt = new Map<string, number>()

function truncate(value: string | undefined, max: number): string | undefined {
  if (!value) return value
  return value.length <= max ? value : value.slice(0, max)
}

export function reportClientError(report: ClientErrorReport): void {
  try {
    if (typeof window === 'undefined') return

    const dedupKey = `${report.kind}:${report.message ?? ''}`
    const now = Date.now()
    const last = lastReportAt.get(dedupKey)
    if (last !== undefined && now - last < DEDUP_WINDOW_MS) return
    lastReportAt.set(dedupKey, now)

    const payload = {
      kind: report.kind,
      message: truncate(report.message, MAX_MESSAGE),
      stack: truncate(report.stack, MAX_STACK),
      digest: report.digest,
      buildId: report.buildId,
      url: truncate(window.location.href, MAX_URL),
    }
    console.warn('[swapmid-client-error]', payload)
  } catch {
    // 错误上报本身绝不二次抛错
  }
}
