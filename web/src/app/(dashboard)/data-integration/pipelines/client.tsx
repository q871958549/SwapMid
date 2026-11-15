'use client'

import { useRef, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { AlertTriangle, Loader2, Play, RotateCcw } from 'lucide-react'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { PageHeader } from '@/components/data-table/page-header'
import { CreateButton } from '@/components/data-table/create-button'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { DeleteConfirm } from '@/components/data-table/delete-confirm'
import { actionsColumn, primaryColumn } from '@/components/data-table/column-factories'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Combobox } from '@/components/ui/combobox'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { toast } from '@/components/ui/use-toast'
import { type PagedResult } from '@/lib/api-client'
import { getApiErrorMessage } from '@/lib/api-error'
import { useCrudForm } from '@/lib/crud/use-crud-form'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import {
  classifyExecutionHttpFailure,
  classifyExecutionResult,
  OUTCOME_UNKNOWN_STATUS,
  readExecutionApiError,
} from './execution-safety'
import { isEtlExecutionRunPayload } from './execution-recovery'
import { useEtlExecutionRecovery } from './use-etl-execution-recovery'
import { PipelineEtlConfigSection } from './etl-config-section'
import {
  isReceiptMode,
  isUpsertConfigValid,
  parseEtlTransformConfig,
  serializeEtlTransformConfig,
  supportsTargetReceipt,
  type EtlTransformFormValue,
} from './transform-config'

const notify = {
  success: (title: string, description?: string) => toast({ variant: 'success', title, description }),
  error: (title: string, description?: string) => toast({ variant: 'destructive', title, description }),
  warning: (title: string, description?: string) => toast({ variant: 'warning', title, description }),
  info: (title: string, description?: string) => toast({ title, description }),
}

interface ExecuteResult {
  status: number
  processedRows?: number | null
  errorMessage?: string | null
}

export interface DataPipeline {
  id: string; name: string; description: string | null
  sourceId: string; sourceName: string | null
  targetId: string | null; targetName: string | null
  transformConfig: string | null
  scheduleCron: string | null; scheduleEnabled: boolean
  isEnabled: boolean; lastRunAt: string | null; lastRunStatus?: number | null; creationTime: string
}

export interface DataSourceOption {
  id: string
  name: string
  type: number
  config?: string | null
}

interface FormState extends EtlTransformFormValue {
  name: string; description: string; sourceId: string; targetId: string
  scheduleCron: string; scheduleEnabled: boolean
}
const defaultForm: FormState = {
  name: '', description: '', sourceId: '', targetId: '',
  sourceQuery: '', targetTable: '', mode: 'append',
  keyColumnsText: '', watermarkColumn: '', columnMappings: [], transformConfigValid: true,
  scheduleCron: '', scheduleEnabled: false,
}

