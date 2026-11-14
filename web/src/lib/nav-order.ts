// 导航栏顺序：组顺序 + 组内项顺序。持久化改为服务端（每账号独立；admin 保存即全局默认），
// 见 /api/proxy/nav-preference。本文件只保留纯函数 + JSON 序列化。

export interface NavOrderState {
  groupOrder: string[] | null
  itemOrders: Record<string, string[]>
}

export function parseNavOrder(json: string | null | undefined): NavOrderState {
  if (!json) return { groupOrder: null, itemOrders: {} }
  try {
    const parsed = JSON.parse(json) as Partial<NavOrderState>
    return {
      groupOrder: Array.isArray(parsed.groupOrder) ? parsed.groupOrder : null,
      itemOrders: parsed.itemOrders && typeof parsed.itemOrders === 'object' ? parsed.itemOrders : {},
    }
  } catch {
    return { groupOrder: null, itemOrders: {} }
  }
}

export function serializeNavOrder(state: NavOrderState): string {
  return JSON.stringify({ groupOrder: state.groupOrder ?? [], itemOrders: state.itemOrders })
}

/** 按 order 排列，未在 order 中的元素保持原相对顺序追加到末尾（兼容新增项）。 */
export function applyOrder<T>(items: T[], order: string[] | null, keyOf: (item: T) => string): T[] {
  if (!order || order.length === 0) return items
  const indexOf = new Map(order.map((key, i) => [key, i]))
  return items
    .map((item, i) => ({ item, key: keyOf(item), original: i }))
    .sort((a, b) => {
      const ai = indexOf.has(a.key) ? indexOf.get(a.key)! : Number.MAX_SAFE_INTEGER
      const bi = indexOf.has(b.key) ? indexOf.get(b.key)! : Number.MAX_SAFE_INTEGER
      return ai === bi ? a.original - b.original : ai - bi
    })
    .map((x) => x.item)
}

/**
 * 后端 AbpSettings.Value 是 varchar(2048)，超长会被 `NavPreferenceLimits`（Application/Layout）拦成 SwapMid:LAY:001。
 * 两处同值、互指；前端用它在保存前判断，避免明知存不下还发请求。
 */
export const NAV_ORDER_MAX_LENGTH = 2048

/**
 * 把「某个域内」的新组顺序并回全局组顺序。
 *
 * 全局 groupOrder 只有一份、跨所有域共用，但拖拽只发生在当前域里，收到的是当前域可见组的新顺序。
 * 直接拿它覆盖全局顺序，会把其他域的自定义顺序整段抹掉（2026-11-04 业务域导航蓝图 B1）。
 * `applyOrder` 只在域内比较相对位置，所以其他域的 key 原样保留、当前域的 key 移到末尾即可。
 */
export function mergeScopedOrder(globalOrder: string[] | null, nextScoped: string[]): string[] {
  const scoped = new Set(nextScoped)
  return [...(globalOrder ?? []).filter((key) => !scoped.has(key)), ...nextScoped]
}

/** order 去掉已不存在的 key 后，是否与默认顺序的相对位置一致（一致则存与不存效果相同）。 */
function knownKeysOrEmpty(order: string[], defaults: readonly string[]): string[] | null {
  const indexOf = new Map(defaults.map((key, i) => [key, i]))
  const known = order.filter((key) => indexOf.has(key))
  const inDefaultOrder = known.every((key, i) => i === 0 || indexOf.get(known[i - 1])! < indexOf.get(key)!)
  return inDefaultOrder ? null : known
}

/**
 * 保存前瘦身：去掉已下线的 key，丢弃与默认顺序一致的条目。
 * 偏好只记录「和默认不同的部分」，才不会随菜单变多撑爆 2048 字节（蓝图 B2）。
 */
export function compactNavOrder(
  state: NavOrderState,
  defaults: { groupKeys: readonly string[]; itemHrefsByGroup: Readonly<Record<string, readonly string[]>> },
): NavOrderState {
  const itemOrders: Record<string, string[]> = {}
  for (const [groupKey, order] of Object.entries(state.itemOrders)) {
    const defaultHrefs = defaults.itemHrefsByGroup[groupKey]
    if (!defaultHrefs) continue
    const kept = knownKeysOrEmpty(order, defaultHrefs)
    if (kept) itemOrders[groupKey] = kept
  }
  const groupOrder = state.groupOrder ? knownKeysOrEmpty(state.groupOrder, defaults.groupKeys) : null
  return { groupOrder, itemOrders }
}

/** 瘦身后序列化；与默认完全一致时返回空串——后端据此删掉该级设置，而不是存一个「空顺序」。 */
export function serializeCompactNavOrder(state: NavOrderState, defaults: Parameters<typeof compactNavOrder>[1]): string {
  const compact = compactNavOrder(state, defaults)
  if (compact.groupOrder === null && Object.keys(compact.itemOrders).length === 0) return ''
  return serializeNavOrder(compact)
}

/**
 * 按个人顺序排好组与组内项。侧栏单域视图、工作台全景、切域器预览、仪表盘域卡都走这里——
 * 以前仪表盘与工作台全景各自直接用内置顺序，侧栏里拖过的顺序在那里不生效（2026-11-05 用户反馈）。
 */
export function applyNavOrder<G extends { key: string; items: I[] }, I extends { href: string }>(
  groups: G[],
  state: NavOrderState,
): G[] {
  return applyOrder(groups, state.groupOrder, (group) => group.key)
    .map((group) => ({ ...group, items: applyOrder(group.items, state.itemOrders[group.key] ?? null, (item) => item.href) }))
}

/** 这几个组（通常是一个域的全部组）的组顺序与组内项顺序，是否和内置顺序不同。 */
export function isNavOrderCustomizedIn(
  state: NavOrderState,
  groupKeys: readonly string[],
  defaults: { itemHrefsByGroup: Readonly<Record<string, readonly string[]>> },
): boolean {
  const ordered = applyOrder([...groupKeys], state.groupOrder, (key) => key)
  if (ordered.some((key, i) => key !== groupKeys[i])) return true
  return groupKeys.some((key) => {
    const builtin = defaults.itemHrefsByGroup[key] ?? []
    return applyOrder([...builtin], state.itemOrders[key] ?? null, (href) => href).some((href, i) => href !== builtin[i])
  })
}

/**
 * 只把这几个组恢复成内置顺序，其他域的自定义原样保留。
 * 以前「重置」一刀清空全部域，在一个域里点一下就把其他域辛苦排好的顺序全丢了（2026-11-05 用户反馈）。
 * 组 key 从全局顺序里拿掉即可：applyOrder 只比较同域内的相对位置，没排名的组按内置顺序排。
 */
export function resetNavOrderIn(state: NavOrderState, groupKeys: readonly string[]): NavOrderState {
  const scope = new Set(groupKeys)
  const groupOrder = (state.groupOrder ?? []).filter((key) => !scope.has(key))
  const itemOrders = Object.fromEntries(Object.entries(state.itemOrders).filter(([key]) => !scope.has(key)))
  return { groupOrder: groupOrder.length > 0 ? groupOrder : null, itemOrders }
}
