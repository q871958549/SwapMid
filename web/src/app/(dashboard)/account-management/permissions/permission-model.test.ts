import { describe, expect, it } from 'vitest'
import { applyPermissionSelection, selectPermissionScopes } from './permission-model'
import type { PermissionGroup } from './permission-tree'

const groups: PermissionGroup[] = [{ name: 'Sales', displayName: '销售', permissions: [
  { name: 'Orders', displayName: '订单', parentName: null, isGranted: false },
  { name: 'Orders.Create', displayName: '新建', parentName: 'Orders', isGranted: false },
  { name: 'Orders.Delete', displayName: '删除', parentName: 'Orders', isGranted: false },
  { name: 'Customers', displayName: '客户', parentName: null, isGranted: true },
] }]
const baseline = Object.fromEntries(groups[0].permissions.map(item => [item.name, item.isGranted]))

describe('权限筛选及依赖联动', () => {
  it('搜索删除保留父级上下文，但批量授予不包含隐藏的新建', () => {
    const [scope] = selectPermissionScopes(groups, '删除', 'all', baseline, baseline, {})
    expect(scope.names).toEqual(['Orders.Delete'])
    expect(scope.nodes[0].perm.name).toBe('Orders')
    expect(applyPermissionSelection(groups, baseline, scope.names, true)).toEqual({ Orders: true, 'Orders.Create': false, 'Orders.Delete': true, Customers: true })
  })
  it('取消父权限撤销其后代，保留其他资源', () => {
    const granted = { ...baseline, Orders: true, 'Orders.Create': true, 'Orders.Delete': true }
    expect(applyPermissionSelection(groups, granted, ['Orders'], false)).toEqual(baseline)
  })
  it('已修改筛选逐项比较基线，不把上下文父级算作变更', () => {
    const granted = { ...baseline, Customers: false, 'Orders.Delete': true }
    expect(selectPermissionScopes(groups, '', 'changed', granted, baseline, {})[0].names).toEqual(['Orders.Delete', 'Customers'])
    expect(selectPermissionScopes(groups, '', 'changed', baseline, baseline, {})).toEqual([])
  })
})
