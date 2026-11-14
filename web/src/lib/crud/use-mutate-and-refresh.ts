'use client'

import { useCallback } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { absorbPlatformUpdating } from '@/lib/platform-updating'

export interface MutateOptions {
  /** HTTP 方法，默认 POST。 */
  method?: 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  /** 请求体（会 JSON 序列化并带 Content-Type；省略则不发 body，如无参 PUT/DELETE）。 */
  body?: unknown
  /** 成功 toast 标题（省略则不弹成功提示，适合静默动作）。 */
  okTitle?: string
  /** 成功 toast 描述。 */
  okDescription?: string
  /** 失败 toast 标题，默认「操作失败」。 */
  errorTitle?: string
  /** 失败 toast 描述的兜底文案（后端未返回可解析的 ABP 错误消息时显示），默认「请稍后重试」。 */
  errorFallback?: string
}

/**
 * 内联 / 批量写操作统一助手：`fetch(写)` → 成功 toast → `reload()`（即时刷新可见列表）→ `router.refresh()`（失效 Next Router Cache）。
 *
 * 复刻 `useCrudForm` 的「reload + refresh 双打」语义（见 `use-crud-form.ts`），**收口**所有不走 useCrudForm 的
 * 内联/批量写（启停 toggle / 关闭 / 审批 / 批量删 / 批量改状态等）——它们此前多只 `list.reload()` 不 `router.refresh()`，
 * 在 `staleTimes.dynamic` 缓存窗口内「切走再切回」会读到旧 SSR 快照（显旧值/旧行）。**两者缺一都不够**：
 * 只 reload → 切回读旧 RSC；只 refresh → 服务端分页页可见列表不即时更新（useCrudResource 忽略新 initial）。
 *
 * @param reload 服务端分页页传 `useCrudResource` 的 `reload`（即时刷新当前页）；客户端分页页可省略（仅靠 refresh 重跑 SSR）。
 * @returns `mutate(url, opts)` → `Promise<boolean>`：成功 true / 失败 false（失败已 toast，调用方据此决定是否清选中等）。
 */
export function useMutateAndRefresh(reload?: () => void) {
  const router = useRouter()
  // 失败 toast 的默认文案走 i18n（Common namespace）；errorTitle/errorFallback 传参覆盖时按调用方原样使用。
  const t = useTranslations('Common')
  return useCallback(
    async (url: string, opts: MutateOptions = {}): Promise<boolean> => {
      try {
        const res = await fetch(url, {
          method: opts.method ?? 'POST',
          headers: opts.body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
          body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
        })
        if (!res.ok) {
          const text = await res.text()
          if (absorbPlatformUpdating(text)) return false
          throw new Error(text)
        }
        if (opts.okTitle) {
          toast({ variant: 'success', title: opts.okTitle, description: opts.okDescription })
        }
        reload?.()
        router.refresh()
        return true
      } catch (e) {
        console.error('[mutate]', url, e)
        toast({
          variant: 'destructive',
          title: opts.errorTitle ?? t('operationFailedTitle'),
          description: getApiErrorMessage(e, opts.errorFallback ?? t('tryAgainLater')),
        })
        return false
      }
    },
    [reload, router, t],
  )
}
