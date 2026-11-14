import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import type { AccountRoleDto } from '@/lib/api/types'
import { RolesClient } from './client'

export default async function RolesPage() {
  const title = await resolvePageShellTitle('/account-management/roles')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={6} />}>
      <RolesPageData />
    </Suspense>
  )
}

async function RolesPageData() {
  let data: PagedResult<AccountRoleDto> = { items: [], totalCount: 0 }
  try {
    data = await apiFetch<PagedResult<AccountRoleDto>>(
      '/account-roles?maxResultCount=1000&skipCount=0',
      { cache: 'no-store' },
    )
  } catch (e) { rethrowApiFetchControlFlow(e); console.error('[roles]', e) }
  return <RolesClient data={data} />
}
