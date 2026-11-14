import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { ListPageSkeleton } from '@/components/data-table/list-page-skeleton'
import { Suspense } from 'react'
import { apiFetch, type PagedResult } from '@/lib/api-client'
import { parseSortParam, toAbpSorting } from '@/lib/crud/list-sort'
import { NotificationRecipientsClient, type NotificationRecipient } from './client'

// 通知接收人（体系化收件人）。配置存业务表，Server 预取后交给 client 做 CRUD。
const FALLBACK: PagedResult<NotificationRecipient> = { items: [], totalCount: 0 }

type NotificationRecipientsSearch = { page?: string; size?: string; sort?: string }

// 外壳随路由切换瞬间渲染；数据在 Suspense 内流式返回。
export default async function NotificationRecipientsPage({ searchParams }: { searchParams: Promise<NotificationRecipientsSearch> }) {
  const title = await resolvePageShellTitle('/system/notification-recipients')
  return (
    <Suspense fallback={<ListPageSkeleton title={title} columns={5} />}>
      <NotificationRecipientsPageData searchParams={searchParams} />
    </Suspense>
  )
}

async function NotificationRecipientsPageData({ searchParams }: { searchParams: Promise<NotificationRecipientsSearch> }) {
  const sp = await searchParams
  // 首屏按 URL 的分页取数（方案A：URL 持久化列表视图状态，切走再切回/刷新可恢复）。该接口无统一 filter，无搜索词。
  const pageSize = Math.max(1, Number(sp.size) || 15)
  const pageIndex = Math.max(0, (Number(sp.page) || 1) - 1)
  const qs = new URLSearchParams({
    maxResultCount: String(pageSize),
    skipCount: String(pageIndex * pageSize),
    sorting: 'name',
  })
  // URL 带用户排序时覆盖默认 name 排序（键沿用小写 sorting，避免与默认键大小写不敏感撞车）
  const sorting = parseSortParam(sp.sort)
  const abpSorting = toAbpSorting(sorting)
  if (abpSorting) qs.set('sorting', abpSorting)

  // 已知通知来源（代码常量为事实源）：订阅来源改 checkbox 多选防手输错配；拉取失败回退空列表（弹窗降级为提示文案）。
  const [data, knownSources] = await Promise.all([
    apiFetch<PagedResult<NotificationRecipient>>(`/notification/recipients?${qs.toString()}`).catch((e) => {
      console.error('[notification-recipients]', e)
      return FALLBACK
    }),
    apiFetch<string[]>('/notification/recipients/sources').catch((e) => {
      console.error('[notification-recipients] sources', e)
      return [] as string[]
    }),
  ])
  return <NotificationRecipientsClient data={data} knownSources={knownSources} initialPage={pageIndex} initialPageSize={pageSize} initialSorting={sorting} />
}
