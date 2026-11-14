'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { FlaskConical, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'
import type { ScheduledJob } from './columns'
import { buildArgsJson, isParamsValid, ParamFields } from './param-fields'

/**
 * 演练（dry-run）发送：只对已接入统一执行管道的任务可用。演练不写库、不触达正式订阅人，
 * 只把生成的报表发到填写的测试邮箱，用于验证「取数 → 生成 → 分发」链路。
 * 参数字段按后端 schema 动态渲染（F7）——此前硬编码「月份」，每日产出重刷等任务实参是 date，字段名对不上。
 * U3 起为受控组件（target 驱动开关）：触发入口收进操作列「执行 ▾」下拉，DropdownMenuItem 点击后菜单
 * 即卸载，弹窗不能再内嵌在触发按钮里自管状态，由 client.tsx 持有演练目标。
 */
export function DryRunDialog({
  target,
  onClose,
  onDone,
}: {
  /** 演练目标任务；null = 关闭。 */
  target: ScheduledJob | null
  onClose: () => void
  onDone?: () => void
}) {
  const [email, setEmail] = useState('')
  const [paramValues, setParamValues] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)
  // eslint-disable-next-line swapmid/prefer-crud-form -- 演练发送是一次性操作、非实体 CRUD，useCrudForm/CrudFormDialog 不适配；按 AGENTS.md 规则对特殊表单豁免
  const [formError, setFormError] = useState<string | null>(null)
  const t = useTranslations('System.scheduledJobs')
  const tc = useTranslations('Common')

  function handleOpenChange(v: boolean) {
    if (!v) {
      onClose()
      // 关闭即清空（下次打开是全新演练）；打开时序由 target 赋值驱动，无需 open 分支。
      setEmail('')
      setParamValues({})
      setFormError(null)
    }
  }

  async function submit() {
    if (!target) return
    setSubmitting(true)
    setFormError(null)
    try {
      const res = await fetch('/api/proxy/scheduled-jobs/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          key: target.key,
          dryRun: true,
          testRecipient: email.trim(),
          argsJson: buildArgsJson(target.parameters, paramValues),
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      const result = (await res.json()) as {
        success: boolean
        summary: string | null
        error: string | null
        status?: number | null
      }
      if (!result.success) throw new Error(result.error ?? t('dryRunDialog.runFailedDefaultMessage'))
      handleOpenChange(false)
      if (result.status === 4) {
        toast({
          variant: 'warning',
          title: t('statusLabels.skipped'),
          description: `${target.displayName}：${result.summary ?? t('toastRunSkippedDescriptionFallback')}`,
        })
        onDone?.()
        return
      }
      // 演练「成功」但结果为空（该时间范围库中无数据）极易误导——升级为警示样式并给出下一步指引，
      // 而非与正常演练同款绿色 toast 一晃而过。
      // 逻辑用途裸中文：匹配后端返回摘要里的「行/条」计数判断结果是否为空，翻译该匹配串会致判断失效，不翻译。
      const isEmpty = /\b0\s*(行|条)\b/.test(result.summary ?? '')
      toast(
        isEmpty
          ? {
              variant: 'warning',
              title: t('dryRunDialog.toastEmptyTitle'),
              description: t('dryRunDialog.toastEmptyDescriptionTemplate', { name: target.displayName, summary: result.summary ?? '' }),
            }
          : { variant: 'success', title: t('dryRunDialog.toastSuccessTitle'), description: `${target.displayName}：${result.summary ?? t('dryRunDialog.toastSuccessDescriptionFallback')}` }
      )
      onDone?.()
    } catch (e) {
      console.error(e)
      setFormError(getApiErrorMessage(e, t('dryRunDialog.errorFallback')))
    } finally {
      setSubmitting(false)
    }
  }

  const canSubmit = !!target && /\S+@\S+\.\S+/.test(email.trim()) && isParamsValid(target.parameters, paramValues)

  return (
    <Dialog open={!!target} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t('dryRunDialog.titleTemplate', { name: target?.displayName ?? '' })}</DialogTitle>
          <DialogDescription>
            {t('dryRunDialog.description')}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <div className="space-y-1.5">
            <Label>{t('dryRunDialog.emailLabel')}</Label>
            <Input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder={t('dryRunDialog.emailPlaceholder')}
            />
          </div>
          {target && (
            <ParamFields
              parameters={target.parameters}
              values={paramValues}
              onChange={(name, value) => setParamValues(p => ({ ...p, [name]: value }))}
            />
          )}
          {formError && (
            <p className="text-destructive bg-destructive/10 rounded-md px-3 py-2 text-sm">{formError}</p>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => handleOpenChange(false)} disabled={submitting}>
            {tc('cancel')}
          </Button>
          <Button onClick={() => void submit()} disabled={!canSubmit || submitting}>
            {submitting ? <Loader2 className="mr-1.5 size-4 animate-spin" /> : <FlaskConical className="mr-1.5 size-4" />}
            {t('dryRunDialog.submit')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
