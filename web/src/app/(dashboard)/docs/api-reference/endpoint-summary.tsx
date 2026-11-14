'use client'

import { useTranslations } from 'next-intl'
import type { ApiEndpoint, ApiModule } from './types'

/** 缺省 true：旧生成物 / 手写集成指南无此字段时仍按已描述处理。 */
export function isSummaryDocumented(ep: Pick<ApiEndpoint, 'summaryDocumented'>) {
  return ep.summaryDocumented !== false
}

export function countModuleSummaryCoverage(mod: ApiModule) {
  let total = 0
  let described = 0
  for (const section of mod.sections) {
    for (const ep of section.endpoints) {
      total += 1
      if (isSummaryDocumented(ep)) described += 1
    }
  }
  return { described, total }
}

/** 真实 XML summary 原样展示；缺失时标「待补」并附带方法+路径的软推断（不可当标题）。 */
export function EndpointSummaryLine({ ep }: { ep: ApiEndpoint }) {
  const t = useTranslations('DeveloperDocs.apiReference')
  if (isSummaryDocumented(ep)) {
    return <span className="mt-0.5 block text-sm text-muted-foreground">{ep.summary}</span>
  }
  return (
    <span className="mt-0.5 flex flex-wrap items-center gap-2 text-sm">
      <span className="text-muted-foreground/50">{t('pendingDescription')}</span>
      <span className="text-xs text-muted-foreground/40">{t('inferredSummary', { summary: ep.summary })}</span>
    </span>
  )
}
