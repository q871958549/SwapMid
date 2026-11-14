import { describe, it, expect, beforeEach } from 'vitest'
import { useTabStore, resolveStoredPagePath, HOME_KEY, HOME_TAB } from './use-tab-store'

beforeEach(() => {
  useTabStore.setState({ tabs: [HOME_TAB], activeKey: HOME_KEY })
})

describe('useTabStore.updateActivePath（URL 持久化配套）', () => {
  it('更新当前激活页签的 path（含 query），key 不变', () => {
    const { openTab, updateActivePath } = useTabStore.getState()
    openTab({ key: '/list', title: '列表', path: '/list' }) // 打开即激活
    updateActivePath('/list?status=0&page=2')

    const tab = useTabStore.getState().tabs.find(t => t.key === '/list')
    expect(tab?.path).toBe('/list?status=0&page=2')
    expect(useTabStore.getState().activeKey).toBe('/list')
  })

  it('只改激活页签，其它页签 path 不受影响', () => {
    const { openTab, updateActivePath, setActive } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    openTab({ key: '/b', title: 'B', path: '/b' })
    setActive('/a')

    updateActivePath('/a?x=1')

    const tabs = useTabStore.getState().tabs
    expect(tabs.find(t => t.key === '/a')?.path).toBe('/a?x=1')
    expect(tabs.find(t => t.key === '/b')?.path).toBe('/b')
  })
})

describe('useTabStore.openTab（URL 为事实源）', () => {
  it('已存在页签：激活并把 path 刷新为本次进入的完整 URL', () => {
    const { openTab, updateActivePath, setActive } = useTabStore.getState()
    openTab({ key: '/list', title: '列表', path: '/list' })
    updateActivePath('/list?status=1&page=3')
    setActive(HOME_KEY)

    // 带新 query 的定向链接进入 → 页签记住新筛选
    openTab({ key: '/list', title: '列表', path: '/list?status=pending' })
    expect(useTabStore.getState().tabs.find(t => t.key === '/list')?.path).toBe('/list?status=pending')
    expect(useTabStore.getState().activeKey).toBe('/list')

    // 裸路径进入 → 显式回到默认视图，旧筛选不复活
    useTabStore.getState().setActive(HOME_KEY)
    openTab({ key: '/list', title: '列表', path: '/list' })
    expect(useTabStore.getState().tabs.find(t => t.key === '/list')?.path).toBe('/list')
  })
})

describe('useTabStore.updateTabTitle（§15 轮H：详情页动态精确化页签标题）', () => {
  it('已存在页签：把标题改成业务名称', () => {
    const { openTab, updateTabTitle } = useTabStore.getState()
    openTab({ key: '/project-management/samples/1', title: '样品详情', path: '/project-management/samples/1' })

    updateTabTitle('/project-management/samples/1', 'A 品名样品')

    expect(useTabStore.getState().tabs.find(t => t.key === '/project-management/samples/1')?.title).toBe('A 品名样品')
  })

  it('两个不同详情页页签更新后标题互不相同', () => {
    const { openTab, updateTabTitle } = useTabStore.getState()
    openTab({ key: '/project-management/samples/1', title: '样品详情', path: '/project-management/samples/1' })
    openTab({ key: '/project-management/samples/2', title: '样品详情', path: '/project-management/samples/2' })

    updateTabTitle('/project-management/samples/1', 'A 品名样品')
    updateTabTitle('/project-management/samples/2', 'B 品名样品')

    const tabs = useTabStore.getState().tabs
    expect(tabs.find(t => t.key === '/project-management/samples/1')?.title).toBe('A 品名样品')
    expect(tabs.find(t => t.key === '/project-management/samples/2')?.title).toBe('B 品名样品')
  })

  it('key 不存在（页签已关闭）→ 静默忽略，不抛错、不新增页签', () => {
    const { updateTabTitle } = useTabStore.getState()
    const before = useTabStore.getState().tabs.length

    expect(() => updateTabTitle('/never-opened', '不存在')).not.toThrow()

    expect(useTabStore.getState().tabs.length).toBe(before)
  })

  it('幂等：标题未变时重复调用不产生新的 tabs 数组引用', () => {
    const { openTab, updateTabTitle } = useTabStore.getState()
    openTab({ key: '/list', title: '列表', path: '/list' })
    updateTabTitle('/list', '已改名')
    const tabsRef1 = useTabStore.getState().tabs

    updateTabTitle('/list', '已改名') // 同标题重复调用

    expect(useTabStore.getState().tabs).toBe(tabsRef1) // 引用不变 = 未触发 set
  })

  it('只改目标页签，其它页签标题不受影响', () => {
    const { openTab, updateTabTitle } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    openTab({ key: '/b', title: 'B', path: '/b' })

    updateTabTitle('/a', 'A 改名后')

    const tabs = useTabStore.getState().tabs
    expect(tabs.find(t => t.key === '/a')?.title).toBe('A 改名后')
    expect(tabs.find(t => t.key === '/b')?.title).toBe('B')
  })
})

