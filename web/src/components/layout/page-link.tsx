'use client'

import Link from 'next/link'
import { useStoredPagePath } from '@/stores/use-tab-store'

/**
 * 导航入口统一零件：裸路径 href 自动解析为已开页签的存量 path（含筛选/分页 query），
 * 使「从侧栏 / 工作台 / 最近使用回到已开页面」与点页签一致地恢复列表视图状态（方案 A 入口收口）。
 * 自带 query 的定向链接（如工作台待办 ?status=...）原样直达；其余行为同 next/link。
 *
 * 适用：href 指向平台功能页（页签体系内路径）的所有导航入口。
 * 不适用：页面内部业务跳转（详情页、外部链接）——那些语义就是"按给定地址打开"，用 next/link。
 *
 * 预取策略固定为 Next 默认 auto：动态后台页只预取到最近 loading 边界。这里刻意不暴露
 * `prefetch={true}`，避免展开侧栏时完整执行一组动态页的数据请求；确需完整预取应另行评审。
 */
export function PageLink({
  href,
  ...props
}: Omit<React.ComponentProps<typeof Link>, 'href' | 'prefetch'> & { href: string }) {
  return <Link {...props} href={useStoredPagePath(href)} prefetch={null} />
}
