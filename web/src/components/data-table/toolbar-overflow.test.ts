import { Fragment, createElement } from 'react'
import { describe, expect, it } from 'vitest'
import { countToolbarExtraSlots, partitionToolbarSlots, TOOLBAR_VISIBLE_LIMIT } from './toolbar-overflow'

const slots = [
  { id: 'import', priority: 10 },
  { id: 'export', priority: 70 },
  { id: 'reset', priority: 30 },
  { id: 'refresh', priority: 100 },
  { id: 'filter', priority: 80 },
  { id: 'columns', priority: 20 },
]

describe('partitionToolbarSlots', () => {
  it('不超过上限时全部留在外面', () => {
    const few = slots.filter((slot) => ['export', 'filter', 'columns'].includes(slot.id))
    const result = partitionToolbarSlots(few, 0)
    expect(result.overflow).toEqual([])
    expect(result.visible.map((slot) => slot.id)).toEqual(['export', 'filter', 'columns'])
  })

  it('六个内置动作留刷新、筛选、导出，其余进更多', () => {
    const result = partitionToolbarSlots(slots, 0)
    expect(result.visible.map((slot) => slot.id)).toEqual(['export', 'refresh', 'filter'])
    expect(result.overflow.map((slot) => slot.id)).toEqual(['import', 'reset', 'columns'])
    expect(result.visible.length + 1).toBeLessThanOrEqual(TOOLBAR_VISIBLE_LIMIT)
  })

  it('页面额外按钮占名额，内置动作继续往里收', () => {
    const result = partitionToolbarSlots(slots, 2)
    expect(result.visible.map((slot) => slot.id)).toEqual(['refresh'])
    expect(result.overflow).toHaveLength(5)
  })

  it('页面额外按钮已经占满时，内置动作全部收进更多', () => {
    const result = partitionToolbarSlots(slots, 4)
    expect(result.visible).toEqual([])
    expect(result.overflow).toHaveLength(6)
  })
})

describe('countToolbarExtraSlots', () => {
  it('Fragment 展开后按顶层节点计数', () => {
    const node = createElement(Fragment, null, createElement('button'), createElement('button'))
    expect(countToolbarExtraSlots(node)).toBe(2)
  })

  it('空节点不计', () => {
    expect(countToolbarExtraSlots(null)).toBe(0)
    expect(countToolbarExtraSlots(false)).toBe(0)
  })
})
