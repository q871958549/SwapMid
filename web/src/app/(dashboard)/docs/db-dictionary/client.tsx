'use client'

import { useState, useEffect, useMemo } from 'react'
import { useTranslations } from 'next-intl'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import { useSessionIdentityVersion } from '@/components/providers/session-identity-provider'
import { bindSessionIdentityToUrl } from '@/lib/session-availability'
import {
  Search, Table2, FileSpreadsheet, FileText, KeyRound, ChevronDown, ChevronRight, Database, Server,
} from 'lucide-react'

type DbDictT = ReturnType<typeof useTranslations<'DeveloperDocs.dbDictionary'>>

export interface DbTableSummary {
  name: string
  displayName: string
  domainGroup: string
  columnCount: number
  description: string | null
}

export interface DbColumn {
  name: string
  storeType: string
  clrType: string
  maxLength: number | null
  isNullable: boolean
  isPrimaryKey: boolean
  isForeignKey: boolean
  foreignTable: string | null
  isIndexed: boolean
  defaultValue: string | null
  comment: string | null
  isSystemColumn: boolean
}

export interface DbTableDetail extends DbTableSummary {
  schema: string | null
  columns: DbColumn[]
}

export interface ExternalDbSource {
  key: string
  displayName: string
  dialect: string
}

// 业务域 → 展示名（后端 DomainGroup 取 namespace 第二段、是英文 PascalCase；此处展示层翻译，
// 漏配的回退英文原名）。模块级常量表改工厂函数（同批次既有范式），由持有 t 的调用方求值。
function getDomainLabels(t: DbDictT): Record<string, string> {
  return {
    AccountManagement: t('domainLabels.accountManagement'),
    ApiManagement: t('domainLabels.apiManagement'),
    DataIntegration: t('domainLabels.dataIntegration'),
    DeveloperDocs: t('domainLabels.developerDocs'),
    EnterpriseManagement: t('domainLabels.enterpriseManagement'),
    MessageBus: t('domainLabels.messageBus'),
    Notification: t('domainLabels.notification'),
    Scheduling: t('domainLabels.scheduling'),
    SystemManagement: t('domainLabels.systemManagement'),
  }
}
const groupLabel = (g: string, labels: Record<string, string>) => labels[g] ?? g

// 搜索节流：外部库的表清单查询直接打到外部数据库元数据视图，每次击键发一次会把
// 一次浏览放大成十几次跨库查询。平台库虽是内存缓存，同样没有必要每键一请求。
const SEARCH_DEBOUNCE_MS = 300

const TH = 'px-3 py-2 font-medium'

function ColumnRow({ c, onSelectTable }: { c: DbColumn; onSelectTable: (tableName: string) => void }) {
  const t = useTranslations('DeveloperDocs.dbDictionary')
  return (
    <tr className="hover:bg-muted/20">
      <td className="px-3 py-2 font-mono text-xs">{c.name}</td>
      <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
        {c.storeType}
        {c.defaultValue ? (
          <div className="text-[10px] text-muted-foreground/70">= {c.defaultValue}</div>
        ) : null}
      </td>
      <td className="px-3 py-2">{c.isNullable ? t('yes') : t('no')}</td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap gap-1">
          {c.isPrimaryKey && (
            <Badge variant="outline" className="gap-0.5 px-1 text-[10px]">
              <KeyRound className="size-3" />PK
            </Badge>
          )}
          {c.isForeignKey && (
            <Badge
              variant="outline"
              className={cn('px-1 text-[10px]', c.foreignTable && 'cursor-pointer hover:bg-muted')}
              title={c.foreignTable ?? ''}
              onClick={() => { if (c.foreignTable) onSelectTable(c.foreignTable) }}
            >
              FK{c.foreignTable ? ` → ${c.foreignTable}` : ''}
            </Badge>
          )}
          {c.isIndexed && (
            <Badge variant="outline" className="px-1 text-[10px]">{t('indexedBadge')}</Badge>
          )}
        </div>
      </td>
      <td className="px-3 py-2">
        {c.comment
          ? <span className="text-foreground/80">{c.comment}</span>
          : <span className="text-muted-foreground/40">{t('pendingDescription')}</span>}
      </td>
    </tr>
  )
}