export function DataPipelinesClient({
  data, sources,
  initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
  canExecute = false,
}: {
  data: PagedResult<DataPipeline>; sources: DataSourceOption[]
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
  canExecute?: boolean
}) {
  const t = useTranslations('DataIntegration.pipeline')
  const rc = useTranslations('DataIntegration.common')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const {
    status: recoveryStatus,
    ready: recoveryReady,
    pendingRunIds,
    blockedPipelines,
    isBlocked,
    claimRunId,
    retainRunId,
    clearRunId,
    blockPipeline,
    verifyRun,
  } = useEtlExecutionRecovery()
  const [executingPipelines, setExecutingPipelines] = useState<Record<string, true>>({})
  // ref 是同步裁决源，堵住双击发生在 React state 提交前时发起第二次请求；state 只负责重绘。
  const executingPipelinesRef = useRef<Record<string, true>>({})
  // 服务端分页：翻页/搜索下推后端。数据源参照保持全量供下拉。
  // page/pageSize/search 经 urlSync 镜像到 URL，切走再切回/刷新可恢复（初值来自 page.tsx 的 searchParams）。
  const list = useCrudResource<DataPipeline>({
    endpoint: 'data-pipelines',
    initial: data,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
  })
  const crud = useCrudForm<FormState, DataPipeline>({
    entityName: t('entityName'),
    endpoint: 'data-pipelines',
    defaultForm,
    toForm: (p) => {
      const transform = parseEtlTransformConfig(p.transformConfig)
      return {
        name: p.name, description: p.description ?? '', sourceId: p.sourceId,
        targetId: p.targetId ?? '', ...transform,
        scheduleCron: p.scheduleCron ?? '', scheduleEnabled: p.scheduleEnabled,
      }
    },
    toBody: (form) => ({
      name: form.name, description: form.description || null,
      sourceId: form.sourceId, targetId: form.targetId || null,
      transformConfig: serializeEtlTransformConfig(form),
      scheduleCron: form.scheduleCron || null, scheduleEnabled: form.scheduleEnabled,
    }),
    onSuccess: list.reload,
  })
  const selectedTarget = sources.find(source => source.id === crud.form.targetId)
  const receiptTargetSupported = supportsTargetReceipt(selectedTarget)
  const receiptModeSelected = isReceiptMode(crud.form.mode)
  const transformCanSubmit = crud.form.transformConfigValid &&
    crud.form.mode !== 'unsupported' &&
    isUpsertConfigValid(crud.form) &&
    (!receiptModeSelected || receiptTargetSupported)

  function showAmbiguousResponse(runId: string) {
    notify.error(
      t('executeResponseUnknownTitle'),
      t('executeResponseUnknownDesc', { runId }),
    )
  }

  async function clearRunIdOrReportUnavailable(
    pipelineId: string,
    runId: string,
    outcomeSummary: string,
  ) {
    const result = await clearRunId(pipelineId, runId)
    if (result !== 'unavailable') return true

    // 服务端已有确定结论，但本机恢复项未能安全清除。保留恢复项并只读核验一次，
    // 禁止用普通业务结果掩盖本机仍处于 fail-closed 保护状态。
    notify.warning(
      rc('recoveryCleanupUnavailableTitle'),
      `${outcomeSummary} ${rc('recoveryCleanupUnavailableDesc')}`,
    )
    try {
      void verifyRun(pipelineId, runId).catch(() => undefined)
    } catch {
      // 只读补验异常不能改写已明确的服务端结果；恢复项仍保留并保持 fail closed。
    }
    return false
  }

  // 手动执行前先固定 RunId；只有拿到明确终态才清除。网关超时、断网或响应解析失败时
  // 保留该 ID，下一次操作走幂等重放，避免“其实已提交却又生成新任务”的双写。
  async function handleExecute(p: DataPipeline) {
    if (!recoveryReady) {
      notify.error(
        recoveryStatus === 'unavailable'
          ? rc('recoveryStorageUnavailableTitle')
          : t('executionRecoveryLoadingTitle'),
        recoveryStatus === 'unavailable'
          ? rc('recoveryStorageUnavailableDesc')
          : t('executionRecoveryLoadingDesc'),
      )
      return
    }
    if (executingPipelinesRef.current[p.id]) return
    if (isBlocked(p.id) || Number(p.lastRunStatus) === OUTCOME_UNKNOWN_STATUS) {
      notify.error(t('executeOutcomeUnknownTitle'), t('executeOutcomeUnknownBlockedDesc'))
      return
    }

    executingPipelinesRef.current[p.id] = true
    setExecutingPipelines(current => ({ ...current, [p.id]: true }))
    let runId: string | null = null

    try {
      // principal/localStorage 必须在跨标签 Web Lock 内重读并原子 claim；
      // 不能根据本组件的旧 ref 自行生成 RunId。
      const claim = await claimRunId(p.id)
      if (!claim) {
        notify.error(t('executionRecoveryUnavailableTitle'), t('executionRecoveryUnavailableDesc'))
        return
      }
      runId = claim.runId
      if (claim.disposition === 'outcome-unknown') {
        notify.error(t('executeOutcomeUnknownTitle'), t('executeOutcomeUnknownBlockedDesc'))
        return
      }

      const query = new URLSearchParams({ runId })
      const res = await fetch(`/api/proxy/data-pipelines/${p.id}/execute?${query.toString()}`, {
        method: 'POST',
      })
      const text = await res.text()

      if (!res.ok) {
        const error = readExecutionApiError(text)
        const disposition = classifyExecutionHttpFailure(
          res.status,
          error.code,
          claim.isReplay,
        )
        if (disposition === 'outcome-unknown') {
          await blockPipeline(p.id, runId)
          notify.error(t('executeOutcomeUnknownTitle'), t('executeOutcomeUnknownBlockedDesc'))
          return
        }

        // 016/019 指向服务器已有的其他未终结执行，当前 RunId 尚未创建。不得把它伪装成
        // 当前运行的 OutcomeUnknown，否则随后 GET 只会 404 并造成永久自锁。
        if (disposition === 'target-blocked') {
          if (!await clearRunIdOrReportUnavailable(
            p.id,
            runId,
            t('executeTargetBlockedDesc'),
          )) return
          notify.error(t('executeTargetBlockedTitle'), t('executeTargetBlockedDesc'))
          return
        }

        // 这些状态可能发生在后端已写目标库、但代理未拿到最终响应之后；若是对同一
        // RunId 的重放遇到 Busy，同样不能据此生成新 ID。
        if (disposition === 'retain-run-id') {
          await retainRunId(p.id, runId)
          void verifyRun(p.id, runId)
          showAmbiguousResponse(runId)
          return
        }

        const failureMessage = error.message
          ?? getApiErrorMessage(new Error(text), t('executeFailedFallback'))
        if (!await clearRunIdOrReportUnavailable(p.id, runId, failureMessage)) return
        notify.error(
          t('executeFailedFallback'),
          failureMessage,
        )
        return
      }

      const parsedExecution: unknown = JSON.parse(text)
      if (!isEtlExecutionRunPayload(p.id, runId, parsedExecution)) {
        await retainRunId(p.id, runId)
        void verifyRun(p.id, runId)
        showAmbiguousResponse(runId)
        return
      }
      const exec = parsedExecution as ExecuteResult
      const status = Number(exec.status)
      const disposition = classifyExecutionResult(status)
      if (disposition === 'outcome-unknown') {
        await blockPipeline(p.id, runId)
        notify.error(t('executeOutcomeUnknownTitle'), t('executeOutcomeUnknownDesc', { runId }))
      } else if (disposition === 'succeeded') {
        const successMessage = t('executeSuccessDesc', { count: exec.processedRows ?? 0 })
        if (!await clearRunIdOrReportUnavailable(p.id, runId, successMessage)) return
        notify.success(successMessage)
      } else if (disposition === 'failed') {
        const failureMessage = t('executeFailedDesc', { message: exec.errorMessage ?? t('unknownError') })
        if (!await clearRunIdOrReportUnavailable(p.id, runId, failureMessage)) return
        notify.error(failureMessage)
      } else if (disposition === 'in-progress') {
        await retainRunId(p.id, runId)
        void verifyRun(p.id, runId)
        notify.info(t('executeStillRunningTitle'), t('executeStillRunningDesc', { runId }))
      } else {
        await retainRunId(p.id, runId)
        void verifyRun(p.id, runId)
        showAmbiguousResponse(runId)
      }
    } catch (e) {
      if (runId) {
        await retainRunId(p.id, runId)
        void verifyRun(p.id, runId)
      }
      console.error('[data-pipeline/execute] response unavailable', e)
      if (runId) {
        showAmbiguousResponse(runId)
      } else {
        notify.error(t('executionRecoveryUnavailableTitle'), t('executionRecoveryUnavailableDesc'))
      }
    } finally {
      delete executingPipelinesRef.current[p.id]
      setExecutingPipelines((current) => {
        const next = { ...current }
        delete next[p.id]
        return next
      })
      void list.reloadAndRefresh()
    }
  }

  const columns: ColumnDef<DataPipeline, unknown>[] = [
    primaryColumn<DataPipeline>('name', t('columnName'), { size: 180, onOpen: crud.openEdit }),
    {
      accessorKey: 'sourceName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('sourceName')} />,
      size: 160,
      // 关联名列需 join，不在后端排序白名单 → 隐藏排序 UI（见 EfCoreDataPipelineRepository.SortWhitelist）。
      enableSorting: false,
      cell: ({ row }) => <Badge variant="outline">{row.original.sourceName ?? '—'}</Badge>,
    },
    {
      accessorKey: 'targetName',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('targetName')} />,
      size: 160,
      enableSorting: false,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.targetName ?? '—'}</span>,
    },
    {
      accessorKey: 'scheduleCron',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('scheduleCron')} />,
      size: 140,
      cell: ({ row }) => (
        <span className="font-mono text-xs text-muted-foreground">{row.original.scheduleCron ?? t('manualSchedule')}</span>
      ),
    },
    {
      accessorKey: 'lastRunAt',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('lastRunAt')} />,
      size: 140,
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.lastRunAt ? new Date(row.original.lastRunAt).toLocaleString(locale) : '—'}
        </span>
      ),
    },
    actionsColumn<DataPipeline>({
      onEdit: crud.openEdit,
      onDelete: crud.askDelete,
      extraItems: (row) => {
        if (!canExecute) return null
        const blocked = blockedPipelines[row.id]
          || Number(row.lastRunStatus) === OUTCOME_UNKNOWN_STATUS
        const executing = executingPipelines[row.id]
        const retainedRunId = pendingRunIds[row.id]

        if (!recoveryReady) {
          return (
            <DropdownMenuItem disabled>
              {recoveryStatus === 'unavailable'
                ? <AlertTriangle className="mr-2 size-3.5 text-destructive" />
                : <Loader2 className="mr-2 size-3.5 animate-spin" />}
              {recoveryStatus === 'unavailable'
                ? rc('recoveryStorageUnavailableTitle')
                : t('executionRecoveryLoadingMenuItem')}
            </DropdownMenuItem>
          )
        }
        if (blocked) {
          return (
            <DropdownMenuItem disabled>
              <AlertTriangle className="mr-2 size-3.5 text-destructive" />
              {t('executeOutcomeUnknownMenuItem')}
            </DropdownMenuItem>
          )
        }
        return (
          <DropdownMenuItem disabled={executing} onClick={() => handleExecute(row)}>
            {executing
              ? <Loader2 className="mr-2 size-3.5 animate-spin" />
              : retainedRunId
                ? <RotateCcw className="mr-2 size-3.5" />
                : <Play className="mr-2 size-3.5" />}
            {executing
              ? t('executingMenuItem')
              : retainedRunId
                ? t('retrySameRunMenuItem')
                : t('executeMenuItem')}
          </DropdownMenuItem>
        )
      },
    }),
  ]

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('countDescription', { count: list.total })} action={<CreateButton label={t('createButtonLabel')} onClick={crud.openCreate} />} />
      <DataTable columns={columns} data={list.rows} {...list.tableProps} searchPlaceholder={t('searchPlaceholder')} tableKey="data-pipelines" />

      <CrudFormDialog
        crud={crud}
        title={{ create: t('createTitle'), edit: t('editTitle') }}
        contentClassName="sm:max-w-2xl"
        canSubmit={!!crud.form.name && !!crud.form.sourceId && transformCanSubmit}
      >
        <div className="space-y-1.5">
          <Label>{t('nameFieldLabel')}</Label>
          <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('namePlaceholder')} />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('sourceFieldLabel')}</Label>
            <Combobox
              options={sources.map(s => ({ value: s.id, label: s.name }))}
              value={crud.form.sourceId}
              onChange={v => crud.set({ sourceId: v })}
              placeholder={t('sourcePlaceholder')}
              searchPlaceholder={t('sourceSearchPlaceholder')}
              emptyText={t('sourceEmptyText')}
            />
          </div>
          <div className="space-y-1.5">
            <Label>{t('targetFieldLabel')}</Label>
            <Combobox
              options={sources.map(s => ({ value: s.id, label: s.name }))}
              value={crud.form.targetId}
              onChange={v => crud.set({ targetId: v })}
              placeholder={t('targetPlaceholder')}
              searchPlaceholder={t('targetSearchPlaceholder')}
              emptyText={t('sourceEmptyText')}
              clearable
            />
          </div>
        </div>
        <PipelineEtlConfigSection
          sourceId={crud.form.sourceId}
          targetId={crud.form.targetId}
          form={crud.form}
          receiptTargetSupported={receiptTargetSupported}
          onChange={crud.set}
        />
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1.5">
            <Label>{t('cronFieldLabel')}</Label>
            <Input value={crud.form.scheduleCron} onChange={e => crud.set({ scheduleCron: e.target.value })} placeholder="0 * * * * ?" className="font-mono" />
          </div>
          <div className="flex items-center gap-2 pt-6">
            <Switch id="scheduleEnabled" checked={crud.form.scheduleEnabled} onCheckedChange={v => crud.set({ scheduleEnabled: v })} />
            <Label htmlFor="scheduleEnabled">{t('scheduleEnabledSwitchLabel')}</Label>
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>{tc('description')}</Label>
          <Textarea value={crud.form.description} onChange={e => crud.set({ description: e.target.value })} placeholder={rc('descriptionPlaceholder')} className="min-h-[60px]" />
        </div>
      </CrudFormDialog>

      <DeleteConfirm
        crud={crud}
        entityName={t('entityName')}
        description={(target) => <>{t('deleteDesc', { name: target.name })}</>}
      />
    </>
  )
}