describe('useTabStore.setTabsOrder（Reorder.Group 落点回调）', () => {
  it('正常重排：非 Home 页签换序生效', () => {
    const { openTab, setTabsOrder } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    openTab({ key: '/b', title: 'B', path: '/b' })
    const tabs = useTabStore.getState().tabs // [Home, A, B]

    setTabsOrder([tabs[0], tabs[2], tabs[1]]) // Home, B, A

    expect(useTabStore.getState().tabs.map(t => t.key)).toEqual([HOME_KEY, '/b', '/a'])
  })

  it('Home 被拖离 0 位：自动钉回索引 0，其余相对顺序保留', () => {
    const { openTab, setTabsOrder } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    openTab({ key: '/b', title: 'B', path: '/b' })
    const tabs = useTabStore.getState().tabs // [Home, A, B]

    setTabsOrder([tabs[1], tabs[0], tabs[2]]) // A, Home, B —— Home 不在 0 位

    expect(useTabStore.getState().tabs.map(t => t.key)).toEqual([HOME_KEY, '/a', '/b'])
  })

  it('钉回结果与现序一致：不 set，tabs 引用不变（拖拽压 Home 中线高频回调不引发重渲染）', () => {
    const { openTab, setTabsOrder } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    openTab({ key: '/b', title: 'B', path: '/b' })
    const before = useTabStore.getState().tabs // [Home, A, B]

    setTabsOrder([before[1], before[0], before[2]]) // A, Home, B → 钉回 [Home, A, B] = 现序

    expect(useTabStore.getState().tabs).toBe(before) // 引用不变 = 未触发 set
  })

  it('钉回且顺序确有变化：正常生效', () => {
    const { openTab, setTabsOrder } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    openTab({ key: '/b', title: 'B', path: '/b' })
    const tabs = useTabStore.getState().tabs // [Home, A, B]

    setTabsOrder([tabs[2], tabs[0], tabs[1]]) // B, Home, A → 钉回 [Home, B, A] ≠ 现序

    expect(useTabStore.getState().tabs.map(t => t.key)).toEqual([HOME_KEY, '/b', '/a'])
  })

  it('成员不符（长度不同 / 含未知 key）：整次 set 忽略', () => {
    const { openTab, setTabsOrder } = useTabStore.getState()
    openTab({ key: '/a', title: 'A', path: '/a' })
    const before = useTabStore.getState().tabs // [Home, A]

    setTabsOrder([before[0]]) // 长度不符（少了 /a）
    expect(useTabStore.getState().tabs).toBe(before) // 引用不变 = 未 set

    setTabsOrder([before[0], { key: '/unknown', title: 'X', path: '/unknown' }]) // 含未知 key
    expect(useTabStore.getState().tabs).toBe(before)
  })
})

describe('resolveStoredPagePath（导航入口收口）', () => {
  it('裸路径且已有页签 → 返回存量 path（含 query）', () => {
    const { openTab, updateActivePath } = useTabStore.getState()
    openTab({ key: '/list', title: '列表', path: '/list' })
    updateActivePath('/list?status=1&page=3')

    expect(resolveStoredPagePath('/list')).toBe('/list?status=1&page=3')
  })

  it('无对应页签 → 原样返回', () => {
    expect(resolveStoredPagePath('/never-opened')).toBe('/never-opened')
  })

  it('自带 query 的定向链接 → 不改写', () => {
    const { openTab, updateActivePath } = useTabStore.getState()
    openTab({ key: '/list', title: '列表', path: '/list' })
    updateActivePath('/list?status=1')

    expect(resolveStoredPagePath('/list?status=pending')).toBe('/list?status=pending')
  })
})
