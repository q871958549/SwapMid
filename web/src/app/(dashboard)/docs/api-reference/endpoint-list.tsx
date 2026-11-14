'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { cn } from '@/lib/utils'
import { ChevronDown, ChevronRight } from 'lucide-react'
import type { ApiField, ApiEndpoint, ApiSection } from './types'
import { EndpointSummaryLine } from './endpoint-summary'

export const METHOD_STYLE: Record<string, string> = {
  GET:    'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300',
  POST:   'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300',
  PUT:    'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300',
  DELETE: 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300',
  PATCH:  'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300',
}

export function endpointKey(ep: Pick<ApiEndpoint, 'method' | 'path'>) {
  return `${ep.method}:${ep.path}`
}

function FieldTable({ fields, title, kind }: { fields: ApiField[]; title: string; kind: 'request' | 'response' }) {
  const t = useTranslations('DeveloperDocs.apiReference')
  const tc = useTranslations('Common')
  // 自动生成的接口字段没有中文名/示例；整列为空时隐藏，避免空列占位
  const showCn = fields.some((f) => f.cnName)
  const showExample = fields.some((f) => f.example)
  // 请求/Query 用「必填」(调用方必须传)；响应用「可空」(返回值是否可能为 null)——语义不同
  const flagLabel = kind === 'response' ? t('nullableColumnHeader') : t('requiredColumnHeader')
  return (
    <div className="mt-3">
      <div className="text-xs font-medium text-muted-foreground mb-1.5">{title}</div>
      <div className="rounded-lg border overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-muted/40 border-b text-xs text-muted-foreground">
              <th className="px-3 py-2 text-left font-medium w-48">{t('fieldNameColumn')}</th>
              {showCn && <th className="px-3 py-2 text-left font-medium w-28">{t('cnNameColumn')}</th>}
              <th className="px-3 py-2 text-left font-medium w-32">{tc('type')}</th>
              <th className="px-3 py-2 text-left font-medium w-14">{flagLabel}</th>
              <th className="px-3 py-2 text-left font-medium">{t('descriptionColumn')}</th>
              {showExample && <th className="px-3 py-2 text-left font-medium w-32">{t('exampleColumn')}</th>}
            </tr>
          </thead>
          <tbody className="divide-y">
            {fields.map((f) => (
              <tr key={f.name} className="hover:bg-muted/20">
                <td className="px-3 py-2 font-mono text-xs text-blue-600 dark:text-blue-400">{f.name}</td>
                {showCn && <td className="px-3 py-2 text-xs">{f.cnName}</td>}
                <td className="px-3 py-2 font-mono text-xs text-muted-foreground">{f.type}</td>
                <td className="px-3 py-2">
                  {kind === 'response' ? (
                    f.nullable ? (
                      <span className="text-[10px] text-muted-foreground">{t('cellNullable')}</span>
                    ) : (
                      <span className="text-[10px] text-muted-foreground">{t('cellNotNullable')}</span>
                    )
                  ) : f.required ? (
                    <span className="text-[10px] font-medium text-red-600 dark:text-red-400">{t('cellRequired')}</span>
                  ) : (
                    <span className="text-[10px] text-muted-foreground">{t('cellOptional')}</span>
                  )}
                </td>
                <td className="px-3 py-2 text-xs text-muted-foreground">{f.description}</td>
                {showExample && <td className="px-3 py-2 font-mono text-xs text-muted-foreground">{f.example ?? ''}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function EndpointCard({ ep, forceOpen, onOpen, onClose }: {
  ep: ApiEndpoint
  forceOpen?: boolean
  onOpen?: (ep: ApiEndpoint) => void
  onClose?: (ep: ApiEndpoint) => void
}) {
  const t = useTranslations('DeveloperDocs.apiReference')
  const [manualOpen, setManualOpen] = useState(false)
  const open = !!forceOpen || manualOpen
  const hasDetail = !!(ep.description || ep.note || ep.queryParams?.length || ep.requestBody?.length || ep.responseFields?.length)

  return (
    <div id={endpointKey(ep)} className="rounded-lg border bg-card overflow-hidden">
      <button
        className="w-full flex items-start gap-3 px-4 py-3 text-left hover:bg-muted/30 transition-colors"
        onClick={() => {
          if (!hasDetail) return
          if (open) {
            setManualOpen(false)
            onClose?.(ep)
          } else {
            setManualOpen(true)
            onOpen?.(ep)
          }
        }}
        disabled={!hasDetail}
      >
        <span className={cn('mt-0.5 shrink-0 rounded px-2 py-0.5 text-xs font-bold w-14 text-center', METHOD_STYLE[ep.method])}>
          {ep.method}
        </span>
        <span className="min-w-0 flex-1">
          <code className="block text-sm font-mono text-foreground break-all">{ep.path}</code>
          <EndpointSummaryLine ep={ep} />
        </span>
        {hasDetail && (
          <span className="mt-0.5 shrink-0 text-muted-foreground">
            {open ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
          </span>
        )}
      </button>
      {open && hasDetail && (
        <div className="border-t px-4 pb-4 pt-3 space-y-2 bg-muted/10">
          {ep.description && <p className="text-sm text-muted-foreground">{ep.description}</p>}
          {ep.note && (
            <div className="flex gap-2 rounded-md bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-3 py-2 text-xs text-amber-800 dark:text-amber-300">
              <span className="font-medium shrink-0">{t('noteLabel')}</span>
              <span>{ep.note}</span>
            </div>
          )}
          {ep.queryParams && ep.queryParams.length > 0 && (
            <FieldTable fields={ep.queryParams} title={t('queryParamsTitle')} kind="request" />
          )}
          {ep.requestBody && ep.requestBody.length > 0 && (
            <FieldTable fields={ep.requestBody} title={t('requestBodyTitle')} kind="request" />
          )}
          {ep.responseFields && ep.responseFields.length > 0 && (
            <FieldTable fields={ep.responseFields} title={t('responseFieldsTitle')} kind="response" />
          )}
        </div>
      )}
    </div>
  )
}

export function SectionBlock({
  section,
  activeKey,
  onOpen,
  onClose,
}: {
  section: ApiSection
  activeKey?: string | null
  onOpen?: (ep: ApiEndpoint) => void
  onClose?: (ep: ApiEndpoint) => void
}) {
  return (
    <div>
      <h3 className="text-sm font-semibold text-muted-foreground mb-2 font-mono">{section.title}</h3>
      <div className="space-y-2">
        {section.endpoints.map((ep) => (
          <EndpointCard
            key={`${ep.method}-${ep.path}`}
            ep={ep}
            forceOpen={activeKey === endpointKey(ep)}
            onOpen={onOpen}
            onClose={onClose}
          />
        ))}
      </div>
    </div>
  )
}
