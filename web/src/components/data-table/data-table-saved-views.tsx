'use client'

import { useState } from 'react'
import { BookmarkPlus, X } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'
import { MAX_SAVED_PRESET_NAME_LENGTH } from './use-table-persistence'
import type { TableSavedViewsController } from './use-table-saved-views'

/**
 * 嵌入列设置弹层顶部的「命名视图预设」面板：
 * 一键保存/切换/删除「列顺序+显隐+列宽+左固定+搜索+排序+筛选」，工具栏可见文字按钮增量 = 0。
 */
export function DataTableSavedViews({ controller }: { controller: TableSavedViewsController }) {
  const t = useTranslations('Common')
  const [adding, setAdding] = useState(false)
  const [draftName, setDraftName] = useState('')

  const atLimit = controller.presets.length >= controller.maxPresets

  function handleSave() {
    const trimmed = draftName.trim()
    if (!trimmed) return
    const saved = controller.saveCurrentAsPreset(trimmed)
    if (saved) {
      setDraftName('')
      setAdding(false)
    }
  }

  function handleCancel() {
    setDraftName('')
    setAdding(false)
  }

  return (
    <div className="flex flex-col gap-1.5 border-b px-1 pb-2">
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-medium text-muted-foreground">{t('savedViews')}</span>
        {!adding && (
          <Button
            variant="link"
            size="sm"
            className="h-auto px-0 text-xs"
            disabled={atLimit}
            title={atLimit ? t('savedViewsLimitHint', { max: controller.maxPresets }) : undefined}
            onClick={() => setAdding(true)}
          >
            <BookmarkPlus className="mr-1 size-3.5" />
            {t('saveCurrentView')}
          </Button>
        )}
      </div>

      {adding && (
        <div className="flex items-center gap-1">
          <Input
            value={draftName}
            maxLength={MAX_SAVED_PRESET_NAME_LENGTH}
            placeholder={t('savedViewNamePlaceholder')}
            aria-label={t('savedViewNamePlaceholder')}
            onChange={(e) => setDraftName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                handleSave()
              } else if (e.key === 'Escape') {
                e.preventDefault()
                handleCancel()
              }
            }}
            className="h-7 flex-1 text-xs"
          />
          <Button
            size="sm"
            variant="outline"
            className="h-7 px-2 text-xs"
            disabled={!draftName.trim()}
            onClick={handleSave}
          >
            {t('save')}
          </Button>
          <IconButton label={t('cancel')} className="size-7" onClick={handleCancel}>
            <X className="size-3.5" />
          </IconButton>
        </div>
      )}

      {controller.presets.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {controller.presets.map((preset) => {
            const active = preset.id === controller.activePresetId
            return (
              <div
                key={preset.id}
                className={cn(
                  'inline-flex max-w-full items-center rounded-md border bg-muted/40 text-xs transition-colors',
                  active && 'border-foreground/30 bg-accent font-medium text-foreground',
                )}
              >
                <button
                  type="button"
                  onClick={() => controller.applyPreset(preset.id)}
                  className="max-w-36 cursor-pointer truncate rounded-l-md px-2 py-1 text-left focus-visible:outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
                >
                  {preset.name}
                </button>
                <IconButton
                  label={t('deleteSavedView', { name: preset.name })}
                  className="size-6 rounded-l-none text-muted-foreground hover:text-destructive"
                  onClick={() => controller.deletePreset(preset.id)}
                >
                  <X className="size-3" />
                </IconButton>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
