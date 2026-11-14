import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import { hasPermission, Permissions } from '@/lib/permissions'
import { getSession } from '@/lib/session'
import { PipelineExecutionsClient, type PipelineExecution } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'

export type ExecutionsSearch = {
  status?: string
  startedFrom?: string
  startedTo?: string
  page?: string
  size?: string; sort?: string
}

export async function ExecutionsPageData({ searchParams }: { searchParams: Promise<ExecutionsSearch> }) {
  const [sp, session] = await Promise.all([searchParams, getSession()])
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1) // URL page 为 1-based，内部 0-based
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(pageIndex * pageSize) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (sp.status) qs.set('Status', sp.status)
  if (sp.startedFrom) qs.set('StartedFrom', sp.startedFrom)
  if (sp.startedTo) qs.set('StartedTo', `${sp.startedTo} 23:59:59`)

  let data: PagedResult<PipelineExecution> = { items: [], totalCount: 0 }
  try {
    data = await apiFetch<PagedResult<PipelineExecution>>(`/pipeline-executions?${qs.toString()}`)
  } catch (error) {
    rethrowApiFetchControlFlow(error)
  }

  return (
    <PipelineExecutionsClient
      initial={data}
      initialStatus={sp.status ?? ''}
      initialStartedFrom={sp.startedFrom ?? ''}
      initialStartedTo={sp.startedTo ?? ''}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      canResolveOutcome={hasPermission(
        session?.grantedPolicies ?? {},
        Permissions.DataIntegration.PipelineExecutionsResolveOutcome,
      )}
      canVerifyTargetReceipt={hasPermission(
        session?.grantedPolicies ?? {},
        Permissions.DataIntegration.PipelineExecutionsVerifyTargetReceipt,
      )}
    />
  )
}
