'use client'

import { useId } from 'react'
import { useTranslations } from 'next-intl'
import { Badge } from '@/components/ui/badge'
import { Checkbox } from '@/components/ui/checkbox'
import { cn } from '@/lib/utils'
import { formatPermName, getPermissionTitle, PermissionGuideKind, type PermNode, type PermissionGuide } from './permission-tree'

export function PermissionKindBadge({ kind }: { kind: number }) {
  const t = useTranslations('Account.permission')
  if (kind === PermissionGuideKind.DataScope) return <Badge variant="outline">{t('kindScope')}</Badge>
  if (kind === PermissionGuideKind.Dangerous) return <Badge variant="destructive">{t('kindDangerous')}</Badge>
  if (kind === PermissionGuideKind.OpsOnly) return <Badge variant="secondary">{t('kindOps')}</Badge>
  return null
}

export function PermissionGuideText({ guide }: { guide?: PermissionGuide }) {
  if (!guide) return null
  return <div className="mt-2 space-y-1 text-xs leading-5 text-muted-foreground">
    <p>{guide.impact}</p>{guide.confuseWith && <p>{guide.confuseWith}</p>}{guide.suggested && <p>{guide.suggested}</p>}
  </div>
}

export function PermissionCountBadge({ grantedCount, totalCount }: { grantedCount: number; totalCount: number }) {
  return <Badge variant={grantedCount > 0 ? 'secondary' : 'outline'} className="shrink-0 tabular-nums">{grantedCount}/{totalCount}</Badge>
}

function PermissionCard({ node, checked, onToggle, disabled, guide, showIdentifiers, base = false }: {
  node: PermNode; checked: boolean; onToggle: (value: boolean) => void; disabled?: boolean
  guide?: PermissionGuide; showIdentifiers?: boolean; base?: boolean
}) {
  const t = useTranslations('Account.permission')
  const id = useId()
  return <div className={cn('rounded-lg border p-3', checked && 'border-primary/30 bg-primary/5', disabled && 'opacity-60')}>
    <div className="flex items-start gap-3">
      <Checkbox id={id} checked={checked} disabled={disabled} onCheckedChange={value => onToggle(value === true)} className="mt-0.5" />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <label htmlFor={id} className="cursor-pointer text-sm font-medium">{base ? t('viewLabel') : getPermissionTitle(node.perm)}</label>
          {guide ? <PermissionKindBadge kind={guide.kind} /> : /[.:]Delete(?:[A-Z][a-zA-Z]*)?$/.test(node.perm.name) && <PermissionKindBadge kind={PermissionGuideKind.Dangerous} />}
        </div>
        {base && <p className="mt-1 text-xs leading-5 text-muted-foreground">{t('basePermissionHint')}</p>}
        {showIdentifiers && <p className="mt-1 break-all font-mono text-xs text-muted-foreground">{formatPermName(node.perm.name)}</p>}
        <PermissionGuideText guide={guide} />
      </div>
    </div>
  </div>
}

export function PermissionActionNode({ node, granted, onToggleNode, disabled = false, guides = {}, showIdentifiers = false }: {
  node: PermNode; granted: Record<string, boolean>; onToggleNode: (node: PermNode, value: boolean) => void
  disabled?: boolean; guides?: Record<string, PermissionGuide>; showIdentifiers?: boolean
}) {
  return <div className="space-y-2">
    <PermissionCard node={node} checked={granted[node.perm.name] === true} onToggle={value => onToggleNode(node, value)} disabled={disabled} guide={guides[node.perm.name]} showIdentifiers={showIdentifiers} />
    {node.children.length > 0 && <div className="space-y-2 border-l pl-3">{node.children.map(child => <PermissionActionNode key={child.perm.name} node={child} granted={granted} onToggleNode={onToggleNode} disabled={disabled} guides={guides} showIdentifiers={showIdentifiers} />)}</div>}
  </div>
}

export function PermissionViewCard({ node, granted, onToggle, disabled, guide, showIdentifiers }: {
  node: PermNode; granted: boolean; onToggle: (value: boolean) => void; disabled?: boolean; guide?: PermissionGuide; showIdentifiers?: boolean
}) {
  return <PermissionCard node={node} checked={granted} onToggle={onToggle} disabled={disabled} guide={guide} showIdentifiers={showIdentifiers} base />
}
