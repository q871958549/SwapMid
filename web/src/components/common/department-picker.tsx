'use client'

// 部门下拉：默认树状（可展开/收起），可切换为名称或全路径。偏好存在 localStorage，全站共用。
// 树状模式不用 Combobox/cmdk：CommandItem 的 [&_svg]:pointer-events-none 会让展开箭头点不到，
// cmdk 过滤也会和折叠状态打架。展开箭头是独立 <button>，点它只折树、不选中。

import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useTranslations } from 'next-intl'
import { Check, ChevronRight, ChevronsUpDown, Search, X } from 'lucide-react'
import { Combobox } from '@/components/ui/combobox'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { cn } from '@/lib/utils'
import type { CompanyDepartmentDto } from '@/lib/api/types'
import {
  buildDepartmentTree,
  collapseAllExpandedIds,
  collectExpandableIds,
  defaultExpandedIds,
  filterTreeByQuery,
  flattenVisibleRows,
  type VisibleDeptRow,
} from './department-tree'

const STORAGE_KEY = 'swapmid-department-picker-display'
type DisplayMode = 'tree' | 'name' | 'path'

function readMode(): DisplayMode {
  if (typeof window === 'undefined') return 'tree'
  const stored = window.localStorage.getItem(STORAGE_KEY)
  return stored === 'name' || stored === 'path' ? stored : 'tree'
}

