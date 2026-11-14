import { describe, it, expect } from 'vitest'
import { parseSortParam, serializeSortParam, toAbpSorting } from './list-sort'

describe('list-sort（排序 URL 编解码）', () => {
  it('单列编解码往返一致', () => {
    expect(serializeSortParam([{ id: 'name', desc: true }])).toBe('name.desc')
    expect(parseSortParam('name.desc')).toEqual([{ id: 'name', desc: true }])
    expect(parseSortParam('name.asc')).toEqual([{ id: 'name', desc: false }])
  })

  it('多列以逗号分隔，顺序保持', () => {
    const sorting = [{ id: 'code', desc: false }, { id: 'creationTime', desc: true }]
    const s = serializeSortParam(sorting)
    expect(s).toBe('code.asc,creationTime.desc')
    expect(parseSortParam(s)).toEqual(sorting)
  })

  it('空/缺省返回空数组与空串', () => {
    expect(parseSortParam(undefined)).toEqual([])
    expect(parseSortParam('')).toEqual([])
    expect(serializeSortParam([])).toBe('')
  })

  it('非法段忽略：未知方向/空字段名', () => {
    expect(parseSortParam('name.up')).toEqual([])
    expect(parseSortParam('.desc')).toEqual([])
    expect(parseSortParam('name.desc,bad.up,code.asc')).toEqual([
      { id: 'name', desc: true },
      { id: 'code', desc: false },
    ])
  })

  it('无方向段默认 asc（容忍手输 ?sort=name）', () => {
    expect(parseSortParam('name')).toEqual([{ id: 'name', desc: false }])
  })

  it('toAbpSorting 产出 ABP Sorting 串', () => {
    expect(toAbpSorting([{ id: 'name', desc: true }, { id: 'code', desc: false }])).toBe('name desc,code asc')
    expect(toAbpSorting([])).toBe('')
  })
})
