import Link from 'next/link'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/** 列表里可点击的文字：主色、手型、悬停下划线。纯文本不要用这个类。 */
export const dataTableLinkClass = 'text-primary cursor-pointer underline-offset-4 hover:underline'

export function DataTableLinkCell({
  href,
  children,
  className,
  title,
}: {
  href: string
  children: ReactNode
  className?: string
  title?: string
}) {
  return (
    <Link href={href} title={title} className={cn(dataTableLinkClass, 'select-text text-left', className)}>
      {children}
    </Link>
  )
}
