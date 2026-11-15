'use client'

import { useState } from 'react'
import { useRouter } from 'next/navigation'
import { useTranslations } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { Copy, KeyRound, ShieldCheck, SlidersHorizontal } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { actionsColumn } from '@/components/data-table/column-factories'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'

export interface ThirdPartyClient {
  clientId: string
  displayName?: string | null
  scopes: string[]
  allowedGroupIds?: string[]
  rateLimitPerMinute?: number | null
}

export interface ApiGroupOption {
  id: string
  name: string
}

interface Credential {
  clientId: string
  clientSecret: string
}

const BASE = '/api/proxy/third-party-clients'
const MIN_RATE_LIMIT = 1
const MAX_RATE_LIMIT = 60_000

export function ThirdPartyClientsClient({
  initial,
  groups = [],
}: {
  initial: ThirdPartyClient[]
  groups?: ApiGroupOption[]
}) {
  const t = useTranslations('ApiManagement.clients')
  const tc = useTranslations('Common')
  const router = useRouter()
  const [rows, setRows] = useState<ThirdPartyClient[]>(initial)

  const [createOpen, setCreateOpen] = useState(false)
  const [displayName, setDisplayName] = useState('')
  // eslint-disable-next-line swapmid/prefer-crud-form -- 创建返回一次性明文凭据(client_secret)须弹窗展示，useCrudForm.onSuccess 不透出响应体、接不住凭据，特殊表单手写反馈（规则文档指定逃生舱）
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const [credential, setCredential] = useState<Credential | null>(null)

  const [policyTarget, setPolicyTarget] = useState<ThirdPartyClient | null>(null)
  const [rateLimitText, setRateLimitText] = useState('')
  const [selectedGroupIds, setSelectedGroupIds] = useState<string[]>([])
  const [policyError, setPolicyError] = useState<string | null>(null)
  const [savingPolicy, setSavingPolicy] = useState(false)

  const [resetTarget, setResetTarget] = useState<string | null>(null)
  const [resetting, setResetting] = useState(false)

  const [deleteTarget, setDeleteTarget] = useState<string | null>(null)
  const [deleting, setDeleting] = useState(false)

  const groupNameMap = new Map(groups.map(group => [group.id, group.name]))

  async function reload() {
    try {
      const res = await fetch(BASE)
      if (res.ok) setRows(await res.json())
    } catch {
      /* 保留当前列表；写操作已各自 toast */
    }
    router.refresh() // 失效 Router Cache，切页签返回不显旧列表
  }

  function openPolicyDialog(client: ThirdPartyClient) {
    setPolicyTarget(client)
    setRateLimitText(client.rateLimitPerMinute ? String(client.rateLimitPerMinute) : '')
    setSelectedGroupIds([...(client.allowedGroupIds ?? [])])
    setPolicyError(null)
  }

  function toggleAllowedGroup(groupId: string, checked: boolean) {
    setSelectedGroupIds((prev) => {
      if (checked) return prev.includes(groupId) ? prev : [...prev, groupId]
      return prev.filter(item => item !== groupId)
    })
  }

  async function handleSavePolicy() {
    if (!policyTarget) return
    const trimmedRate = rateLimitText.trim()
    let parsedRate: number | null = null
    if (trimmedRate.length > 0) {
      const num = Number(trimmedRate)
      if (!Number.isInteger(num) || num < MIN_RATE_LIMIT || num > MAX_RATE_LIMIT) {
        setPolicyError(t('policyRateInvalid'))
        return
      }
      parsedRate = num
    }

    setSavingPolicy(true)
    setPolicyError(null)
    try {
      const res = await fetch(`${BASE}/${encodeURIComponent(policyTarget.clientId)}/policy`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          allowedGroupIds: selectedGroupIds,
          rateLimitPerMinute: parsedRate,
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      setPolicyTarget(null)
      toast({ variant: 'success', title: t('policySavedTitle') })
      void reload()
    } catch (e) {
      setPolicyError(getApiErrorMessage(e, tc('tryAgainLater')))
    } finally {
      setSavingPolicy(false)
    }
  }

  async function handleCreate() {
    const name = displayName.trim()
    if (!name) {
      setFormError(t('nameRequired'))
      return
    }
    setSubmitting(true)
    setFormError(null)
    try {
      const res = await fetch(BASE, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ displayName: name }),
      })
      if (!res.ok) throw new Error(await res.text())
      const cred: Credential = await res.json()
      setCreateOpen(false)
      setDisplayName('')
      setCredential(cred)
      toast({ variant: 'success', title: t('createdTitle') })
      void reload()
    } catch (e) {
      setFormError(getApiErrorMessage(e, t('createFailedFallback')))
    } finally {
      setSubmitting(false)
    }
  }

  async function handleReset() {
    if (!resetTarget) return
    setResetting(true)
    try {
      const res = await fetch(`${BASE}/${encodeURIComponent(resetTarget)}/reset-secret`, { method: 'POST' })
      if (!res.ok) throw new Error(await res.text())
      const cred: Credential = await res.json()
      setResetTarget(null)
      setCredential(cred)
      toast({ variant: 'success', title: t('resetSucceededTitle') })
      void reload()
    } catch (e) {
      toast({ variant: 'destructive', title: t('resetFailedTitle'), description: getApiErrorMessage(e, tc('tryAgainLater')) })
    } finally {
      setResetting(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      const res = await fetch(`${BASE}/${encodeURIComponent(deleteTarget)}`, { method: 'DELETE' })
      if (!res.ok) throw new Error(await res.text())
      setDeleteTarget(null)
      toast({ variant: 'success', title: t('deletedTitle') })
      void reload()
    } catch (e) {
      toast({ variant: 'destructive', title: tc('deleteFailedTitle'), description: getApiErrorMessage(e, tc('tryAgainLater')) })
    } finally {
      setDeleting(false)
    }
  }

  function copy(text: string, label: string) {
    void navigator.clipboard.writeText(text).then(() => toast({ variant: 'success', title: t('copiedTitle', { label }) }))
  }

  const columns: ColumnDef<ThirdPartyClient, unknown>[] = [
    {
      accessorKey: 'clientId',
      header: ({ column }) => <DataTableColumnHeader column={column} title="Client ID" />,
      cell: ({ row }) => <span className="font-mono text-xs">{row.original.clientId}</span>,
    },
    {
      accessorKey: 'displayName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnDisplayName')} />,
    },
    {
      accessorKey: 'scopes',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnScopes')} />,
      enableSorting: false,
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {row.original.scopes.map((s) => (
            <Badge key={s} variant="secondary">{s}</Badge>
          ))}
        </div>
      ),
    },
    {
      id: 'gatewayPolicy',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnGatewayPolicy')} />,
      enableSorting: false,
      cell: ({ row }) => {
        const rate = row.original.rateLimitPerMinute
        const allowed = row.original.allowedGroupIds ?? []
        return (
          <div className="flex flex-wrap items-center gap-1.5">
            <Badge variant="outline">
              {rate ? t('policyRateBadge', { count: rate }) : t('policyRateDefaultBadge')}
            </Badge>
            <Badge variant={allowed.length > 0 ? 'secondary' : 'outline'}>
              {allowed.length > 0
                ? allowed.length === 1 && groupNameMap.has(allowed[0])
                  ? groupNameMap.get(allowed[0])
                  : t('policyGroupCountBadge', { count: allowed.length })
                : t('policyAllGroupsBadge')}
            </Badge>
          </div>
        )
      },
    },
    actionsColumn<ThirdPartyClient>({
      onDelete: (row) => setDeleteTarget(row.clientId),
      extraItems: (row) => (
        <>
          <DropdownMenuItem onClick={() => openPolicyDialog(row)}>
            <SlidersHorizontal className="mr-2 size-4" />
            {t('gatewayPolicyMenuItem')}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => setResetTarget(row.clientId)}>
            <KeyRound className="mr-2 size-4" />
            {t('resetSecretMenuItem')}
          </DropdownMenuItem>
        </>
      ),
    }),
  ]

  return (
    <>
      <PageHeader
        title={t('pageTitle')}
        description={t('pageDescription')}
        action={
          <CreateButton
            label={t('createLabel')}
            onClick={() => {
              setDisplayName('')
              setFormError(null)
              setCreateOpen(true)
            }}
          />
        }
      />

      <DataTable columns={columns} data={rows} tableKey="third-party-clients" searchPlaceholder={t('searchPlaceholder')} />

      {/* 新建应用 */}
      <Dialog open={createOpen} onOpenChange={(o) => { if (!submitting) { setCreateOpen(o); if (!o) setFormError(null) } }}>
        <DialogContent aria-describedby={undefined}>
          <DialogHeader>
            <DialogTitle>{t('createDialogTitle')}</DialogTitle>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label>{t('columnDisplayName')} *</Label>
            <Input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder={t('namePlaceholder')}
              onKeyDown={(e) => { if (e.key === 'Enter') handleCreate() }}
              autoFocus
            />
            <p className="text-xs text-muted-foreground">
              {t('issueHint')}
            </p>
          </div>
          {formError && (
            <p className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">{formError}</p>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)} disabled={submitting}>{tc('cancel')}</Button>
            <Button onClick={handleCreate} disabled={submitting}>{submitting ? t('creating') : t('createButton')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 网关访问策略（限流与路由分组 ACL） */}
      <Dialog
        open={!!policyTarget}
        onOpenChange={(o) => {
          if (!savingPolicy && !o) {
            setPolicyTarget(null)
            setPolicyError(null)
          }
        }}
      >
        <DialogContent className="sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle>{t('policyDialogTitle')}</DialogTitle>
            <DialogDescription>
              {t('policyDialogDescription', { clientId: policyTarget?.clientId ?? '' })}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-1.5">
              <Label>{t('policyRateFieldLabel')}</Label>
              <Input
                type="number"
                min={MIN_RATE_LIMIT}
                max={MAX_RATE_LIMIT}
                value={rateLimitText}
                onChange={(e) => setRateLimitText(e.target.value)}
                placeholder={t('policyRatePlaceholder')}
                className="font-mono"
              />
              <p className="text-xs text-muted-foreground">{t('policyRateHint')}</p>
            </div>
            <div className="space-y-2">
              <Label>{t('policyGroupsFieldLabel')}</Label>
              <p className="text-xs text-muted-foreground">{t('policyGroupsHint')}</p>
              {groups.length === 0 ? (
                <p className="rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
                  {t('policyGroupsEmptyHint')}
                </p>
              ) : (
                <div className="max-h-44 space-y-2 overflow-y-auto rounded-md border p-3">
                  {groups.map((group) => {
                    const checked = selectedGroupIds.includes(group.id)
                    const checkboxId = `gw-group-${group.id}`
                    return (
                      <div key={group.id} className="flex items-center gap-2">
                        <Checkbox
                          id={checkboxId}
                          checked={checked}
                          onCheckedChange={(val) => toggleAllowedGroup(group.id, val === true)}
                        />
                        <Label htmlFor={checkboxId} className="cursor-pointer text-sm font-normal">
                          {group.name}
                        </Label>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
            {policyError && (
              <p role="alert" className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
                {policyError}
              </p>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPolicyTarget(null)} disabled={savingPolicy}>
              {tc('cancel')}
            </Button>
            <Button onClick={handleSavePolicy} disabled={savingPolicy}>
              {savingPolicy ? tc('saving') : tc('save')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 凭据展示（创建/重置后一次性明文） */}
      <Dialog open={!!credential} onOpenChange={(o) => { if (!o) setCredential(null) }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="size-5 text-primary" />
              {t('credentialDialogTitle')}
            </DialogTitle>
            <DialogDescription className="text-destructive">
              {t('credentialDialogWarning')}
            </DialogDescription>
          </DialogHeader>
          {credential && (
            <div className="space-y-3">
              <CredentialField label="Client ID" value={credential.clientId} onCopy={copy} />
              <CredentialField label="Client Secret" value={credential.clientSecret} onCopy={copy} />
              <div className="rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
                <p className="mb-1 font-medium text-foreground">{t('usageTitle')}</p>
                <p>{t.rich('usageStep1', { code: (chunks) => <code>{chunks}</code> })}</p>
                {/* authHeader 含字面尖括号 <token>，作为参数值传入（非模板内字面量），避开 rich text 标签解析歧义 */}
                <p>{t.rich('usageStep2', { code: (chunks) => <code>{chunks}</code>, authHeader: 'Authorization: Bearer <token>' })}</p>
              </div>
            </div>
          )}
          <DialogFooter>
            <Button onClick={() => setCredential(null)}>{t('savedConfirmButton')}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* 重置密钥确认 */}
      <ConfirmDialog
        open={!!resetTarget}
        onOpenChange={(o) => { if (!o) setResetTarget(null) }}
        title={t('resetConfirmTitle')}
        description={<>{t('resetConfirmDescription', { clientId: resetTarget ?? '' })}</>}
        confirmText={t('resetButton')}
        busyText={t('resetting')}
        busy={resetting}
        onConfirm={handleReset}
      />

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!deleteTarget}
        onOpenChange={(o) => { if (!o) setDeleteTarget(null) }}
        title={t('deleteConfirmTitle')}
        description={<>{t('deleteConfirmDescription', { clientId: deleteTarget ?? '' })}</>}
        confirmText={tc('delete')}
        busyText={tc('deleting')}
        destructive
        busy={deleting}
        onConfirm={handleDelete}
      />
    </>
  )
}

function CredentialField({
  label, value, onCopy,
}: { label: string; value: string; onCopy: (text: string, label: string) => void }) {
  const t = useTranslations('ApiManagement.clients')
  return (
    <div className="space-y-1">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      <div className="flex items-center gap-2">
        <code className="flex-1 truncate rounded-md border bg-muted px-3 py-2 font-mono text-sm">{value}</code>
        <IconButton label={t('copyLabel', { label })} variant="outline" onClick={() => onCopy(value, label)}>
          <Copy className="size-4" />
        </IconButton>
      </div>
    </div>
  )
}
