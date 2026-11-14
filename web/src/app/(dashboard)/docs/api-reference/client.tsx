'use client'

import { useEffect, useMemo, useState } from 'react'
import { useTranslations } from 'next-intl'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'
import { Search } from 'lucide-react'
import type { ApiEndpoint, ApiModule, DomainOverview } from './types'
import { API_MODULES as GENERATED_MODULES } from './api-doc-data.generated'
import { DOMAIN_OVERRIDES } from './api-doc-overrides'
import { INTEGRATION_GUIDES } from './integration-guides'
import { IntegrationView } from './integration-view'
import { countModuleSummaryCoverage, EndpointSummaryLine } from './endpoint-summary'
import { endpointKey, METHOD_STYLE, SectionBlock } from './endpoint-list'

type ApiRefT = ReturnType<typeof useTranslations<'DeveloperDocs.apiReference'>>

// ─── Method colors / endpoint cards → endpoint-list.tsx ────────────────────

// ─── Design principles (人工维护内容，随 chrome 一并三语化；接口数据见 api-doc-data.generated.ts) ───
// 模块级常量表改工厂函数（同批次既有范式）：内容需要 useTranslations，只能在渲染期由持有 t 的调用方求值。

function getDesignPrinciples(t: ApiRefT) {
  return [
    { title: t('principles.urlConventionTitle'), content: t('principles.urlConventionContent') },
    { title: t('principles.paginationTitle'), content: t('principles.paginationContent') },
    { title: t('principles.timeRangeTitle'), content: t('principles.timeRangeContent') },
    { title: t('principles.codeAssociationTitle'), content: t('principles.codeAssociationContent') },
    { title: t('principles.errorFormatTitle'), content: t('principles.errorFormatContent') },
    { title: t('principles.httpMethodTitle'), content: t('principles.httpMethodContent') },
    { title: t('principles.authTitle'), content: t('principles.authContent') },
  ]
}

// 设计规范是人工内容，置于自动生成的业务模块之前
function getDesignModule(t: ApiRefT): ApiModule {
  return {
    id: 'design',
    title: t('designPrinciplesTitle'),
    description: t('designModuleDescription'),
    baseUrl: '',
    sections: [],
  }
}

// 自动生成的模块按域 id 叠加手写覆盖层（域概述 / 主线编排 / 关键约束，来自 api-doc-overrides.ts，
// 该文件正文按拍板登记「有意不翻」，此处不涉及）；无覆盖的域原样透传。
function getApiModules(t: ApiRefT): ApiModule[] {
  return [
    getDesignModule(t),
    ...GENERATED_MODULES.map((m) => (DOMAIN_OVERRIDES[m.id] ? { ...m, overview: DOMAIN_OVERRIDES[m.id] } : m)),
  ]
}

// ─── Components ────────────────────────────────────────────────────────────

