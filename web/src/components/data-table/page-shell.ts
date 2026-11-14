import { getTranslations } from 'next-intl/server'
import { getPageTitle, type NavT } from '@/lib/nav-utils'

/**
 * 路由外壳标题：只读本地 Nav 词典，不打后端。
 * 给 page.tsx 默认导出用——await 这个再返回 `<Suspense>`，冷切换就能先出带标题的骨架。
 */
export async function resolvePageShellTitle(pathname: string): Promise<string> {
  const t = (await getTranslations('Nav')) as NavT
  return getPageTitle(pathname, t)
}
