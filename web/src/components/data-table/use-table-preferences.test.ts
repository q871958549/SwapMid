import { describe, it, expect, beforeEach } from 'vitest'
import { renderHook, act, waitFor } from '@testing-library/react'
import { type ColumnDef } from '@tanstack/react-table'
import { useTablePreferences } from './use-table-preferences'

interface Row {
  id: string
  name: string
}

// 两数据列 + 右固定结构列（actions）——reconcileColumnOrder 据此把 actions 钉在末尾。
const columns: ColumnDef<Row, unknown>[] = [
  { accessorKey: 'id' },
  { accessorKey: 'name' },
  { id: 'actions' },
]
const defaultColumnOrder = ['id', 'name', 'actions']
// 模拟审计列默认隐藏。
const defaultVisibility = { creator: false }

function renderPrefs(effectiveTableKey: string, tableColumns = columns) {
  return renderHook(() =>
    useTablePreferences<Row>({
      effectiveTableKey,
      defaultColumnOrder,
      defaultVisibility,
      pageSize: 15,
      tableColumns,
    }),
  )
}

beforeEach(() => {
  localStorage.clear()
})

describe('useTablePreferences · hydration', () => {
  it('无 localStorage 记录时，hydration 后落到默认顺序 / 显隐 / 每页', async () => {
    const { result } = renderPrefs('k1')
    await waitFor(() => expect(result.current.hydrated).toBe(true))
    expect(result.current.columnOrder).toEqual(['id', 'name', 'actions'])
    expect(result.current.columnVisibility).toEqual({ creator: false })
    expect(result.current.pagination).toEqual({ pageIndex: 0, pageSize: 15 })
  })

  it('从 localStorage 恢复列顺序 / 显隐 / 每页条数（顺序经 reconcile 保结构列钉尾）', async () => {
    localStorage.setItem('swapmid-table:k2:order', JSON.stringify(['name', 'id']))
    localStorage.setItem('swapmid-table:k2:visibility', JSON.stringify({ name: false }))
    localStorage.setItem('swapmid-table:k2:pageSize', JSON.stringify(50))

    const { result } = renderPrefs('k2')
    await waitFor(() => expect(result.current.hydrated).toBe(true))

    expect(result.current.columnOrder).toEqual(['name', 'id', 'actions'])
    expect(result.current.columnVisibility).toEqual({ creator: false, name: false })
    expect(result.current.pagination.pageSize).toBe(50)
  })

  it('effectiveTableKey 不变但 tableColumns 重建时，hydration 守卫早退、不把分页重置回第 1 页', async () => {
    const { result, rerender } = renderHook(
      ({ tc }: { tc: ColumnDef<Row, unknown>[] }) =>
        useTablePreferences<Row>({
          effectiveTableKey: 'k3',
          defaultColumnOrder,
          defaultVisibility,
          pageSize: 15,
          tableColumns: tc,
        }),
      { initialProps: { tc: columns } },
    )
    await waitFor(() => expect(result.current.hydrated).toBe(true))

    act(() => result.current.setPagination({ pageIndex: 3, pageSize: 15 }))
    expect(result.current.pagination.pageIndex).toBe(3)

    // 新数组引用（内容相同）令 hydration effect 依赖变化而重跑；hydratedKeyRef 命中 → 早退，
    // 不复位分页（对应「可编辑表格 cell 随外部 state 变 → tableColumns 频繁重建」的场景）。
    rerender({ tc: [...columns] })
    expect(result.current.pagination.pageIndex).toBe(3)
  })
})

describe('useTablePreferences · 后加的默认隐藏列', () => {
  it('存档没提过的默认隐藏列仍然隐藏，明确打开过的列保持打开', async () => {
    localStorage.setItem('swapmid-table:k-merge:visibility', JSON.stringify({ creator: true }))
    const { result } = renderHook(() =>
      useTablePreferences<Row>({
        effectiveTableKey: 'k-merge',
        defaultColumnOrder,
        defaultVisibility: { creator: true, agentVersion: false },
        pageSize: 15,
        tableColumns: columns,
      }),
    )
    await waitFor(() => expect(result.current.hydrated).toBe(true))
    expect(result.current.columnVisibility).toEqual({ creator: true, agentVersion: false })
  })
})

describe('useTablePreferences · 变更即存', () => {
  it('columnVisibility 变更写入 localStorage', async () => {
    const { result } = renderPrefs('k4')
    await waitFor(() => expect(result.current.hydrated).toBe(true))

    act(() => result.current.setColumnVisibility({ id: false }))

    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:k4:visibility')!)).toEqual({ id: false }),
    )
  })

  it('每页条数变更写入 localStorage', async () => {
    const { result } = renderPrefs('k5')
    await waitFor(() => expect(result.current.hydrated).toBe(true))

    act(() => result.current.setPagination({ pageIndex: 2, pageSize: 100 }))

    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:k5:pageSize')!)).toBe(100),
    )
  })
})
