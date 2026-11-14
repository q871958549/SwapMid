'use client'

import { useEffect, useRef } from 'react'
import { usePathname } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import {
  setPlatformUpdatingCopy,
  setPlatformUpdatingNotifier,
} from '@/lib/platform-updating'
import { shouldSilentVersionSwitch } from '@/lib/version-skew'

// 版本偏斜只在用户已经发起路由切换时，无感改走整页加载。
// 不弹窗、不在原页强制刷新，也不在切回标签页或接口失败时打断正在填写的内容。
let knownBuildId: string | null = null
let knownRelease = ''
let lastSwitchAt = 0
let switching = false

function rememberKnownVersion(buildId: string, release: string) {
  if (knownBuildId === null) knownBuildId = buildId
  if (!knownRelease && release) knownRelease = release
}

export function VersionWatcher({
  initialBuildId,
  initialRelease = '',
}: {
  initialBuildId: string
  initialRelease?: string
}) {
  const pathname = usePathname()
  const t = useTranslations('Common')
  const sawFirstPath = useRef(false)
  const title = t('platformUpdatingTitle')
  const description = t('platformUpdatingDescription')
  setPlatformUpdatingCopy({ title, description })
  setPlatformUpdatingNotifier(() => {
    toast({ title, description })
  })
  rememberKnownVersion(initialBuildId, initialRelease)

  useEffect(() => {
    if (!sawFirstPath.current) {
      sawFirstPath.current = true
      return
    }
    if (switching || knownBuildId === null || knownBuildId === 'dev') return
    const known = { buildId: knownBuildId, release: knownRelease }
    void (async () => {
      try {
        const response = await fetch('/build-meta', { cache: 'no-store' })
        if (!response.ok || switching) return
        const meta = await response.json() as { buildId?: string; release?: string }
        const now = Date.now()
        if (!shouldSilentVersionSwitch({
          known,
          meta,
          navigated: true,
          now,
          lastSwitchAt,
        })) return
        switching = true
        lastSwitchAt = now
        const nextUrl = window.location.pathname + window.location.search + window.location.hash
        window.location.replace(nextUrl)
      } catch {
        // 探针失败就留在当前页。下一次路由切换再看，不打断这次操作。
      }
    })()
  }, [pathname])

  return null
}
