import { describe, it, expect } from 'vitest'
import { diffGranted, filterNodes, formatPermName, type PermNode } from './permission-tree'

// P0-3 展示层剥前缀纯函数的回归网：仅影响展示，搜索/提交仍用全名（filterNodes/permissionMatches 不经它）。
describe('formatPermName', () => {
  it('平台前缀剥除：SwapMid.X.Y → X.Y', () => {
    expect(formatPermName('SwapMid.DataIntegration.DataSources')).toBe('DataIntegration.DataSources')
    // 组名（两段）同样适用——权限页左列组 name 也经它展示
    expect(formatPermName('SwapMid.EnterpriseManagement')).toBe('EnterpriseManagement')
  })

  it('ABP 内置组名不带平台前缀，原样返回', () => {
    expect(formatPermName('AbpIdentity.Roles')).toBe('AbpIdentity.Roles')
    expect(formatPermName('FeatureManagement')).toBe('FeatureManagement')
  })

  it('边界：恰好等于前缀串（含尾点）剥成空串；不含尾点的裸平台名不满足 startsWith 前缀、原样返回', () => {
    // 真实权限数据不存在这两种形态（权限 name 恒为「前缀+段」/ 内置组名），此例锁定当前实现语义防回归。
    expect(formatPermName('SwapMid.')).toBe('')
    expect(formatPermName('SwapMid')).toBe('SwapMid')
  })
})

// P1-3 角色权限对比核心逻辑：按权限名集合运算，不关心分组/树结构。
describe('diffGranted', () => {
  it('划分仅 A / 仅 B / 两者均有三态', () => {
    const a = ['Perm.A', 'Perm.Common1', 'Perm.Common2']
    const b = ['Perm.B', 'Perm.Common1', 'Perm.Common2']
    const result = diffGranted(a, b)
    expect(result.onlyA).toEqual(['Perm.A'])
    expect(result.onlyB).toEqual(['Perm.B'])
    expect(result.both.sort()).toEqual(['Perm.Common1', 'Perm.Common2'])
  })

  it('两者完全一致：onlyA/onlyB 为空，both 等于全集', () => {
    const a = ['Perm.X', 'Perm.Y']
    const b = ['Perm.Y', 'Perm.X'] // 顺序不同，集合语义应仍判定一致
    const result = diffGranted(a, b)
    expect(result.onlyA).toEqual([])
    expect(result.onlyB).toEqual([])
    expect(result.both.sort()).toEqual(['Perm.X', 'Perm.Y'])
  })

  it('两者完全不相交：both 为空', () => {
    const result = diffGranted(['Perm.A'], ['Perm.B'])
    expect(result.onlyA).toEqual(['Perm.A'])
    expect(result.onlyB).toEqual(['Perm.B'])
    expect(result.both).toEqual([])
  })

  it('空数组输入：三个集合均为空，不抛异常', () => {
    expect(diffGranted([], [])).toEqual({ onlyA: [], onlyB: [], both: [] })
    expect(diffGranted(['Perm.A'], [])).toEqual({ onlyA: ['Perm.A'], onlyB: [], both: [] })
    expect(diffGranted([], ['Perm.B'])).toEqual({ onlyA: [], onlyB: ['Perm.B'], both: [] })
  })

  it('入参含重复名：Set 去重，不影响结果集合（不校验重复计数）', () => {
    const result = diffGranted(['Perm.A', 'Perm.A'], ['Perm.A'])
    expect(result.onlyA).toEqual([])
    expect(result.onlyB).toEqual([])
    expect(result.both).toEqual(['Perm.A'])
  })
})

describe('filterNodes 说明目录', () => {
  const tree: PermNode[] = [
    {
      perm: { name: 'SwapMid.DataIntegration.DataPipelines', displayName: '数据管道', parentName: null, isGranted: true },
      children: [
        { perm: { name: 'SwapMid.DataIntegration.DataPipelines.Execute', displayName: '手动执行', parentName: 'SwapMid.DataIntegration.DataPipelines', isGranted: false }, children: [] },
      ],
    },
  ]

  it('可用后果说明搜到权限，即使显示名对不上', () => {
    const filtered = filterNodes(tree, '立即触发', {
      'SwapMid.DataIntegration.DataPipelines.Execute': {
        name: 'SwapMid.DataIntegration.DataPipelines.Execute',
        kind: 0,
        impact: '关掉后不能立即触发同步任务',
      },
    })
    expect(filtered).toHaveLength(1)
    expect(filtered[0]?.children[0]?.perm.displayName).toBe('手动执行')
  })

  it('没有说明目录时仍只按名称搜', () => {
    expect(filterNodes(tree, '立即触发')).toEqual([])
    expect(filterNodes(tree, '手动')).toHaveLength(1)
  })
})
