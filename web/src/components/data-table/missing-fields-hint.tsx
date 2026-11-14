'use client'

import { useTranslations } from 'next-intl'
import { Info } from 'lucide-react'
import { cn } from '@/lib/utils'

/**
 * 「还需填写：X、Y」——提交按钮被禁用时，把**原因**说出来。
 *
 * 🔴 **为什么需要它**：本仓全站表单都用「必填未齐 → 禁用提交按钮」表达校验状态
 * （`CrudFormDialog` 的 `canSubmit`，56 个页面在用）。禁用是个**无声**的信号：用户看到
 * 灰按钮，知道点不了，却不知道差哪一项——字段一多就只能挨个回看
 * （2026-08-11 用户反馈原话：「只是单纯的不予点击…但并未清晰的提醒用户缺少哪些需要填写的信息」）。
 *
 * **刻意保留禁用、不改成「可点击 + 点后报错」**：后者要多点一次才知道结果，且与全站既有
 * 交互习惯相悖；本组件让缺失项随填随消，用户在**点之前**就知道还差什么，信息更早、代价更小。
 *
 * 语气用 `text-muted-foreground` 而非 `text-destructive`：表单还没提交过，缺项是**引导**不是错误，
 * 一打开就飘红会把「尚未填写」误传成「填错了」。真正的提交失败仍走 `formError` 的红色块。
 */
export function MissingFieldsHint({ fields, className }: MissingFieldsHintProps) {
  const t = useTranslations('Common')
  if (fields.length === 0) return null

  return (
    <p
      // role="status" 让读屏在缺失项变化时播报——视觉用户靠颜色和位置发现它，读屏用户需要主动通知
      role="status"
      className={cn('text-muted-foreground flex min-w-0 items-center gap-1.5 text-xs', className)}
    >
      <Info className="size-3.5 shrink-0" />
      <span className="truncate">{t('missingFieldsHint', { fields: fields.join('、') })}</span>
    </p>
  )
}

interface MissingFieldsHintProps {
  /** 缺失字段的**显示名**（已翻译）。空数组时不渲染任何东西。 */
  fields: string[]
  className?: string
}
