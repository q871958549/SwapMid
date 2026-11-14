'use client'

/**
 * 单删确认零件——把每个 CRUD 页逐字重复的「单删 ConfirmDialog」那 8 行接线收成一行。
 *
 * 背景:实证 23 个有单删的页,这段几乎逐字相同——只有标题/描述在变,其余
 * `open=!!crud.deleteTarget`、`onOpenChange=closeDelete`、`busy=crud.deleting`、
 * `onConfirm=crud.confirmDelete`、`confirmText="删除"`、`destructive` 全是固定接线,
 * 且这堆 crud 接线最易接错(漏一个对话框就不弹/不关/不转圈)。收成零件后页面只声明
 * 「删什么(entityName)」和「删除提示文案(description)」。
 *
 * 设计同 column-factories:**造返回 ReactNode 的零件,不接管页面 return 拓扑**——
 * 页面照常线性写 `<DeleteConfirm crud={crud} .../>`,看得见全貌。批量删/危险操作确认
 * 仍直接用底层 `<ConfirmDialog>`(payload/文案各异,不强收)。
 *
 * @example
 * // 简单页:只给实体名,用默认文案
 * <DeleteConfirm crud={crud} entityName="房间" />
 * // 带实体名 + 副作用提示
 * <DeleteConfirm crud={crud} entityName="分组"
 *   description={(g) => <>删除分组「{g.name}」后无法恢复。该分组下的路由将失去分组关联。</>} />
 */

import { type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { type UseCrudFormReturn } from '@/lib/crud/use-crud-form'

/** useCrudForm 返回值里「删除确认」所需的最小切面;页面传完整 crud 即可(结构兼容)。 */
type CrudDeleteSlice<TRow> = Pick<
  UseCrudFormReturn<unknown, TRow>,
  'deleteTarget' | 'closeDelete' | 'confirmDelete' | 'deleting'
>

interface DeleteConfirmProps<TRow> {
  /** useCrudForm 的返回值(只用到 deleteTarget/closeDelete/confirmDelete/deleting)。 */
  crud: CrudDeleteSlice<TRow>
  /** 实体中文名,用于默认标题「确认删除X」与默认描述。 */
  entityName?: string
  /** 覆盖默认标题(默认「确认删除{entityName}」)。 */
  title?: string
  /**
   * 删除提示文案。函数拿到当前 deleteTarget 行,返回描述(常带实体名 + 副作用提示)。
   * 不传则用默认「删除该{entityName}后无法恢复,请确认。」。
   */
  description?: (target: TRow) => ReactNode
  /** 确认按钮文案,默认「删除」。 */
  confirmText?: string
}

export function DeleteConfirm<TRow>({
  crud,
  entityName,
  title,
  description,
  confirmText,
}: DeleteConfirmProps<TRow>) {
  const t = useTranslations('Common')
  const target = crud.deleteTarget
  return (
    <ConfirmDialog
      open={!!target}
      onOpenChange={(o) => { if (!o) crud.closeDelete() }}
      title={title ?? t('confirmDeleteTitle', { entityName: entityName ?? '' })}
      description={
        target
          ? (description?.(target) ?? t('deleteDescriptionDefault', { entityName: entityName ?? t('recordFallback') }))
          : null
      }
      confirmText={confirmText ?? t('delete')}
      busyText={t('deleting')}
      destructive
      busy={crud.deleting}
      onConfirm={crud.confirmDelete}
    />
  )
}
