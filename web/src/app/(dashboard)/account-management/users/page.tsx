import { Suspense } from 'react'
import { getTranslations } from 'next-intl/server'
import { apiFetch, rethrowApiFetchControlFlow, type PagedResult } from '@/lib/api-client'
import { getSession } from '@/lib/session'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { UsersClient } from './client'
import type { AbpRole, AbpUser } from './users-types'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'
import { parseServerListPagination } from '@/lib/crud/list-pagination'
import { ACCOUNT_USER_LIST_MAX_PAGE_SIZE } from './constants'
import { parsePasswordPolicy } from '@/lib/password-policy'

type UsersSearch = {
  departmentId?: string
  isActive?: string
  roleIds?: string
  page?: string
  size?: string
  sort?: string
  q?: string
}

async function loadPasswordPolicy() {
  try {
    return parsePasswordPolicy(await apiFetch<unknown>('/account-management/password-policy'))
  } catch (e) {
    rethrowApiFetchControlFlow(e)
    return null
  }
}

export default async function UsersPage({ searchParams }: { searchParams: Promise<UsersSearch> }) {
  const t = await getTranslations('Account')
  return (
    <Suspense fallback={<ListPageSkeleton title={t('user.pageTitle')} createLabel={t('user.createTitle')} columns={5} />}>
      <UsersData searchParams={searchParams} />
    </Suspense>
  )
}

async function UsersData({ searchParams }: { searchParams: Promise<UsersSearch> }) {
  const session = await getSession()
  const sp = await searchParams
  const { pageIndex, pageSize, skipCount } = parseServerListPagination(
    sp.page, sp.size, undefined, ACCOUNT_USER_LIST_MAX_PAGE_SIZE)
  const usersQs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(skipCount) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) usersQs.set('Sorting', abpSorting)
  if (sp.departmentId) { usersQs.set('departmentId', sp.departmentId); usersQs.set('includeChildDepartments', 'true') }
  if (sp.isActive === 'true' || sp.isActive === 'false') usersQs.set('isActive', sp.isActive)
  for (const roleId of (sp.roleIds ?? '').split(',').map(id => id.trim()).filter(Boolean)) {
    usersQs.append('roleIds', roleId)
  }
  if (sp.q) usersQs.set('filter', sp.q)
  const [data, defaults, roles, passwordPolicy] = await Promise.all([
    apiFetch<PagedResult<AbpUser>>(`/account-users?${usersQs.toString()}`),
    apiFetch<{ defaultPassword: string }>('/account-management/settings/defaults'),
    apiFetch<{ items: AbpRole[] }>('/account-users/assignable-roles'),
    loadPasswordPolicy(),
  ])
  if (!defaults.defaultPassword?.trim()) {
    throw new Error('Account defaults response is missing the required default password')
  }
  return (
    <UsersClient
      data={data}
      defaultPassword={defaults.defaultPassword}
      passwordPolicy={passwordPolicy}
      roles={roles.items}
      initialDepartmentId={sp.departmentId ?? ''}
      initialIsActive={sp.isActive === 'true' || sp.isActive === 'false' ? sp.isActive : ''}
      initialRoleIds={sp.roleIds ?? ''}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
      currentUserName={session?.userName ?? ''}
      isImpersonating={!!session?.impersonator}
    />
  )
}
