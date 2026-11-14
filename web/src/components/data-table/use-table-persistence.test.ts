import { beforeEach, describe, expect, it } from 'vitest'
import {
  addCustomPageSize,
  readCustomPageSizes,
  removeCustomPageSize,
  retainUsableCustomPageSizes,
  splitCustomPageSizes,
} from './use-table-persistence'

const GLOBAL_KEY = 'swapmid-table:page-size-options'
const SCOPED_KEY = 'swapmid-table:users:page-size-options'

beforeEach(() => {
  localStorage.clear()
})

describe('splitCustomPageSizes', () => {
  it('按本页上限拆成可用 / 不可用', () => {
    expect(splitCustomPageSizes([150, 500, 1000], 200)).toEqual({
      usable: [150],
      unavailable: [500, 1000],
    })
  })

  it('下拉只应消费 usable：500/1000 不进入 200 上限页选项', () => {
    const { usable } = splitCustomPageSizes([500, 1000], 200)
    expect(usable).toEqual([])
  })
})

describe('自定义每页条数写入只保留本页合法档位', () => {
  it('无本表键时回退全局旧键', () => {
    localStorage.setItem(GLOBAL_KEY, JSON.stringify([150, 500, 1000]))
    expect(readCustomPageSizes('users')).toEqual([150, 500, 1000])
  })

  it('添加时只把 ≤ 上限的档位写入本表键，不改全局旧键', () => {
    localStorage.setItem(GLOBAL_KEY, JSON.stringify([150, 500, 1000]))
    expect(addCustomPageSize(180, 'users', 200)).toEqual([150, 180])
    expect(JSON.parse(localStorage.getItem(SCOPED_KEY) ?? '[]')).toEqual([150, 180])
    expect(JSON.parse(localStorage.getItem(GLOBAL_KEY) ?? '[]')).toEqual([150, 500, 1000])
  })

  it('拒绝超过本页上限的添加，且不写本表键', () => {
    localStorage.setItem(GLOBAL_KEY, JSON.stringify([500]))
    expect(addCustomPageSize(500, 'users', 200)).toBeNull()
    expect(localStorage.getItem(SCOPED_KEY)).toBeNull()
    expect(JSON.parse(localStorage.getItem(GLOBAL_KEY) ?? '[]')).toEqual([500])
  })

  it('移除可用档位时本表键只留下限内项', () => {
    localStorage.setItem(GLOBAL_KEY, JSON.stringify([150, 500, 1000]))
    expect(removeCustomPageSize(150, 'users', 200)).toEqual([])
    expect(JSON.parse(localStorage.getItem(SCOPED_KEY) ?? '[]')).toEqual([])
    expect(JSON.parse(localStorage.getItem(GLOBAL_KEY) ?? '[]')).toEqual([150, 500, 1000])
  })

  it('从本表移除超限档位：写入限内项，全局旧键不动', () => {
    localStorage.setItem(GLOBAL_KEY, JSON.stringify([150, 500, 1000]))
    expect(retainUsableCustomPageSizes('users', 200)).toEqual([150])
    expect(JSON.parse(localStorage.getItem(SCOPED_KEY) ?? '[]')).toEqual([150])
    expect(JSON.parse(localStorage.getItem(GLOBAL_KEY) ?? '[]')).toEqual([150, 500, 1000])
  })
})
