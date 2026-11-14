import { Suspense } from 'react'
import { getTranslations } from 'next-intl/server'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { DepartmentsClient, type CompanyDepartment } from './client'

// 外壳（标题/新建按钮）随路由切换瞬间渲染；主列表数据在 Suspense 内流式返回。
// 企业列表（仅新建/编辑弹窗用）由客户端懒加载。
export default async function DepartmentsPage() {
  const t = await getTranslations('Enterprise')
  return (
    <Suspense
      fallback={
        <ListPageSkeleton
          title={t('department.pageTitle')}
          createLabel={t('common.createEntityTitle', { entityName: t('common.departmentEntity') })}
          columns={8}
        />
      }
    >
      <DepartmentsData />
    </Suspense>
  )
}

async function DepartmentsData() {
  const depts = await apiFetch<PagedResult<CompanyDepartment>>('/departments?maxResultCount=1000')
    .catch(() => ({ items: [], totalCount: 0 }) as PagedResult<CompanyDepartment>)
  return <DepartmentsClient data={depts} />
}
