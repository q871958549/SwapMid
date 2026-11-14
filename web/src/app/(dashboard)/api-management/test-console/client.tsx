'use client'

import { useEffect, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { History, Loader2, Send, Trash2 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Combobox } from '@/components/ui/combobox'
import { PageHeader } from '@/components/data-table/page-header'
import { useAuthenticatedBrowserStorage } from '@/components/providers/session-identity-provider'
import {
  authenticatedBrowserStoragePolicies,
  type AuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'

interface CatalogOperation {
  method: string
  path: string
  summary: string
  tag: string
}

export interface PersistedHistoryEntry {
  method: string
  path: string
  at: number
}

interface HistoryEntry extends PersistedHistoryEntry {
  body: string
  headersText: string
}

interface ConsoleResponse {
  status: number
  durationMs: number
  contentType: string | null
  body: string
}

const METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH']
const MAX_HISTORY_ENTRIES = 20
const MAX_HISTORY_PATH_LENGTH = 2_048
const HISTORY_POLICY = authenticatedBrowserStoragePolicies.apiConsoleHistory

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isPersistedHistoryEntry(value: unknown, now: number): value is PersistedHistoryEntry {
  if (!isPlainRecord(value)) return false
  const keys = Object.keys(value)
  if (
    keys.length !== 3
    || !keys.every((key) => key === 'method' || key === 'path' || key === 'at')
  ) {
    return false
  }

  return typeof value.method === 'string'
    && METHODS.includes(value.method)
    && typeof value.path === 'string'
    && value.path.length > 0
    && value.path.length <= MAX_HISTORY_PATH_LENGTH
    && value.path.trim() === value.path
    && !/[\u0000-\u001F\u007F]/.test(value.path)
    && typeof value.at === 'number'
    && Number.isInteger(value.at)
    && value.at > 0
    && value.at <= now
}

export function isApiConsoleHistory(value: unknown): value is PersistedHistoryEntry[] {
  if (!Array.isArray(value) || value.length > MAX_HISTORY_ENTRIES) return false
  const now = Date.now()
  return Number.isFinite(now)
    && value.every((entry) => isPersistedHistoryEntry(entry, now))
}

export function readApiConsoleHistory(
  storage: AuthenticatedBrowserStorage,
): PersistedHistoryEntry[] {
  return storage.read(HISTORY_POLICY, isApiConsoleHistory) ?? []
}

export function writeApiConsoleHistory(
  storage: AuthenticatedBrowserStorage,
  history: readonly HistoryEntry[],
): boolean {
  const metadata = history.map(({ method, path, at }) => ({ method, path, at }))
  return storage.write(HISTORY_POLICY, metadata)
}

export function clearApiConsoleHistory(storage: AuthenticatedBrowserStorage): void {
  storage.remove(HISTORY_POLICY)
}

function methodVariant(method: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (method) {
    case 'GET': return 'secondary'
    case 'DELETE': return 'destructive'
    default: return 'outline'
  }
}

function statusVariant(code: number): 'success' | 'warning' | 'destructive' | 'secondary' {
  if (code < 300) return 'success'
  if (code < 500) return 'warning'
  return 'destructive'
}

function prettyBody(body: string): string {
  try {
    return JSON.stringify(JSON.parse(body), null, 2)
  } catch {
    return body
  }
}

function toForwardUrl(path: string): string {
  const p = path.startsWith('/') ? path : `/${path}`
  return `/api/proxy${p}`
}

function parseHeaders(text: string): Record<string, string> {
  const headers: Record<string, string> = {}
  for (const line of text.split('\n')) {
    const idx = line.indexOf(':')
    if (idx > 0) {
      const key = line.slice(0, idx).trim()
      const value = line.slice(idx + 1).trim()
      if (key) headers[key] = value
    }
  }
  return headers
}

export function TestConsoleClient() {
  const t = useTranslations('ApiManagement.testConsole')
  const locale = useLocale()
  const browserStorage = useAuthenticatedBrowserStorage()
  const [catalog, setCatalog] = useState<CatalogOperation[]>([])
  const [method, setMethod] = useState('GET')
  const [path, setPath] = useState('/api/abp/application-configuration')
  const [body, setBody] = useState('')
  const [headersText, setHeadersText] = useState('')
  const [sending, setSending] = useState(false)
  const [response, setResponse] = useState<ConsoleResponse | null>(null)
  const [history, setHistory] = useState<HistoryEntry[]>([])

  useEffect(() => {
    fetch('/api/proxy/swagger/v1/swagger.json')
      .then((r) => (r.ok ? r.json() : null))
      .then((spec) => {
        if (!spec?.paths) return
        const ops: CatalogOperation[] = []
        for (const [p, item] of Object.entries(spec.paths as Record<string, Record<string, { summary?: string; tags?: string[] }>>)) {
          for (const [m, op] of Object.entries(item)) {
            if (!METHODS.includes(m.toUpperCase())) continue
            ops.push({ method: m.toUpperCase(), path: p, summary: op.summary ?? '', tag: op.tags?.[0] ?? '' })
          }
        }
        ops.sort((a, b) => a.tag.localeCompare(b.tag) || a.path.localeCompare(b.path))
        setCatalog(ops)
      })
      .catch(() => {})
  }, [])

  useEffect(() => {
    void Promise.resolve().then(() => {
      setHistory(readApiConsoleHistory(browserStorage).map((entry) => ({
        ...entry,
        body: '',
        headersText: '',
      })))
    })
  }, [browserStorage])

  function applyCatalog(value: string) {
    const op = catalog.find((o) => `${o.method} ${o.path}` === value)
    if (!op) return
    setMethod(op.method)
    setPath(op.path)
  }

  function loadHistory(entry: HistoryEntry) {
    setMethod(entry.method)
    setPath(entry.path)
    setBody(entry.body)
    setHeadersText(entry.headersText)
  }

  function pushHistory(entry: HistoryEntry) {
    const next = [
      entry,
      ...history.filter((h) => !(h.method === entry.method && h.path === entry.path)),
    ].slice(0, MAX_HISTORY_ENTRIES)
    setHistory(next)
    writeApiConsoleHistory(browserStorage, next)
  }

  async function send() {
    if (!path.trim()) return
    setSending(true)
    setResponse(null)
    const started = performance.now()
    try {
      const headers = parseHeaders(headersText)
      const hasBody = method !== 'GET' && body.trim() !== ''
      if (hasBody && !Object.keys(headers).some((k) => k.toLowerCase() === 'content-type')) {
        headers['Content-Type'] = 'application/json'
      }
      const res = await fetch(toForwardUrl(path.trim()), {
        method,
        headers,
        body: hasBody ? body : undefined,
      })
      const text = await res.text()
      setResponse({
        status: res.status,
        durationMs: Math.round(performance.now() - started),
        contentType: res.headers.get('content-type'),
        body: text,
      })
      pushHistory({
        method,
        path: path.trim(),
        body,
        headersText,
        at: Date.now(),
      })
    } catch (e) {
      setResponse({
        status: 0,
        durationMs: Math.round(performance.now() - started),
        contentType: null,
        body: t('requestFailed', { message: e instanceof Error ? e.message : String(e) }),
      })
    } finally {
      setSending(false)
    }
  }

  const catalogOptions = catalog.map((o) => ({
    value: `${o.method} ${o.path}`,
    label: `${o.method} ${o.path}`,
    description: (o.tag || t('otherTag')) + (o.summary ? ` · ${o.summary}` : ''),
  }))

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('pageDescription')}
      />

      <div className="grid gap-4 xl:grid-cols-[1fr_320px]">
        <div className="space-y-4">
          <div className="space-y-3 rounded-lg border bg-card p-4">
            <div className="space-y-1">
              <Label className="text-xs">{t('catalogLabel', { count: catalog.length })}</Label>
              <Combobox
                options={catalogOptions}
                value=""
                onChange={applyCatalog}
                placeholder={t('catalogPlaceholder')}
                searchPlaceholder={t('catalogSearchPlaceholder')}
                emptyText={t('catalogEmptyText')}
              />
            </div>
            <div className="flex gap-2">
              <Select value={method} onValueChange={setMethod}>
                <SelectTrigger className="h-9 w-[110px]"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {METHODS.map((m) => <SelectItem key={m} value={m}>{m}</SelectItem>)}
                </SelectContent>
              </Select>
              <Input
                className="h-9 flex-1 font-mono text-sm"
                placeholder={t('pathPlaceholder')}
                value={path}
                onChange={(e) => setPath(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') void send() }}
              />
              <Button className="h-9" onClick={() => void send()} disabled={sending || !path.trim()}>
                {sending ? <Loader2 className="mr-1.5 size-4 animate-spin" /> : <Send className="mr-1.5 size-4" />}{t('sendButton')}
              </Button>
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div className="space-y-1">
                <Label className="text-xs">{t('bodyLabel')}</Label>
                <Textarea className="min-h-28 font-mono text-xs" placeholder='{ "name": "..." }' value={body} onChange={(e) => setBody(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label className="text-xs">{t('headersLabel')}</Label>
                <Textarea className="min-h-28 font-mono text-xs" placeholder="X-Custom: value" value={headersText} onChange={(e) => setHeadersText(e.target.value)} />
              </div>
            </div>
          </div>

          <div className="rounded-lg border bg-card">
            <div className="flex items-center gap-3 border-b px-4 py-2">
              <p className="text-sm font-medium">{t('responseTitle')}</p>
              {response && (
                <>
                  <Badge variant={statusVariant(response.status)}>{response.status || t('networkError')}</Badge>
                  <span className="text-xs text-muted-foreground">{response.durationMs}ms</span>
                  {response.contentType && <span className="truncate text-xs text-muted-foreground">{response.contentType}</span>}
                </>
              )}
            </div>
            <pre className="max-h-[480px] min-h-32 overflow-auto p-4 font-mono text-xs">
              {response ? (prettyBody(response.body) || t('emptyResponseBody')) : t('responseHint')}
            </pre>
          </div>
        </div>

        <div className="rounded-lg border bg-card">
          <div className="flex items-center justify-between border-b px-4 py-2">
            <p className="flex items-center gap-1.5 text-sm font-medium"><History className="size-4" />{t('recentRequestsTitle')}</p>
            <IconButton
              label={t('clearHistoryLabel')}
              className="size-7"
              disabled={!history.length}
              onClick={() => {
                setHistory([])
                clearApiConsoleHistory(browserStorage)
              }}
            >
              <Trash2 className="size-3.5" />
            </IconButton>
          </div>
          <div className="max-h-[640px] overflow-auto">
            {history.length === 0 ? (
              <p className="px-4 py-6 text-center text-sm text-muted-foreground">{t('noHistoryHint')}</p>
            ) : (
              <ul className="divide-y">
                {history.map((h, i) => (
                  <li key={i}>
                    <button className="block w-full cursor-pointer px-4 py-2 text-left hover:bg-muted/40" onClick={() => loadHistory(h)}>
                      <span className="flex items-center gap-2">
                        <Badge variant={methodVariant(h.method)} className="text-[10px]">{h.method}</Badge>
                        <span className="truncate font-mono text-xs" title={h.path}>{h.path}</span>
                      </span>
                      <span className="mt-0.5 block text-[10px] text-muted-foreground">
                        {new Date(h.at).toLocaleString(locale)}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>
    </>
  )
}