export function DepartmentPicker({
  value,
  onChange,
  placeholder,
  searchPlaceholder,
  emptyText,
  clearable = true,
  disabled,
  className,
}: {
  value?: string
  onChange?: (value: string, department?: CompanyDepartmentDto) => void
  placeholder?: string
  searchPlaceholder?: string
  emptyText?: string
  clearable?: boolean
  disabled?: boolean
  className?: string
}) {
  const t = useTranslations('Common')
  const [mode, setMode] = useState<DisplayMode>(readMode)
  const [departments, setDepartments] = useState<CompanyDepartmentDto[]>([])

  useEffect(() => {
    let cancelled = false
    fetch('/api/proxy/departments?maxResultCount=2000')
      .then(r => r.ok ? r.json() : Promise.reject(new Error(String(r.status))))
      .then((d: { items: CompanyDepartmentDto[] }) => {
        if (!cancelled) setDepartments(d.items ?? [])
      })
      .catch(e => { console.error('[department-picker] 部门列表加载失败', e) })
    return () => { cancelled = true }
  }, [])

  function changeMode(next: DisplayMode) {
    setMode(next)
    try { window.localStorage.setItem(STORAGE_KEY, next) } catch { /* ignore quota */ }
  }

  const enabled = useMemo(
    () => departments.filter(d => d.isEnabled !== false),
    [departments],
  )
  const tree = useMemo(() => buildDepartmentTree(enabled), [enabled])

  const flatOptions = useMemo(() => {
    const sorted = [...enabled].sort((a, b) => {
      const pathA = a.fullPath || a.name
      const pathB = b.fullPath || b.name
      const byPath = pathA.localeCompare(pathB, 'zh-CN')
      if (byPath !== 0) return byPath
      return (a.order ?? 0) - (b.order ?? 0)
    })
    return sorted.map(d => ({
      value: d.id,
      label: mode === 'path' ? (d.fullPath || d.name) : d.name,
      keywords: `${d.name} ${d.fullPath ?? ''} ${d.code ?? ''}`,
    }))
  }, [enabled, mode])

  const resolvedPlaceholder = placeholder ?? t('deptSelectPlaceholder')
  const resolvedSearch = searchPlaceholder ?? t('deptSearchPlaceholder')
  const resolvedEmpty = emptyText ?? t('deptEmptyText')
  const modes: { id: DisplayMode; label: string }[] = [
    { id: 'tree', label: t('deptDisplayTree') },
    { id: 'name', label: t('deptDisplayName') },
    { id: 'path', label: t('deptDisplayPath') },
  ]

  return (
    <div className={cn('space-y-1', className)}>
      {mode === 'tree' ? (
        <DepartmentTreeCombobox
          tree={tree}
          departments={enabled}
          value={value}
          onChange={onChange}
          placeholder={resolvedPlaceholder}
          searchPlaceholder={resolvedSearch}
          emptyText={resolvedEmpty}
          clearable={clearable}
          disabled={disabled}
          t={t}
        />
      ) : (
        <Combobox
          options={flatOptions}
          value={value}
          onChange={v => onChange?.(v, enabled.find(d => d.id === v))}
          placeholder={resolvedPlaceholder}
          searchPlaceholder={resolvedSearch}
          emptyText={resolvedEmpty}
          clearable={clearable}
          disabled={disabled}
        />
      )}
      <div className="flex flex-wrap items-center gap-1" role="group" aria-label={t('deptDisplayHint')}>
        {modes.map(item => (
          <button
            key={item.id}
            type="button"
            className={cn(
              'rounded px-1.5 py-0.5 text-xs select-none',
              mode === item.id
                ? 'bg-muted text-foreground'
                : 'text-muted-foreground hover:text-foreground',
            )}
            onClick={() => changeMode(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>
    </div>
  )
}

function DepartmentTreeCombobox({
  tree, departments, value, onChange, placeholder, searchPlaceholder, emptyText,
  clearable, disabled, t,
}: {
  tree: ReturnType<typeof buildDepartmentTree>
  departments: CompanyDepartmentDto[]
  value?: string
  onChange?: (value: string, department?: CompanyDepartmentDto) => void
  placeholder: string
  searchPlaceholder: string
  emptyText: string
  clearable: boolean
  disabled?: boolean
  t: ReturnType<typeof useTranslations>
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [expandedIds, setExpandedIds] = useState<Set<string>>(() => new Set())
  const [activeId, setActiveId] = useState<string | null>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const selected = departments.find(d => d.id === value)
  const showClear = clearable && !!selected && !disabled
  const searching = query.trim().length > 0
  const filtered = useMemo(() => filterTreeByQuery(tree, query), [tree, query])
  const effectiveExpanded = useMemo(() => {
    if (searching) return new Set(collectExpandableIds(filtered))
    return expandedIds
  }, [searching, filtered, expandedIds])
  const rows = useMemo(() => flattenVisibleRows(filtered, effectiveExpanded), [filtered, effectiveExpanded])

  const treeWasEmptyRef = useRef(tree.length === 0)

  function handleOpenChange(next: boolean) {
    setOpen(next)
    if (next) {
      setQuery('')
      setExpandedIds(defaultExpandedIds(tree, value))
      setActiveId(value ?? null)
    }
  }

  useEffect(() => {
    if (!open) {
      treeWasEmptyRef.current = tree.length === 0
      return
    }
    if (treeWasEmptyRef.current && tree.length > 0) {
      setExpandedIds(defaultExpandedIds(tree, value))
      setActiveId(value ?? null)
    }
    treeWasEmptyRef.current = tree.length === 0
  }, [open, tree, value])

  function selectDept(id: string) {
    onChange?.(id === value ? '' : id, departments.find(d => d.id === id))
    setOpen(false)
    triggerRef.current?.focus()
  }

  function handleClear() {
    onChange?.('')
    triggerRef.current?.focus()
  }

  function toggleExpanded(id: string) {
    setExpandedIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function moveActive(delta: number) {
    if (rows.length === 0) return
    const current = activeId ? rows.findIndex(r => r.node.id === activeId) : -1
    const nextIndex = current < 0
      ? (delta > 0 ? 0 : rows.length - 1)
      : Math.min(rows.length - 1, Math.max(0, current + delta))
    setActiveId(rows[nextIndex].node.id)
  }

  function handlePopoverKeyDown(e: KeyboardEvent) {
    if (e.nativeEvent.isComposing) return
    const row = rows.find(r => r.node.id === activeId) ?? rows[0]
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      moveActive(1)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      moveActive(-1)
    } else if (e.key === 'Home') {
      e.preventDefault()
      if (rows[0]) setActiveId(rows[0].node.id)
    } else if (e.key === 'End') {
      e.preventDefault()
      if (rows.length > 0) setActiveId(rows[rows.length - 1].node.id)
    } else if (e.key === 'Enter' && row) {
      e.preventDefault()
      selectDept(row.node.id)
    } else if (e.key === 'ArrowRight' && row?.expandable && !searching) {
      e.preventDefault()
      if (!row.expanded) toggleExpanded(row.node.id)
      else if (row.node.children[0]) setActiveId(row.node.children[0].id)
    } else if (e.key === 'ArrowLeft' && row && !searching) {
      e.preventDefault()
      if (row.expanded) toggleExpanded(row.node.id)
      else if (row.node.parentId) setActiveId(row.node.parentId)
    }
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <div className="relative">
        <PopoverTrigger asChild>
          <Button
            ref={triggerRef}
            variant="outline"
            role="combobox"
            aria-expanded={open}
            aria-haspopup="tree"
            disabled={disabled}
            className={cn(
              'h-9 w-full justify-between px-3 font-normal',
              !selected && 'text-muted-foreground',
              showClear && 'pr-12',
            )}
          >
            <span className="truncate">{selected?.name ?? placeholder}</span>
            <ChevronsUpDown className="ml-1 size-3.5 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        {showClear && (
          <button
            type="button"
            aria-label={t('clearSelection')}
            className="absolute right-7 top-1/2 -translate-y-1/2 rounded-sm p-1 text-muted-foreground outline-none transition-colors hover:text-foreground focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)"
            onClick={handleClear}
          >
            <X className="size-3.5" />
          </button>
        )}
      </div>
      <PopoverContent
        className="w-[var(--radix-popover-trigger-width)] p-0"
        align="start"
        onWheel={e => e.stopPropagation()}
        onKeyDown={handlePopoverKeyDown}
      >
        <div className="flex items-center border-b px-3">
          <Search className="mr-2 size-4 shrink-0 opacity-50" />
          <input
            value={query}
            onChange={e => {
              setQuery(e.target.value)
              setActiveId(null)
            }}
            placeholder={searchPlaceholder}
            className="flex h-9 w-full bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground"
          />
        </div>
        {!searching && collectExpandableIds(tree).length > 0 && (
          <div className="flex justify-end gap-2 border-b px-2 py-1">
            <button
              type="button"
              className="text-xs text-muted-foreground select-none hover:text-foreground"
              onClick={() => setExpandedIds(new Set(collectExpandableIds(tree)))}
            >
              {t('deptExpandAll')}
            </button>
            <button
              type="button"
              className="text-xs text-muted-foreground select-none hover:text-foreground"
              onClick={() => setExpandedIds(collapseAllExpandedIds(tree, value))}
            >
              {t('deptCollapseAll')}
            </button>
          </div>
        )}
        <div
          role="tree"
          aria-label={t('deptDisplayTree')}
          className="max-h-[min(20rem,50vh)] overflow-y-auto overflow-x-hidden p-1"
          onWheel={e => {
            const el = e.currentTarget
            if (el.scrollHeight > el.clientHeight) e.stopPropagation()
          }}
        >
          {rows.length === 0 && (
            <p className="py-6 text-center text-sm text-muted-foreground">{emptyText}</p>
          )}
          {rows.map(row => (
            <DepartmentTreeRow
              key={row.node.id}
              row={row}
              selected={row.node.id === value}
              active={row.node.id === (activeId ?? value)}
              searching={searching}
              expandLabel={t('deptExpandNode', { name: row.node.name })}
              collapseLabel={t('deptCollapseNode', { name: row.node.name })}
              onToggle={() => toggleExpanded(row.node.id)}
              onSelect={() => selectDept(row.node.id)}
            />
          ))}
        </div>
      </PopoverContent>
    </Popover>
  )
}

function DepartmentTreeRow({
  row, selected, active, searching, expandLabel, collapseLabel, onToggle, onSelect,
}: {
  row: VisibleDeptRow
  selected: boolean
  active: boolean
  searching: boolean
  expandLabel: string
  collapseLabel: string
  onToggle: () => void
  onSelect: () => void
}) {
  return (
    <div
      role="treeitem"
      aria-selected={selected}
      aria-expanded={row.expandable ? row.expanded : undefined}
      aria-level={row.depth + 1}
      ref={el => { if (el && active) el.scrollIntoView({ block: 'nearest' }) }}
      className={cn(
        'flex items-center rounded-sm select-none hover:bg-accent hover:text-accent-foreground',
        active && 'bg-accent text-accent-foreground',
      )}
      style={{ paddingLeft: 4 + row.depth * 16 }}
    >
      {row.expandable && !searching ? (
        <button
          type="button"
          tabIndex={-1}
          aria-label={row.expanded ? collapseLabel : expandLabel}
          className="flex size-6 shrink-0 items-center justify-center rounded-sm text-muted-foreground hover:text-foreground"
          onPointerDown={e => e.preventDefault()}
          onClick={e => { e.stopPropagation(); onToggle() }}
        >
          <ChevronRight
            className={cn(
              'size-3.5 transition-transform duration-150 ease-out motion-reduce:transition-none',
              row.expanded && 'rotate-90',
            )}
          />
        </button>
      ) : (
        <span className="size-6 shrink-0" aria-hidden />
      )}
      <button
        type="button"
        tabIndex={-1}
        className="flex min-w-0 flex-1 items-center gap-1.5 px-1 py-1.5 text-left text-sm"
        onClick={onSelect}
      >
        <span className="min-w-0 flex-1 truncate">{row.node.name}</span>
        <Check className={cn('size-4 shrink-0', selected ? 'opacity-100' : 'opacity-0')} />
      </button>
    </div>
  )
}
