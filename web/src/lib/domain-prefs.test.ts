import { describe, expect, it } from 'vitest'
import { navDomains, navGroups } from '@/components/layout/nav-config'
import {
  applyDomainOrder, deriveRecentDomainKeys, findResumeHref, parseDomainPrefs, serializeDomainPrefs, splitDomains,
} from './domain-prefs'

const keys = ['a', 'b', 'c', 'd']
const id = (key: string) => key

// 取某个域里第一个不要求权限的页面，或者干脆给全量权限：这里只关心归域，不关心权限
const allPolicies = new Proxy({}, { get: () => true }) as Record<string, boolean>
function firstHrefOf(domainKey: string): string {
  const domain = navDomains.find((d) => d.key === domainKey)!
  const group = navGroups.find((g) => domain.groupKeys.includes(g.key))!
  return group.items[0]!.href
}

describe('domain-prefs 序列化', () => {
  it('非法或旧版本的值一律当作没设置', () => {
    expect(parseDomainPrefs(null)).toEqual({ order: [], hidden: [] })
    expect(parseDomainPrefs('not json')).toEqual({ order: [], hidden: [] })
    expect(parseDomainPrefs('{"v":2,"order":["a"]}')).toEqual({ order: [], hidden: [] })
    expect(parseDomainPrefs('{"v":1,"order":["a",3],"hidden":"x"}')).toEqual({ order: ['a'], hidden: [] })
  })

  it('什么都没改时序列化为空串，后端据此删掉设置', () => {
    expect(serializeDomainPrefs({ order: ['a', 'b'], hidden: [] }, keys)).toBe('')
    expect(serializeDomainPrefs({ order: [], hidden: ['zzz'] }, keys)).toBe('')
  })

  it('排序存成完整顺序、去掉已下线的键；隐藏按内置顺序存', () => {
    const value = serializeDomainPrefs({ order: ['c', 'gone', 'a'], hidden: ['d', 'b'] }, keys)
    expect(JSON.parse(value)).toEqual({ v: 1, order: ['c', 'a', 'b', 'd'], hidden: ['b', 'd'] })
    expect(parseDomainPrefs(value)).toEqual({ order: ['c', 'a', 'b', 'd'], hidden: ['b', 'd'] })
  })

  it('真实业务域全排一遍也远在 2048 字符预算内', () => {
    const all = navDomains.map((d) => d.key)
    const value = serializeDomainPrefs({ order: [...all].reverse(), hidden: all.slice(1) }, all)
    expect(value.length).toBeLessThan(2048)
  })
})

describe('domain-prefs 排序与隐藏', () => {
  it('order 里的排前面，没列出的（如新上线的域）按原顺序追加末尾', () => {
    expect(applyDomainOrder(keys, ['c', 'a'], id)).toEqual(['c', 'a', 'b', 'd'])
    expect(applyDomainOrder(keys, [], id)).toBe(keys)
  })

  it('隐藏的域拆到「更多」段，常用段保持个人顺序', () => {
    expect(splitDomains(keys, { order: ['d', 'c'], hidden: ['c', 'a'] }, id)).toEqual({
      shown: ['d', 'b'],
      hidden: ['a', 'c'],
    })
  })
})

describe('最近业务域与切域恢复', () => {
  const [first, second, third] = navDomains.map((d) => d.key)
  const available = navDomains.map((d) => d.key)

  it('按访问时间倒序去重取前两个，排除当前域和无权限的域', () => {
    const hrefs = [firstHrefOf(first!), firstHrefOf(second!), firstHrefOf(second!), firstHrefOf(third!), '/dashboard']
    expect(deriveRecentDomainKeys(hrefs, available, '')).toEqual([first, second])
    expect(deriveRecentDomainKeys(hrefs, available, first!)).toEqual([second, third])
    expect(deriveRecentDomainKeys(hrefs, [third!], '')).toEqual([third])
  })

  it('切到某域时回到它最近访问的那一页；没有记录返回 null', () => {
    const target = firstHrefOf(second!)
    const hrefs = [firstHrefOf(first!), target]
    expect(findResumeHref(hrefs, second!, allPolicies)).toBe(target)
    expect(findResumeHref(hrefs, third!, allPolicies)).toBeNull()
  })

  it('权限已回收的页面不作为恢复目标', () => {
    const domain = navDomains.find((d) => navGroups.some((g) => d.groupKeys.includes(g.key) && g.items[0]?.permission))!
    const group = navGroups.find((g) => domain.groupKeys.includes(g.key) && g.items[0]?.permission)!
    expect(findResumeHref([group.items[0]!.href], domain.key, {})).toBeNull()
  })
})
