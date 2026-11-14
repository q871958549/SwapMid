import { canAccessPath, findNavDomainKeyByPath } from '@/lib/nav-utils'

/**
 * 业务域个人偏好（蓝图 P1-1）：只存「与内置不同」的部分，存后端 Setting `Layout.DomainPrefs`（User 级）。
 * - order：个人排序（只列出动过的域；未列出的按内置顺序追加末尾，新上线的域自然排最后）
 * - hidden：隐藏的域，折进切域器末尾「更多业务域」，仍可搜到、仍可进入
 *
 * 蓝图原稿还有 pinned（置顶），落地时并入 order：「置顶」就是拖到最上面，两套机制并存只会让
 * 「置顶区内部怎么排」「置顶后再拖动算什么」变成需要解释的规则。
 */
export interface DomainPrefs {
  order: string[]
  hidden: string[]
}

export const EMPTY_DOMAIN_PREFS: DomainPrefs = { order: [], hidden: [] }

const DOMAIN_PREFS_VERSION = 1

function stringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((x): x is string => typeof x === 'string') : []
}

export function parseDomainPrefs(raw: string | null | undefined): DomainPrefs {
  if (!raw) return EMPTY_DOMAIN_PREFS
  try {
    const value = JSON.parse(raw) as { v?: unknown; order?: unknown; hidden?: unknown }
    if (value?.v !== DOMAIN_PREFS_VERSION) return EMPTY_DOMAIN_PREFS
    return { order: stringArray(value.order), hidden: stringArray(value.hidden) }
  } catch {
    return EMPTY_DOMAIN_PREFS
  }
}

/** 序列化前去掉未知键与「和内置顺序一致」的排序；什么都没改时返回空串（后端据此删掉该设置）。 */
export function serializeDomainPrefs(prefs: DomainPrefs, defaultKeys: string[]): string {
  const known = new Set(defaultKeys)
  const order = prefs.order.filter((key) => known.has(key))
  const hidden = defaultKeys.filter((key) => prefs.hidden.includes(key))
  const effective = applyDomainOrder(defaultKeys, order, (key) => key)
  const isDefaultOrder = effective.every((key, index) => key === defaultKeys[index])
  if (isDefaultOrder && hidden.length === 0) return ''
  return JSON.stringify({ v: DOMAIN_PREFS_VERSION, order: isDefaultOrder ? [] : effective, hidden })
}

/** 按个人顺序排列：order 里的在前（按 order），其余保持原相对顺序追加。 */
export function applyDomainOrder<T>(items: T[], order: string[], keyOf: (item: T) => string): T[] {
  if (order.length === 0) return items
  const rank = new Map(order.map((key, index) => [key, index]))
  const ranked = items.filter((item) => rank.has(keyOf(item)))
    .sort((a, b) => rank.get(keyOf(a))! - rank.get(keyOf(b))!)
  return [...ranked, ...items.filter((item) => !rank.has(keyOf(item)))]
}

/** 拆成「常用」（排好序）与「隐藏」两段；隐藏段保持内置顺序。 */
export function splitDomains<T>(items: T[], prefs: DomainPrefs, keyOf: (item: T) => string): { shown: T[]; hidden: T[] } {
  const hidden = new Set(prefs.hidden)
  const ordered = applyDomainOrder(items, prefs.order, keyOf)
  return {
    shown: ordered.filter((item) => !hidden.has(keyOf(item))),
    hidden: items.filter((item) => hidden.has(keyOf(item))),
  }
}

/**
 * 从「最近访问」（按时间倒序的 href）推出最近用过的业务域（蓝图 P1-2），去重后取前 limit 个。
 * 只认 availableKeys 内的域（权限回收后本地残留的记录自动失效）。
 */
export function deriveRecentDomainKeys(hrefs: string[], availableKeys: string[], excludeKey: string, limit = 2): string[] {
  const available = new Set(availableKeys)
  const result: string[] = []
  for (const href of hrefs) {
    const key = findNavDomainKeyByPath(href)
    if (!key || key === excludeKey || !available.has(key) || result.includes(key)) continue
    result.push(key)
    if (result.length >= limit) break
  }
  return result
}

/** 切到某域时要恢复的页面（蓝图 P1-3 / 决策 Q1）：该域最近访问且仍有权限的那一页；没有返回 null。 */
export function findResumeHref(hrefs: string[], domainKey: string, grantedPolicies: Record<string, boolean>): string | null {
  return hrefs.find((href) => findNavDomainKeyByPath(href) === domainKey && canAccessPath(href, grantedPolicies)) ?? null
}
