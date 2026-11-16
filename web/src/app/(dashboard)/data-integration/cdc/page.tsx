import { Suspense } from 'react'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import type {
  CdcStreamDto,
  DataPipelineDto,
  DataSourceDto,
  MessageTopicDto,
} from '@/lib/api/types'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'
import { hasPermission, Permissions } from '@/lib/permissions'
import { getSession } from '@/lib/session'
import { CdcStreamsClient } from './client'

type CdcStreamsSearch = {
  page?: string
  size?: string
  sort?: string
  q?: string
  status?: string
}

function isPostgreSqlSource(source: DataSourceDto): boolean {
  if (source.type !== 0 || !source.config) return false
  try {
    const parsed = JSON.parse(source.config) as { provider?: unknown }
    return typeof parsed.provider === 'string' && parsed.provider.toLowerCase() === 'postgresql'
  } catch {
    return /"provider"\s*:\s*"postgresql"/i.test(source.config)
  }
}

async function fetchInitialData(
  pageIndex: number,
  pageSize: number,
  q?: string,
  status?: string,
  abpSorting?: string,
) {
  const qs = new URLSearchParams({
    maxResultCount: String(pageSize),
    skipCount: String(pageIndex * pageSize),
  })
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (q) qs.set('filter', q)
  if (status && ['0', '1', '2', '3'].includes(status)) qs.set('status', status)

  const [streams, sources, pipelines, topics] = await Promise.all([
    apiFetch<PagedResult<CdcStreamDto>>(`/cdc-streams?${qs.toString()}`).catch(() => ({
      items: [],
      totalCount: 0,
    })),
    apiFetch<PagedResult<DataSourceDto>>('/data-sources?maxResultCount=1000').catch(() => ({
      items: [],
      totalCount: 0,
    })),
    apiFetch<PagedResult<DataPipelineDto>>('/data-pipelines?maxResultCount=1000').catch(() => ({
      items: [],
      totalCount: 0,
    })),
    apiFetch<PagedResult<MessageTopicDto>>('/message-topics?maxResultCount=1000').catch(() => ({
      items: [],
      totalCount: 0,
    })),
  ])

  const pgSources = sources.items.filter(isPostgreSqlSource)
  const sinks = sources.items.filter(s => s.type === 3 || s.type === 4 || s.type === 5)
  return {
    streams,
    sources: pgSources.length > 0 ? pgSources : sources.items.filter(s => s.type === 0),
    sinks,
    pipelines: pipelines.items,
    topics: topics.items,
  }
}

export default async function CdcStreamsPage({
  searchParams,
}: {
  searchParams: Promise<CdcStreamsSearch>
}) {
  const title = await resolvePageShellTitle('/data-integration/cdc')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={8} />}>
      <CdcStreamsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function CdcStreamsPageData({
  searchParams,
}: {
  searchParams: Promise<CdcStreamsSearch>
}) {
  const [sp, session] = await Promise.all([searchParams, getSession()])
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1)
  const sorting = parseSortParam(sp.sort)
  const status = sp.status && ['0', '1', '2', '3'].includes(sp.status) ? sp.status : 'all'
  const { streams, sources, sinks, pipelines, topics } = await fetchInitialData(
    pageIndex,
    pageSize,
    sp.q,
    status === 'all' ? undefined : status,
    toAbpSorting(sorting),
  )
  const grantedPolicies = session?.grantedPolicies ?? {}

  return (
    <CdcStreamsClient
      data={streams}
      sources={sources}
      sinks={sinks}
      pipelines={pipelines}
      topics={topics}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
      initialStatus={status}
      canCreate={hasPermission(grantedPolicies, Permissions.DataIntegration.CdcStreamsCreate)}
      canUpdate={hasPermission(grantedPolicies, Permissions.DataIntegration.CdcStreamsUpdate)}
      canDelete={hasPermission(grantedPolicies, Permissions.DataIntegration.CdcStreamsDelete)}
      canManage={hasPermission(grantedPolicies, Permissions.DataIntegration.CdcStreamsManage)}
    />
  )
}
