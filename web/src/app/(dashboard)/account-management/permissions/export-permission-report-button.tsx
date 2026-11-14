'use client'

import { useState } from 'react'
import { Download, Loader2 } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import { downloadXlsx } from '@/lib/excel-io'

/** 导出已保存的全部角色授权。未保存勾选仍留在权限页，不进文件。 */
export function ExportPermissionReportButton({ dirty, controlledOpen, onOpenChange }: { dirty: boolean; controlledOpen?: boolean; onOpenChange?: (open: boolean) => void }) {
  const t = useTranslations('Account')
  const [exporting, setExporting] = useState(false)
  const [localOpen, setLocalOpen] = useState(false)
  const open = controlledOpen ?? localOpen
  const setOpen = onOpenChange ?? setLocalOpen

  async function handleExport() {
    setExporting(true)
    try {
      await downloadXlsx('/api/proxy/account-roles/permission-report', t('permission.exportFileName'))
      setOpen(false)
      toast({
        variant: 'success',
        title: t('permission.exportSuccessTitle'),
        description: dirty ? t('permission.exportUnsavedHint') : t('permission.exportContentsHint'),
      })
    } catch (error) {
      console.error(error)
      toast({
        variant: 'destructive',
        title: t('permission.exportFailedTitle'),
        description: getApiErrorMessage(error, t('permission.exportFailedFallback')),
      })
    } finally {
      setExporting(false)
    }
  }

  return <>
    {controlledOpen === undefined && <Button variant="outline" size="sm" onClick={() => setOpen(true)} disabled={exporting}>
      {exporting ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <Download className="mr-1.5 size-3.5" />}
      {exporting ? t('permission.exportingButton') : t('permission.exportButton')}
    </Button>}
    <ConfirmDialog open={open} onOpenChange={setOpen} title={t('permission.exportButton')} description={<div className="space-y-2"><p>{t('permission.exportScopeHint')}</p>{dirty && <p>{t('permission.exportUnsavedHint')}</p>}</div>} confirmText={t('permission.exportConfirm')} busy={exporting} onConfirm={handleExport} />
  </>
}
