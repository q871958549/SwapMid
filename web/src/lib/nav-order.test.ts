import { describe, expect, it } from 'vitest'
import { navGroups } from '@/components/layout/nav-config'
import {
  applyNavOrder, applyOrder, compactNavOrder, isNavOrderCustomizedIn, mergeScopedOrder, NAV_ORDER_MAX_LENGTH, resetNavOrderIn,
  serializeCompactNavOrder, serializeNavOrder,
} from './nav-order'

describe('mergeScopedOrder 域内组顺序并回全局', () => {
  it('在 B 域排序后，A 域此前的自定义顺序不丢', () => {
    const afterA = mergeScopedOrder(null, ['a2', 'a1'])
    const afterB = mergeScopedOrder(afterA, ['b2', 'b1'])

    expect(applyOrder(['a1', 'a2'], afterB, (key) => key)).toEqual(['a2', 'a1'])
    expect(applyOrder(['b1', 'b2'], afterB, (key) => key)).toEqual(['b2', 'b1'])
  })

  it('同一域重复排序不产生重复 key', () => {
    const once = mergeScopedOrder(['x', 'a1', 'a2'], ['a2', 'a1'])
    expect(mergeScopedOrder(once, ['a1', 'a2'])).toEqual(['x', 'a1', 'a2'])
  })
})

describe('compactNavOrder 保存前瘦身', () => {
  const defaults = { groupKeys: ['g1', 'g2', 'g3'], itemHrefsByGroup: { g1: ['/a', '/b', '/c'], g2: ['/d', '/e'] } }

  it('与默认顺序一致的条目不存', () => {
    expect(compactNavOrder({ groupOrder: ['g1', 'g3'], itemOrders: { g1: ['/a', '/c'], g2: ['/d', '/e'] } }, defaults))
      .toEqual({ groupOrder: null, itemOrders: {} })
  })

  it('真正调过的顺序保留，已下线的 key 与组被剔除', () => {
    expect(compactNavOrder({ groupOrder: ['g3', 'gone', 'g1'], itemOrders: { g1: ['/c', '/old', '/a'], gone: ['/x'] } }, defaults))
      .toEqual({ groupOrder: ['g3', 'g1'], itemOrders: { g1: ['/c', '/a'] } })
  })

  it('与默认完全一致时序列化为空串（发布全员默认时据此撤销，而不是存空顺序）', () => {
    expect(serializeCompactNavOrder({ groupOrder: ['g1', 'g2'], itemOrders: { g2: ['/d', '/e'] } }, defaults)).toBe('')
    expect(serializeCompactNavOrder({ groupOrder: ['g2', 'g1'], itemOrders: {} }, defaults))
      .toBe('{"groupOrder":["g2","g1"],"itemOrders":{}}')
  })

  it('当前真实导航与大规模扩展导航在只调少数组时均远低于后端上限', () => {
    const real = {
      groupKeys: navGroups.map((group) => group.key),
      itemHrefsByGroup: Object.fromEntries(navGroups.map((group) => [group.key, group.items.map((item) => item.href)])),
    }
    const realOrders = Object.fromEntries(Object.entries(real.itemHrefsByGroup).map(([key, hrefs]) => [key, [...hrefs]]))
    realOrders.data = [...realOrders.data].reverse()
    expect(serializeNavOrder(compactNavOrder({ groupOrder: [...real.groupKeys], itemOrders: realOrders }, real)).length)
      .toBeLessThan(NAV_ORDER_MAX_LENGTH)

    const scaleGroups = Array.from({ length: 20 }, (_, gi) => `domain-group-${gi}`)
    const scaleDefaults = {
      groupKeys: scaleGroups,
      itemHrefsByGroup: Object.fromEntries(
        scaleGroups.map((gk) => [
          gk,
          Array.from({ length: 10 }, (_, ii) => `/${gk}/module-feature-entry-${ii}`),
        ]),
      ),
    }
    const bloatedOrders = Object.fromEntries(
      Object.entries(scaleDefaults.itemHrefsByGroup).map(([key, hrefs]) => [key, [...hrefs]]),
    )
    for (const key of scaleGroups.slice(0, 2)) bloatedOrders[key].reverse()

    const before = serializeNavOrder({ groupOrder: [...scaleDefaults.groupKeys], itemOrders: bloatedOrders })
    const after = serializeNavOrder(compactNavOrder({ groupOrder: [...scaleDefaults.groupKeys], itemOrders: bloatedOrders }, scaleDefaults))
    expect(before.length).toBeGreaterThan(NAV_ORDER_MAX_LENGTH)
    expect(after.length).toBeLessThan(NAV_ORDER_MAX_LENGTH)
  })
})

describe('resetNavOrderIn 只恢复一个域', () => {
  const defaults = { itemHrefsByGroup: { a1: ['/a1/x', '/a1/y'], a2: ['/a2/x'], b1: ['/b1/x', '/b1/y'], b2: ['/b2/x'] } }
  const customized = {
    groupOrder: ['a2', 'a1', 'b2', 'b1'],
    itemOrders: { a1: ['/a1/y', '/a1/x'], b1: ['/b1/y', '/b1/x'] },
  }

  it('A 域恢复内置顺序，B 域的组顺序与项顺序原样保留', () => {
    const next = resetNavOrderIn(customized, ['a1', 'a2'])

    expect(isNavOrderCustomizedIn(next, ['a1', 'a2'], defaults)).toBe(false)
    expect(applyOrder(['b1', 'b2'], next.groupOrder, (key) => key)).toEqual(['b2', 'b1'])
    expect(next.itemOrders).toEqual({ b1: ['/b1/y', '/b1/x'] })
  })

  it('全局顺序里只剩被恢复的域时归空', () => {
    expect(resetNavOrderIn({ groupOrder: ['a2', 'a1'], itemOrders: {} }, ['a1', 'a2']).groupOrder).toBeNull()
  })

  it('isNavOrderCustomizedIn 只看给定的组：组顺序或项顺序任一不同即算自定义', () => {
    expect(isNavOrderCustomizedIn({ groupOrder: null, itemOrders: {} }, ['a1', 'a2'], defaults)).toBe(false)
    expect(isNavOrderCustomizedIn({ groupOrder: ['a2', 'a1'], itemOrders: {} }, ['a1', 'a2'], defaults)).toBe(true)
    expect(isNavOrderCustomizedIn({ groupOrder: null, itemOrders: { a1: ['/a1/y'] } }, ['a1', 'a2'], defaults)).toBe(true)
    expect(isNavOrderCustomizedIn(customized, ['a1', 'a2'], defaults)).toBe(true)
    expect(isNavOrderCustomizedIn({ groupOrder: ['b2', 'b1'], itemOrders: {} }, ['a1', 'a2'], defaults)).toBe(false)
  })
})

describe('applyNavOrder 组与组内项一起排', () => {
  it('仪表盘、工作台全景用的就是侧栏里拖出来的顺序', () => {
    const groups = [
      { key: 'g1', items: [{ href: '/1' }, { href: '/2' }] },
      { key: 'g2', items: [{ href: '/3' }] },
    ]
    const ordered = applyNavOrder(groups, { groupOrder: ['g2', 'g1'], itemOrders: { g1: ['/2', '/1'] } })

    expect(ordered.map((g) => g.key)).toEqual(['g2', 'g1'])
    expect(ordered[1]!.items.map((i) => i.href)).toEqual(['/2', '/1'])
  })
})
