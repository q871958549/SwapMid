import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { PageShellSkeleton } from '@/components/data-table/page-shell-skeleton'
import { Suspense } from 'react'
import { getTranslations } from 'next-intl/server'
import { apiFetch } from '@/lib/api-client'
import { SystemHealthClient, type SystemHealthDto } from './client'

// 实时查 Prometheus 翻译健康状态，不缓存。
export const dynamic = 'force-dynamic'

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function SystemHealthPage() {
  const title = await resolvePageShellTitle('/system/health')
  return (
    <Suspense fallback={<PageShellSkeleton title={title} />}>
      <SystemHealthPageData />
    </Suspense>
  )
}

async function SystemHealthPageData() {
  const t = await getTranslations('System.health')
  // 后端已对 Prometheus 连不上做了「未知」降级；这里再兜一层：整个请求失败时给一个全「未知」对象，避免页面崩。
  const data = await apiFetch<SystemHealthDto>('/system-health').catch((e) => {
    console.error('[system-health]', e)
    return {
      overallStatus: 3,
      overallConclusion: t('fetchFailedFallbackConclusion'),
      checkedAt: new Date().toISOString(),
      items: [],
    } satisfies SystemHealthDto
  })

  return <SystemHealthClient initial={data} />
}
