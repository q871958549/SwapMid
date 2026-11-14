import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { PageShellSkeleton } from '@/components/data-table/page-shell-skeleton'
import { Suspense } from 'react'
import { apiFetch } from '@/lib/api-client'
import { DbDictionaryClient, type DbTableSummary, type ExternalDbSource } from './client'

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function DbDictionaryPage() {
  const title = await resolvePageShellTitle('/docs/db-dictionary')
  return (
    <Suspense fallback={<PageShellSkeleton title={title} />}>
      <DbDictionaryPageData />
    </Suspense>
  )
}

async function DbDictionaryPageData() {
  const [platformTables, sources] = await Promise.all([
    apiFetch<DbTableSummary[]>('/db-schema/tables').catch(() => [] as DbTableSummary[]),
    apiFetch<ExternalDbSource[]>('/db-schema/external/sources').catch(() => [] as ExternalDbSource[]),
  ])
  return <DbDictionaryClient platformTables={platformTables} sources={sources} />
}