// 域级概述（手写覆盖层）：渲染在该域自动生成的接口列表之上，把「零件清单」升级为「装配说明」。
// overview 内容（summary/workflow/notes）来自 api-doc-overrides.ts 的手写领域知识，按拍板登记
// 「有意不翻」，此处只翻译框架文案（"主线编排"/"关键约束" 等标签）。
function DomainOverviewPanel({ overview }: { overview: DomainOverview }) {
  const t = useTranslations('DeveloperDocs.apiReference')
  return (
    <div className="space-y-4 rounded-lg border bg-muted/20 p-4">
      <p className="text-sm leading-relaxed">{overview.summary}</p>
      {overview.workflow && overview.workflow.length > 0 && (
        <div>
          <div className="mb-2 text-xs font-semibold text-muted-foreground">{t('workflowTitle')}</div>
          <ol className="space-y-2">
            {overview.workflow.map((step) => (
              <li key={step.title} className="rounded-md border bg-card px-3 py-2">
                <div className="text-sm font-medium">{step.title}</div>
                <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">{step.detail}</p>
                {step.endpoints && step.endpoints.length > 0 && (
                  <div className="mt-1.5 flex flex-wrap gap-1.5">
                    {step.endpoints.map((ep) => (
                      <code key={ep} className="rounded bg-muted px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">
                        {ep}
                      </code>
                    ))}
                  </div>
                )}
              </li>
            ))}
          </ol>
        </div>
      )}
      {overview.notes && overview.notes.length > 0 && (
        <div className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 dark:border-amber-800 dark:bg-amber-900/20">
          <div className="mb-1.5 text-xs font-medium text-amber-800 dark:text-amber-300">{t('constraintsTitle')}</div>
          <ul className="space-y-1 text-xs text-amber-800 dark:text-amber-300">
            {overview.notes.map((n, i) => (
              <li key={i} className="flex gap-1.5">
                <span className="shrink-0">•</span>
                <span className="leading-relaxed">{n}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

function DesignPrinciplesPanel() {
  const t = useTranslations('DeveloperDocs.apiReference')
  const principles = getDesignPrinciples(t)
  return (
    <div className="space-y-4">
      <div>
        <h2 className="text-xl font-semibold">{t('designPrinciplesTitle')}</h2>
        <p className="text-sm text-muted-foreground mt-1">{t('designPrinciplesSubtitle')}</p>
      </div>
      <div className="grid gap-3">
        {principles.map((p) => (
          <div key={p.title} className="rounded-lg border p-4 bg-card">
            <div className="font-medium text-sm mb-1">{p.title}</div>
            <p className="text-sm text-muted-foreground leading-relaxed">{p.content}</p>
          </div>
        ))}
      </div>
      <div className="rounded-lg border border-blue-200 dark:border-blue-800 bg-blue-50 dark:bg-blue-900/20 p-4">
        <div className="font-medium text-sm text-blue-800 dark:text-blue-300 mb-1">{t('autoSyncTitle')}</div>
        <p className="text-sm text-blue-700 dark:text-blue-400 leading-relaxed">
          {t.rich('autoSyncDescription', {
            strong: (chunks) => <strong>{chunks}</strong>,
            syncCmd: (chunks) => <code className="font-mono text-xs bg-blue-100 dark:bg-blue-800/40 px-1.5 py-0.5 rounded">{chunks}</code>,
            swaggerUrl: (chunks) => (
              <code className="ml-1 font-mono text-xs bg-blue-100 dark:bg-blue-800/40 px-1.5 py-0.5 rounded">{chunks}</code>
            ),
          })}
        </p>
      </div>
    </div>
  )
}

// ─── Main component ────────────────────────────────────────────────────────

export function ApiReferenceClient() {
  const t = useTranslations('DeveloperDocs.apiReference')
  const [view, setView] = useState<'reference' | 'integration'>('reference')
  const [activeModule, setActiveModule] = useState<string>('design')
  const [activeGuide, setActiveGuide] = useState<string>(INTEGRATION_GUIDES[0]?.id ?? '')
  const [search, setSearch] = useState('')
  const [activeKey, setActiveKey] = useState<string | null>(null)

  const API_MODULES = getApiModules(t)
  const currentModule = API_MODULES.find(m => m.id === activeModule)
  const currentGuide = INTEGRATION_GUIDES.find(g => g.id === activeGuide)

  const openEndpoint = (ep: ApiEndpoint, moduleId?: string) => {
    const key = endpointKey(ep)
    setActiveKey(key)
    if (moduleId) setActiveModule(moduleId)
    if (typeof window === 'undefined') return
    const next = `#${encodeURIComponent(key)}`
    if (window.location.hash !== next) history.replaceState(null, '', next)
  }

  const closeEndpoint = (ep: ApiEndpoint) => {
    const key = endpointKey(ep)
    setActiveKey((current) => (current === key ? null : current))
    if (typeof window === 'undefined') return
    const hash = `#${encodeURIComponent(key)}`
    if (window.location.hash === hash) {
      history.replaceState(null, '', `${window.location.pathname}${window.location.search}`)
    }
  }

  useEffect(() => {
    const applyHash = () => {
      const raw = window.location.hash.replace(/^#/, '')
      if (!raw) return
      const key = decodeURIComponent(raw)
      for (const mod of GENERATED_MODULES) {
        for (const section of mod.sections) {
          const ep = section.endpoints.find((item) => endpointKey(item) === key)
          if (ep) {
            setView('reference')
            setActiveModule(mod.id)
            setActiveKey(key)
            setSearch('')
            return
          }
        }
      }
    }
    // hash 是外部系统；推迟到下一 tick 再写 state，避免 set-state-in-effect
    const timer = window.setTimeout(applyHash, 0)
    window.addEventListener('hashchange', applyHash)
    return () => {
      window.clearTimeout(timer)
      window.removeEventListener('hashchange', applyHash)
    }
  }, [])

  const globalHits = useMemo(() => {
    const kw = search.trim()
    if (!kw) return [] as { moduleId: string; moduleTitle: string; ep: ApiEndpoint }[]
    const lower = kw.toLowerCase()
    const hits: { moduleId: string; moduleTitle: string; ep: ApiEndpoint }[] = []
    for (const mod of GENERATED_MODULES) {
      for (const section of mod.sections) {
        for (const ep of section.endpoints) {
          if (
            ep.path.toLowerCase().includes(lower) ||
            ep.summary.toLowerCase().includes(lower) ||
            ep.method.toLowerCase() === lower
          ) {
            hits.push({ moduleId: mod.id, moduleTitle: mod.title, ep })
          }
        }
      }
    }
    return hits
  }, [search])

  const showingGlobalSearch = view === 'reference' && search.trim().length > 0

  return (
    // 接入 dashboard <main> 的 flex 高度链（flex-col flex-1 min-h-0），-m-6 抵消父级 p-6 贴满边界。
    // 顶部一级切换 shrink-0；唯一滚动发生在下方内部 aside / main，外层不再产生第二条滚动条。
    <div className="flex flex-col flex-1 min-h-0 -m-6">
      {/* 顶部一级切换：系统接口参考（全量·内部）/ 集成指南（场景化·对外） */}
      <div className="flex shrink-0 items-center gap-1 border-b px-4">
        {([
          ['reference', t('viewReference')],
          ['integration', t('viewIntegration')],
        ] as const).map(([v, label]) => (
          <button
            key={v}
            onClick={() => setView(v)}
            className={cn(
              '-mb-px border-b-2 px-4 py-2.5 text-sm transition-colors',
              view === v
                ? 'border-primary font-medium text-foreground'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            )}
          >
            {label}
          </button>
        ))}
      </div>

      <div className="flex min-h-0 flex-1">
        {/* Sidebar：reference 显示业务模块；integration 显示集成场景 */}
        <aside className="w-56 shrink-0 overflow-y-auto overscroll-none border-r bg-muted/20 [overflow-anchor:none]">
          <div className="p-3 border-b space-y-2">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
              {view === 'reference' ? t('sidebarReferenceLabel') : t('sidebarIntegrationLabel')}
            </p>
            {view === 'reference' && (
              <div className="relative">
                <Search className="absolute left-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
                <Input
                  placeholder={t('searchPlaceholder')}
                  value={search}
                  onChange={e => setSearch(e.target.value)}
                  className="pl-7 h-8 text-sm"
                />
              </div>
            )}
          </div>
          <nav className="p-2 space-y-0.5">
            {view === 'reference'
              ? API_MODULES.map((mod) => (
                  <button
                    key={mod.id}
                    onClick={() => { setActiveModule(mod.id); setSearch('') }}
                    className={cn(
                      'w-full flex items-center gap-2 px-3 py-2 rounded-md text-sm text-left transition-colors',
                      !showingGlobalSearch && activeModule === mod.id
                        ? 'bg-primary text-primary-foreground font-medium'
                        : 'text-muted-foreground hover:bg-muted hover:text-foreground'
                    )}
                  >
                    {mod.title}
                  </button>
                ))
              : INTEGRATION_GUIDES.map((g) => (
                  <button
                    key={g.id}
                    onClick={() => setActiveGuide(g.id)}
                    className={cn(
                      'w-full flex items-center gap-2 px-3 py-2 rounded-md text-sm text-left transition-colors',
                      activeGuide === g.id
                        ? 'bg-primary text-primary-foreground font-medium'
                        : 'text-muted-foreground hover:bg-muted hover:text-foreground'
                    )}
                  >
                    {g.title}
                  </button>
                ))}
          </nav>
        </aside>

        {/* Main content */}
        <main className="flex-1 overflow-y-auto overscroll-none [overflow-anchor:none]">
          {view === 'integration' ? (
            currentGuide ? (
              <IntegrationView guide={currentGuide} />
            ) : (
              <p className="p-8 text-center text-sm text-muted-foreground">{t('noIntegrationScenario')}</p>
            )
          ) : (
            <div className="max-w-4xl mx-auto px-6 py-6 space-y-6">
              {showingGlobalSearch ? (
                <>
                  <div>
                    <h2 className="text-xl font-semibold">{t('searchResultsTitle')}</h2>
                    <p className="text-sm text-muted-foreground mt-1">
                      {t('searchResultsCount', { count: globalHits.length })}
                    </p>
                  </div>
                  <div className="space-y-2">
                    {globalHits.map(({ moduleId, moduleTitle, ep }) => (
                      <button
                        key={`${moduleId}-${endpointKey(ep)}`}
                        className="w-full rounded-lg border bg-card px-4 py-3 text-left hover:bg-muted/30"
                        onClick={() => {
                          setSearch('')
                          openEndpoint(ep, moduleId)
                        }}
                      >
                        <div className="flex items-start gap-3">
                          <span className={cn('mt-0.5 shrink-0 rounded px-2 py-0.5 text-xs font-bold w-14 text-center', METHOD_STYLE[ep.method])}>
                            {ep.method}
                          </span>
                          <span className="min-w-0 flex-1">
                            <code className="block text-sm font-mono break-all">{ep.path}</code>
                            <EndpointSummaryLine ep={ep} />
                            <span className="mt-1 block text-xs text-muted-foreground/80">{moduleTitle}</span>
                          </span>
                        </div>
                      </button>
                    ))}
                    {globalHits.length === 0 && (
                      <p className="text-sm text-muted-foreground text-center py-8">{t('noMatchingEndpoint')}</p>
                    )}
                  </div>
                </>
              ) : activeModule === 'design' ? (
                <DesignPrinciplesPanel />
              ) : currentModule && (
                <>
                  <div>
                    <h2 className="text-xl font-semibold">{currentModule.title}</h2>
                    <p className="text-sm text-muted-foreground mt-1">{currentModule.description}</p>
                    <div className="flex flex-wrap items-center gap-2 mt-3">
                      <Badge variant="outline" className="font-mono text-xs">{currentModule.baseUrl}</Badge>
                      {(() => {
                        const { described, total } = countModuleSummaryCoverage(currentModule)
                        if (total === 0) return null
                        return (
                          <Badge
                            variant="outline"
                            className={cn(
                              'text-xs',
                              described < total
                                ? 'border-amber-300 text-amber-800 dark:border-amber-700 dark:text-amber-300'
                                : undefined
                            )}
                          >
                            {t('describedEndpointsRatio', { described, total })}
                          </Badge>
                        )
                      })()}
                    </div>
                  </div>

                  {currentModule.overview && <DomainOverviewPanel overview={currentModule.overview} />}

                  <div className="space-y-6">
                    {currentModule.sections.map(section => (
                      <SectionBlock
                        key={section.id}
                        section={section}
                        activeKey={activeKey}
                        onOpen={(ep) => openEndpoint(ep)}
                        onClose={closeEndpoint}
                      />
                    ))}
                  </div>
                </>
              )}
            </div>
          )}
        </main>
      </div>
    </div>
  )
}
