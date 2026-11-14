import { describe, expect, it } from 'vitest'
import type { ColumnDef } from '@tanstack/react-table'
import { leftStickyLayout, reconcileColumnOrder } from './use-table-persistence'

describe('reconcileColumnOrder · 左钉列', () => {
  it('钉在选择列之后，不跟已保存的拖拽顺序走', () => {
    const columns: ColumnDef<{ id: string }, unknown>[] = [
      { id: 'select' },
      { accessorKey: 'enabled', meta: { pin: 'left' } },
      { id: 'job', meta: { pin: 'left' } },
      { accessorKey: 'cron' },
      { id: 'actions' },
    ]
    expect(reconcileColumnOrder(['cron', 'job', 'enabled'], columns)).toEqual([
      'select', 'enabled', 'job', 'cron', 'actions',
    ])
  })
})

describe('leftStickyLayout', () => {
  it('连续钉住列从左累加偏移，阴影只画在最后一列', () => {
    const layout = leftStickyLayout([
      { id: 'select', sticky: true, width: 44 },
      { id: 'name', sticky: true, width: 130 },
      { id: 'dept', sticky: false, width: 120 },
    ])
    expect(layout.get('select')).toEqual({ offset: 0, edge: false })
    expect(layout.get('name')).toEqual({ offset: 44, edge: true })
    expect(layout.has('dept')).toBe(false)
  })

  it('普通列打断后，后面的钉住列不再留下', () => {
    const layout = leftStickyLayout([
      { id: 'name', sticky: true, width: 100 },
      { id: 'dept', sticky: false, width: 80 },
      { id: 'code', sticky: true, width: 90 },
    ])
    expect([...layout.keys()]).toEqual(['name'])
  })
})
