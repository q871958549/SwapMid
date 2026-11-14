import { describe, expect, it } from 'vitest'
import zhMessages from '../../messages/zh-Hans.json'
import { navDomains, navGroups, WORKBENCH_DOMAIN } from '@/components/layout/nav-config'
import {
  canAccessPath,
  findNavDomainKeyByPath,
  findNavGroupByPath,
  findNavItemByPath,
  findSidebarHighlightItem,
  getPageTitle,
  isNavItemVisible,
  isPathInNavGroup,
  pathMatchesHref,
  resolveDomains,
  resolveTabTitle,
  translateNavDescription,
  translateNavTitle,
  uniqueGroupPathPrefix,
  type NavT,
} from './nav-utils'

function groupByKey(key: string) {
  const group = navGroups.find((entry) => entry.key === key)
  if (!group) throw new Error(`navGroups 缺少 ${key}`)
  return group
}

const fakeT: NavT = (key) => `EN:${key}`

describe('findNavItemByPath 最长前缀匹配', () => {
  it('精确导航路径命中对应项', () => {
    expect(findNavItemByPath('/data-integration/pipelines')?.title).toBe('数据管道')
    expect(findNavItemByPath('/system/notification')?.title).toBe('通知设置')
    expect(findNavItemByPath('/system/notification-recipients')?.title).toBe('通知接收人')
    expect(findNavItemByPath('/system/notification-logs')?.title).toBe('通知日志')
  })

  it('父项的动态子路由仍归属父项，相似前缀但非斜杠边界不误命中', () => {
    expect(findNavItemByPath('/system/scheduled-jobs/executions')?.title).toBe('定时任务')
    expect(findNavItemByPath('/system/notification-extra')).toBeNull()
    expect(findNavItemByPath('/no-such-page')).toBeNull()
  })
})

describe('pathMatchesHref 斜杠分界', () => {
  it('精确路径与子路径命中，相似前缀不命中', () => {
    expect(pathMatchesHref('/system/scheduled-jobs', '/system/scheduled-jobs')).toBe(true)
    expect(pathMatchesHref('/system/scheduled-jobs/executions', '/system/scheduled-jobs')).toBe(true)
    expect(pathMatchesHref('/system/notification-logs', '/system/notification')).toBe(false)
  })
})

describe('侧栏归属与独有前缀', () => {
  it('各业务组具备清晰独有前缀且互不占用', () => {
    expect(uniqueGroupPathPrefix(groupByKey('data'))).toBe('/data-integration')
    expect(uniqueGroupPathPrefix(groupByKey('message'))).toBe('/message-bus')
    expect(uniqueGroupPathPrefix(groupByKey('api'))).toBe('/api-management')
    expect(uniqueGroupPathPrefix(groupByKey('enterprise'))).toBe('/enterprise-management')
    expect(uniqueGroupPathPrefix(groupByKey('account'))).toBe('/account-management')
    expect(uniqueGroupPathPrefix(groupByKey('system'))).toBe('/system')
    expect(uniqueGroupPathPrefix(groupByKey('docs'))).toBe('/docs')

    for (const group of navGroups) {
      const prefix = uniqueGroupPathPrefix(group)
      if (!prefix) continue
      const trespassers = navGroups.flatMap((other) =>
        other.key === group.key
          ? []
          : other.items.filter((item) => item.href === prefix || item.href.startsWith(`${prefix}/`)).map((item) => `${other.key}:${item.href}`),
      )
      expect(trespassers, `${group.key} 的独有前缀 ${prefix} 被占用`).toEqual([])
    }
  })

  it('路径正确映射到所属组与所属域，个人中心不粘滞任何业务域', () => {
    expect(findSidebarHighlightItem('/data-integration/pipelines')?.title).toBe('数据管道')
    expect(findNavGroupByPath('/data-integration/pipelines')?.key).toBe('data')
    expect(findNavDomainKeyByPath('/data-integration/pipelines')).toBe('integration')
    expect(isPathInNavGroup('/data-integration/pipelines', groupByKey('data'))).toBe(true)
    expect(isPathInNavGroup('/data-integration/pipelines', groupByKey('message'))).toBe(false)

    expect(findNavGroupByPath('/profile')).toBeNull()
    expect(findNavDomainKeyByPath('/profile')).toBeNull()
    expect(findNavDomainKeyByPath('/no-such-page')).toBeNull()
  })
})

