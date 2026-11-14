import { describe, expect, it } from 'vitest'
import { navDomains, navGroups } from '@/components/layout/nav-config'

const BUDGET = { domains: 12, groupsPerDomain: 8, itemsPerGroup: 12 }

const GROUPS_PER_DOMAIN_BASELINE: Record<string, number> = {}
const ITEMS_PER_GROUP_BASELINE: Record<string, number> = {}

function ratchet(actual: Record<string, number>, budget: number, baseline: Record<string, number>) {
  const overBudget = Object.entries(actual)
    .filter(([key, count]) => count > Math.max(budget, baseline[key] ?? 0))
    .map(([key, count]) => `${key}: ${count}（上限 ${Math.max(budget, baseline[key] ?? 0)}）`)
  const tighten = Object.entries(baseline)
    .filter(([key, recorded]) => (actual[key] ?? 0) < recorded)
    .map(([key, recorded]) => `${key}: 基线 ${recorded} → 实际 ${actual[key] ?? 0}`)
  return { overBudget, tighten }
}

describe('业务域导航规模棘轮', () => {
  it(`业务域不超过 ${BUDGET.domains} 个`, () => {
    expect(navDomains.length).toBeLessThanOrEqual(BUDGET.domains)
  })

  it(`域内组数不超预算 ${BUDGET.groupsPerDomain}，已超的不再增加`, () => {
    const actual = Object.fromEntries(navDomains.map((domain) => [domain.key, domain.groupKeys.length]))
    const { overBudget, tighten } = ratchet(actual, BUDGET.groupsPerDomain, GROUPS_PER_DOMAIN_BASELINE)
    expect(overBudget, '组数超标：新组挂进其他域或合并现有组').toEqual([])
    expect(tighten, '组数已减少：把 GROUPS_PER_DOMAIN_BASELINE 收紧到实际值（降到预算内则删掉该行）').toEqual([])
  })

  it(`组内项数不超预算 ${BUDGET.itemsPerGroup}，已超的不再增加`, () => {
    const actual = Object.fromEntries(navGroups.map((group) => [group.key, group.items.length]))
    const { overBudget, tighten } = ratchet(actual, BUDGET.itemsPerGroup, ITEMS_PER_GROUP_BASELINE)
    expect(overBudget, '项数超标：拆分组，或把低频入口收进页内').toEqual([])
    expect(tighten, '项数已减少：把 ITEMS_PER_GROUP_BASELINE 收紧到实际值（降到预算内则删掉该行）').toEqual([])
  })
})
