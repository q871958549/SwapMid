'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { Badge } from '@/components/ui/badge'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import type { CompanyDepartmentDto } from '@/lib/api/types'

/**
 * 可复用「部门单元格」：在任何列表的部门列里点击即弹出部门详情（懒加载 /departments/{id}）。
 * 无 departmentId → 退化为纯文本徽标（不可点）；无名称 → 显示 fallback。
 * 所有渲染部门列的列表都应统一用本组件（约定见 web CLAUDE.md / 记忆 department-cell）。
 */
export function DepartmentCell({
  departmentId,
  departmentName,
  fallback = '—',
}: {
  departmentId?: string | null
  departmentName?: string | null
  fallback?: string
}) {
  const t = useTranslations('Common')
  const [open, setOpen] = useState(false)
  const [detail, setDetail] = useState<CompanyDepartmentDto | null>(null)
  const [loading, setLoading] = useState(false)

  if (!departmentName) return <span className="text-muted-foreground">{fallback}</span>

  // 没有 id 无法取详情，退化为纯文本徽标
  if (!departmentId) {
    return <Badge variant="outline" className="max-w-[200px] truncate">{departmentName}</Badge>
  }

  async function openDetail() {
    setOpen(true)
    if (detail || loading) return
    setLoading(true)
    try {
      const res = await fetch(`/api/proxy/departments/${departmentId}`)
      if (res.ok) setDetail(await res.json())
    } catch {
      // 详情加载失败时弹窗给出提示，不抛错
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <button
        type="button"
        onClick={openDetail}
        className="max-w-[200px] cursor-pointer rounded-md border px-2 py-0.5 text-xs text-foreground transition-colors hover:bg-accent"
        title={t('departmentCell.viewTooltip')}
      >
        <span className="block truncate">{departmentName}</span>
      </button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{t('departmentCell.dialogTitle')}</DialogTitle>
            <DialogDescription className="sr-only">{t('departmentCell.dialogDescription')}</DialogDescription>
          </DialogHeader>
          {loading && !detail ? (
            <p className="text-muted-foreground py-4 text-center text-sm">{t('loading')}</p>
          ) : detail ? (
            <dl className="divide-y text-sm">
              <Row label={t('name')} value={detail.name} />
              <Row label={t('code')} value={detail.code} mono />
              <Row label={t('ownerEnterprise')} value={detail.enterpriseName} />
              <Row label={t('parentDepartment')} value={detail.parentName} />
              <Row label={t('departmentCell.levelPath')} value={detail.fullPath} />
              <Row label={t('departmentCell.leader')} value={detail.leaderEmployeeName} />
              <Row label={t('status')} value={detail.isEnabled ? t('enabled') : t('disabled')} />
            </dl>
          ) : (
            <p className="text-muted-foreground py-4 text-center text-sm">{t('departmentCell.loadFailed')}</p>
          )}
        </DialogContent>
      </Dialog>
    </>
  )
}

function Row({ label, value, mono }: { label: string; value?: string | null; mono?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5">
      <dt className="text-muted-foreground shrink-0">{label}</dt>
      <dd className={`min-w-0 break-all text-right ${mono ? 'font-mono text-xs' : ''}`}>{value || '—'}</dd>
    </div>
  )
}