describe('getPageTitle', () => {
  it('仪表盘与个人中心固定标题，定时任务执行历史覆盖为独立标题', () => {
    expect(getPageTitle('/dashboard')).toBe('仪表盘')
    expect(getPageTitle('/profile')).toBe('个人中心')
    expect(getPageTitle('/system/scheduled-jobs/executions')).toBe('执行历史')
    expect(getPageTitle('/system/scheduled-jobs/executions', fakeT)).toBe('EN:pages.scheduledJobExecutions')
  })
})

describe('translateNavTitle / translateNavDescription', () => {
  it('有 titleKey + 传 t：返回 t(titleKey)，否则回退中文原文', () => {
    expect(translateNavTitle({ title: '企业', titleKey: 'items.enterprise.enterprises' }, fakeT)).toBe('EN:items.enterprise.enterprises')
    expect(translateNavTitle({ title: '企业', titleKey: 'items.enterprise.enterprises' })).toBe('企业')
    expect(translateNavTitle({ title: '企业' }, fakeT)).toBe('企业')
    expect(translateNavDescription({ description: '数据同步', descriptionKey: 'domains.integration.description' }, fakeT))
      .toBe('EN:domains.integration.description')
    expect(translateNavDescription({ description: '数据同步' }, fakeT)).toBe('数据同步')
  })
})

describe('resolveTabTitle（页签标题现场求值，不吞动态业务标题）', () => {
  it('静态导航页签现场翻译，动态覆盖后的标题原样保留', () => {
    const tab = { key: '/data-integration/pipelines', title: '数据管道' }
    expect(resolveTabTitle(tab, fakeT)).toBe('EN:items.data.pipelines')
    expect(resolveTabTitle(tab)).toBe('数据管道')
    expect(resolveTabTitle({ key: '/dashboard', title: '仪表盘' }, fakeT)).toBe('EN:pages.dashboard')
    expect(resolveTabTitle({ key: '/data-integration/pipelines', title: '订单管道 · 运行中' }, fakeT)).toBe('订单管道 · 运行中')
  })
})

describe('navDomains 覆盖全部 navGroups', () => {
  it('每个导航组恰好归入一个业务域', () => {
    const assigned = navDomains.flatMap((domain) => domain.groupKeys)
    const groupKeys = navGroups.map((group) => group.key)
    const orphans = groupKeys.filter((key) => !assigned.includes(key))
    const unknown = assigned.filter((key) => !groupKeys.includes(key))
    const duplicates = assigned.filter((key, index) => assigned.indexOf(key) !== index)

    expect(orphans, `未归入任何域: ${orphans.join(', ')}`).toEqual([])
    expect(unknown, `域引用了不存在的组: ${unknown.join(', ')}`).toEqual([])
    expect(duplicates, `组被多个域重复引用: ${duplicates.join(', ')}`).toEqual([])
  })
})

describe('权限过滤与路由可达性', () => {
  it('按权限过滤可见域与路由访问控制', () => {
    expect(resolveDomains({})).toEqual([])
    expect(canAccessPath('/dashboard', {})).toBe(true)
    expect(canAccessPath('/profile', {})).toBe(true)
    expect(canAccessPath('/data-integration/pipelines', {})).toBe(false)

    const policies = { 'SwapMid.DataIntegration.DataPipelines': true }
    expect(canAccessPath('/data-integration/pipelines', policies)).toBe(true)
    expect(resolveDomains(policies).map((d) => d.domain.key)).toEqual(['integration'])

    const item = findNavItemByPath('/data-integration/pipelines')!
    expect(isNavItemVisible(item, policies)).toBe(true)
    expect(isNavItemVisible(item, {})).toBe(false)
  })
})

describe('业务域元数据', () => {
  const allDomains = [...navDomains, WORKBENCH_DOMAIN]
  const zhNav = zhMessages.Nav as unknown as Record<string, unknown>
  const lookup = (key: string) => key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], zhNav)

  it('标题与描述和简中词典一致', () => {
    for (const domain of allDomains) {
      expect(domain.titleKey, `${domain.key} 缺 titleKey`).toBeTruthy()
      expect(domain.descriptionKey, `${domain.key} 缺 descriptionKey`).toBeTruthy()
      expect(lookup(domain.titleKey!), domain.key).toBe(domain.title)
      expect(lookup(domain.descriptionKey!), domain.key).toBe(domain.description)
    }
  })

  it('图标两两不同', () => {
    const owners = new Map<unknown, string[]>()
    for (const domain of allDomains) owners.set(domain.icon, [...(owners.get(domain.icon) ?? []), domain.key])
    const clashes = [...owners.values()].filter((keys) => keys.length > 1)
    expect(clashes, `图标撞车: ${clashes.map((keys) => keys.join('/')).join(', ')}`).toEqual([])
  })
})
