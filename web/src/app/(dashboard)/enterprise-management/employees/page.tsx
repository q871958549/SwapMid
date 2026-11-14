import { Suspense } from 'react'
import { getTranslations } from 'next-intl/server'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { EmployeesClient, type Employee } from './client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'
import { parseServerListPagination } from '@/lib/crud/list-pagination'
import { EMPLOYEE_LIST_MAX_PAGE_SIZE } from './constants'

// 外壳随路由切换瞬间渲染；员工主列表在 Suspense 内流式返回。
// 企业/部门/岗位（仅新建/编辑弹窗与部门筛选用）由客户端懒加载。
type EmployeesSearch = {
  departmentId?: string
  status?: string
  account?: string
  page?: string
  size?: string; sort?: string
  q?: string
}

export default async function EmployeesPage({ searchParams }: { searchParams: Promise<EmployeesSearch> }) {
  const t = await getTranslations('Enterprise')
  return (
    <Suspense
      fallback={
        <ListPageSkeleton
          title={t('employee.pageTitle')}
          createLabel={t('common.createEntityTitle', { entityName: t('common.employeeEntity') })}
          columns={7}
        />
      }
    >
      <EmployeesData searchParams={searchParams} />
    </Suspense>
  )
}

async function EmployeesData({ searchParams }: { searchParams: Promise<EmployeesSearch> }) {
  const sp = await searchParams
  // 首屏按 URL 的分页/筛选取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。
  const { pageIndex, pageSize, skipCount } = parseServerListPagination(
    sp.page, sp.size, undefined, EMPLOYEE_LIST_MAX_PAGE_SIZE)
  const qs = new URLSearchParams({ maxResultCount: String(pageSize), skipCount: String(skipCount) })
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('Sorting', abpSorting)
  if (sp.departmentId) {
    qs.set('departmentId', sp.departmentId)
    qs.set('includeChildDepartments', 'true') // 按部门查询含下级（需求2）
  }
  if (sp.status) qs.set('status', sp.status)
  if (sp.account) qs.set('hasAccount', sp.account)
  if (sp.q) qs.set('filter', sp.q)
  // 与列表并发：服务端统计「在职且未生成账号」精确数，供一键开通对话框首屏显示（避免客户端 mount effect）。
  const pendingQs = new URLSearchParams({ status: '0', hasAccount: 'false', maxResultCount: '1' })
  const [employees, pending] = await Promise.all([
    apiFetch<PagedResult<Employee>>(`/employees?${qs.toString()}`)
      .catch(() => ({ items: [], totalCount: 0 }) as PagedResult<Employee>),
    apiFetch<PagedResult<Employee>>(`/employees?${pendingQs.toString()}`)
      .catch(() => ({ items: [], totalCount: 0 }) as PagedResult<Employee>),
  ])
  return (
    <EmployeesClient
      data={employees}
      initialDepartmentId={sp.departmentId ?? ''}
      initialStatus={sp.status ?? ''}
      initialAccount={sp.account ?? ''}
      initialPage={pageIndex}
      initialPageSize={pageSize}
      initialSorting={sorting}
      initialSearch={sp.q ?? ''}
      initialPendingCount={pending.totalCount}
    />
  )
}