function SourceTab({ active, onClick, icon, label }: {
  active: boolean; onClick: () => void; icon: React.ReactNode; label: string
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs transition-colors',
        active ? 'border-primary bg-primary/10 text-primary font-medium' : 'text-muted-foreground hover:bg-muted',
      )}
    >
      {icon}
      {label}
    </button>
  )
}

export function DbDictionaryClient({ platformTables, sources }: {
  platformTables: DbTableSummary[]
  sources: ExternalDbSource[]
}) {
  const t = useTranslations('DeveloperDocs.dbDictionary')
  const identityVersion = useSessionIdentityVersion()
  const excelExportUrl = bindSessionIdentityToUrl(
    '/api/proxy/db-schema/export/excel',
    identityVersion,
  )
  const markdownExportUrl = bindSessionIdentityToUrl(
    '/api/proxy/db-schema/export/markdown',
    identityVersion,
  )
  const tc = useTranslations('Common')
  const domainLabels = getDomainLabels(t)
  const [currentSource, setCurrentSource] = useState<string | null>(null) // null = 平台库
  const [externalTables, setExternalTables] = useState<DbTableSummary[]>([])
  const [externalLoading, setExternalLoading] = useState(false)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<string | null>(platformTables[0]?.name ?? null)
  const [detail, setDetail] = useState<DbTableDetail | null>(null)
  const [showSystem, setShowSystem] = useState(false)
  const [remoteMatches, setRemoteMatches] = useState<DbTableSummary[] | null>(null)

  const isPlatform = currentSource === null
  const tables = isPlatform ? platformTables : externalTables

  // 切外部数据源 → 加载其表清单 + 重置选中（setState 全在 async IIFE，规避 set-state-in-effect）
  useEffect(() => {
    let active = true
    void (async () => {
      if (currentSource === null) {
        if (active) { setExternalTables([]); setExternalLoading(false) }
        return
      }
      if (active) setExternalLoading(true)
      try {
        const res = await fetch(`/api/proxy/db-schema/external/${encodeURIComponent(currentSource)}/tables`)
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as DbTableSummary[]
        if (active) { setExternalTables(data); setSelected(data[0]?.name ?? null) }
      } catch {
        if (active) { setExternalTables([]); setSelected(null) }
      } finally {
        if (active) setExternalLoading(false)
      }
    })()
    return () => { active = false }
  }, [currentSource])

  // 选表 → 加载字段明细（平台 / 外部端点不同）
  useEffect(() => {
    let active = true
    void (async () => {
      if (!selected) { if (active) setDetail(null); return }
      try {
        const url = isPlatform
          ? `/api/proxy/db-schema/tables/${encodeURIComponent(selected)}`
          : `/api/proxy/db-schema/external/${encodeURIComponent(currentSource!)}/tables/${encodeURIComponent(selected)}`
        const res = await fetch(url)
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as DbTableDetail
        if (active) setDetail(data)
      } catch {
        if (active) setDetail(null)
      }
    })()
    return () => { active = false }
  }, [selected, currentSource, isPlatform])

  // 关键字匹配范围：平台库含字段名/中文含义（后端内存缓存里筛），外部库在 DB 侧按表名/表注释筛。
  // 结果与本地表名过滤合并，避免输入瞬间空白；请求本身按 SEARCH_DEBOUNCE_MS 节流。
  useEffect(() => {
    let active = true
    const timer = window.setTimeout(() => {
      void (async () => {
        const kw = search.trim()
        if (!kw) {
          if (active) setRemoteMatches(null)
          return
        }
        try {
          const url = isPlatform
            ? `/api/proxy/db-schema/tables?filter=${encodeURIComponent(kw)}`
            : `/api/proxy/db-schema/external/${encodeURIComponent(currentSource!)}/tables?filter=${encodeURIComponent(kw)}`
          const res = await fetch(url)
          if (!res.ok) throw new Error()
          const data = (await res.json()) as DbTableSummary[]
          if (active) setRemoteMatches(data)
        } catch {
          if (active) setRemoteMatches(null)
        }
      })()
    }, SEARCH_DEBOUNCE_MS)
    return () => {
      active = false
      window.clearTimeout(timer)
    }
  }, [search, currentSource, isPlatform])

  const grouped = useMemo(() => {
    const kw = search.trim().toLowerCase()
    const nameFiltered = kw
      ? tables.filter((tbl) => tbl.name.toLowerCase().includes(kw) || tbl.displayName.toLowerCase().includes(kw))
      : tables
    const merged = new Map<string, DbTableSummary>()
    for (const tbl of nameFiltered) merged.set(tbl.name, tbl)
    if (kw && remoteMatches) {
      for (const tbl of remoteMatches) merged.set(tbl.name, tbl)
    }
    const map = new Map<string, DbTableSummary[]>()
    for (const tbl of merged.values()) {
      const list = map.get(tbl.domainGroup) ?? []
      list.push(tbl)
      map.set(tbl.domainGroup, list)
    }
    return Array.from(map.entries())
  }, [tables, search, remoteMatches])

  const detailLoading = selected != null && detail?.name !== selected
  const businessCols = detail?.columns.filter((c) => !c.isSystemColumn) ?? []
  const systemCols = detail?.columns.filter((c) => c.isSystemColumn) ?? []
  const withComment = businessCols.filter((c) => c.comment).length

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <div className="flex items-center justify-between gap-4 shrink-0">
        <div>
          <h1 className="text-xl font-semibold">{t('pageTitle')}</h1>
          <p className="text-sm text-muted-foreground">
            {t('pageDescription', { count: platformTables.length })}
          </p>
        </div>
        {isPlatform && (
          <div className="flex gap-2">
            <Button variant="outline" size="sm" asChild>
              <a href={excelExportUrl} download>
                <FileSpreadsheet className="size-4" /> {t('exportExcel')}
              </a>
            </Button>
            <Button variant="outline" size="sm" asChild>
              <a href={markdownExportUrl} download>
                <FileText className="size-4" /> {t('exportMarkdown')}
              </a>
            </Button>
          </div>
        )}
      </div>

      {/* 数据源切换器 */}
      {sources.length > 0 && (
        <div className="flex flex-wrap items-center gap-1.5 shrink-0">
          <span className="mr-1 text-xs text-muted-foreground">{t('dataSourceLabel')}</span>
          <SourceTab active={isPlatform} onClick={() => { setCurrentSource(null); setSelected(platformTables[0]?.name ?? null) }}
            icon={<Database className="size-3.5" />} label={t('platformDatabase')} />
          {sources.map((s) => (
            <SourceTab key={s.key} active={currentSource === s.key} onClick={() => setCurrentSource(s.key)}
              icon={<Server className="size-3.5" />} label={s.displayName} />
          ))}
          {!isPlatform && (
            <span className="ml-2 text-xs text-muted-foreground/60">{t('externalCommentHint')}</span>
          )}
        </div>
      )}

      <div className="flex min-h-0 flex-1 gap-4">
        {/* 左：表列表 */}
        <aside className="flex w-72 shrink-0 flex-col rounded-lg border">
          <div className="border-b p-2">
            <div className="relative">
              <Search className="absolute left-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input value={search} onChange={(e) => setSearch(e.target.value)}
                placeholder={t('searchPlaceholder')} className="h-8 pl-8" />
            </div>
          </div>
          <div className="min-h-0 flex-1 overflow-auto p-2">
            {externalLoading && (
              <p className="p-4 text-center text-sm text-muted-foreground">{tc('loading')}</p>
            )}
            {!externalLoading && grouped.length === 0 && (
              <p className="p-4 text-center text-sm text-muted-foreground">
                {isPlatform ? t('noMatchingTables') : t('noExternalTablesOrConfig')}
              </p>
            )}
            {grouped.map(([group, items]) => (
              <div key={group} className="mb-4">
                <div className="mb-1 flex items-baseline gap-1.5 px-1">
                  <span className="text-xs font-semibold tracking-wide text-foreground/70">
                    {groupLabel(group, domainLabels)}
                  </span>
                  <span className="text-[10px] tabular-nums text-muted-foreground/50">{items.length}</span>
                </div>
                <div className="flex flex-col gap-0.5">
                  {items.map((tbl) => (
                    <button key={tbl.name} onClick={() => setSelected(tbl.name)}
                      className={cn(
                        'flex w-full flex-col gap-0.5 rounded-md px-2 py-1.5 text-left hover:bg-muted',
                        selected === tbl.name && 'bg-muted ring-1 ring-border',
                      )}>
                      <span className="truncate text-sm leading-tight">{tbl.displayName}</span>
                      <span className="truncate font-mono text-[10px] leading-tight text-muted-foreground">{tbl.name}</span>
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </aside>

        {/* 右：字段明细 */}
        <main className="min-w-0 flex-1 overflow-auto rounded-lg border">
          {!selected && (
            <p className="p-8 text-center text-sm text-muted-foreground">{t('selectTableFromLeft')}</p>
          )}
          {detailLoading && (
            <p className="p-8 text-center text-sm text-muted-foreground">{tc('loading')}</p>
          )}
          {detail && detail.name === selected && (
            <div>
              <div className="border-b bg-muted/30 p-4">
                <div className="flex flex-wrap items-center gap-2">
                  <Table2 className="size-5 text-muted-foreground" />
                  <h2 className="text-lg font-semibold">{detail.displayName}</h2>
                  <Badge variant="outline" className="font-mono">{detail.name}</Badge>
                  <Badge variant="secondary">{groupLabel(detail.domainGroup, domainLabels)}</Badge>
                </div>
                {detail.description && detail.description !== detail.displayName && (
                  <p className="mt-1.5 text-sm text-muted-foreground">{detail.description}</p>
                )}
              </div>

              <div className="flex items-center justify-between px-4 pb-1 pt-3">
                <span className="text-xs font-semibold text-muted-foreground">
                  {isPlatform ? t('platformFieldsCount', { count: businessCols.length }) : t('externalFieldsCount', { count: businessCols.length })}
                </span>
                <span className="text-xs text-muted-foreground/70">
                  {t('describedFieldsRatio', { described: withComment, total: businessCols.length })}
                </span>
              </div>
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-xs text-muted-foreground">
                  <tr className="border-y text-left">
                    <th className={TH}>{t('fieldColumn')}</th>
                    <th className={TH}>{tc('type')}</th>
                    <th className={cn(TH, 'w-14')}>{t('nullableColumn')}</th>
                    <th className={cn(TH, 'w-24')}>{t('keyColumn')}</th>
                    <th className={TH}>{t('descriptionColumn')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {businessCols.length === 0 && (
                    <tr>
                      <td colSpan={5} className="px-3 py-4 text-center text-muted-foreground">{t('noFields')}</td>
                    </tr>
                  )}
                  {businessCols.map((c) => (
                    <ColumnRow key={c.name} c={c} onSelectTable={setSelected} />
                  ))}
                </tbody>
              </table>

              {systemCols.length > 0 && (
                <div className="border-t">
                  <button onClick={() => setShowSystem((v) => !v)}
                    className="flex w-full items-center gap-1.5 px-4 py-2 text-xs font-semibold text-muted-foreground hover:bg-muted/30">
                    {showSystem ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                    {t('systemFieldsToggle', { count: systemCols.length })}
                    <span className="font-normal text-muted-foreground/60">{t('systemFieldsHint')}</span>
                  </button>
                  {showSystem && (
                    <table className="w-full text-sm">
                      <tbody className="divide-y border-t">
                        {systemCols.map((c) => (
                          <ColumnRow key={c.name} c={c} onSelectTable={setSelected} />
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>
              )}
            </div>
          )}
        </main>
      </div>
    </div>
  )
}
