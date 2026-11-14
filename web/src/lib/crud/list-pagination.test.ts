import { describe, expect, it } from 'vitest'
import {
  INTERACTIVE_LIST_MAX_PAGE_SIZE,
  parseServerListPagination,
  SERVER_MAX_PAGE_SIZE,
} from './list-pagination'

describe('parseServerListPagination', () => {
  it('把合法的 1-based URL 页码转换为后端参数', () => {
    expect(parseServerListPagination('3', '20')).toEqual({
      pageIndex: 2,
      pageSize: 20,
      skipCount: 40,
    })
  })

  it.each([
    [undefined, undefined],
    ['0', '-1'],
    ['1.5', '2.5'],
    ['Infinity', 'Infinity'],
    ['not-a-number', 'not-a-number'],
  ])('非法页码或每页条数回退安全默认值', (page, size) => {
    expect(parseServerListPagination(page, size)).toEqual({
      pageIndex: 0,
      pageSize: 15,
      skipCount: 0,
    })
  })

  it('默认钳到交互列表上限，而不是服务端硬顶', () => {
    const parsed = parseServerListPagination(
      String(Number.MAX_SAFE_INTEGER),
      String(Number.MAX_SAFE_INTEGER),
    )

    expect(parsed.pageSize).toBe(INTERACTIVE_LIST_MAX_PAGE_SIZE)
    expect(parsed.skipCount).toBeLessThanOrEqual(2_147_483_647)
    expect(Number.isSafeInteger(parsed.skipCount)).toBe(true)
  })

  it('size=1000 在默认上限下钳到 200', () => {
    expect(parseServerListPagination('1', '1000').pageSize).toBe(INTERACTIVE_LIST_MAX_PAGE_SIZE)
  })

  it('调用方提供的默认每页条数也不能越过交互上限', () => {
    expect(parseServerListPagination(undefined, undefined, 50_000)).toEqual({
      pageIndex: 0,
      pageSize: INTERACTIVE_LIST_MAX_PAGE_SIZE,
      skipCount: 0,
    })
  })

  it('显式传入硬顶时仍允许 5000', () => {
    expect(parseServerListPagination('1', '5000', undefined, SERVER_MAX_PAGE_SIZE)).toEqual({
      pageIndex: 0,
      pageSize: SERVER_MAX_PAGE_SIZE,
      skipCount: 0,
    })
  })

  it('显式 max=500 时 1000 钳到 500', () => {
    expect(parseServerListPagination('1', '1000', undefined, 500).pageSize).toBe(500)
  })
})
