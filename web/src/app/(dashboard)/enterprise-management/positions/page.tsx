import { Suspense } from 'react'
import { getTranslations } from 'next-intl/server'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { PositionsClient, type Position } from './client'

// 外壳随路由切换瞬间渲染；岗位主列表在 Suspense 内流式返回。
// 企业/部门（仅新建/编辑弹窗用）由客户端懒加载。
export default async function PositionsPage() {
  const t = await getTranslations('Enterprise')
  return (
    <Suspense
      fallback={
        <ListPageSkeleton
          title={t('position.pageTitle')}
          createLabel={t('common.createEntityTitle', { entityName: t('common.positionEntity') })}
          columns={7}
        />
      }
    >
      <PositionsData />
    </Suspense>
  )
}

async function PositionsData() {
  const data = await apiFetch<PagedResult<Position>>('/positions?maxResultCount=1000').catch(e => {
    console.error('[positions]', e)
    return { items: [], totalCount: 0 } as PagedResult<Position>
  })
  return <PositionsClient data={data} />
}
