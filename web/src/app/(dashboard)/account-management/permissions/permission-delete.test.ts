import { describe, expect, it } from 'vitest'
import { listGrantedDeletePermissions, permissionChanges, type PermissionGroup } from './permission-tree'

const groups: PermissionGroup[] = [
  { name: 'Sales', displayName: '销售', permissions: [
    { name: 'Sales.Orders', displayName: '订单', parentName: null, isGranted: true },
    { name: 'Sales.Orders.Create', displayName: '新建', parentName: 'Sales.Orders', isGranted: true },
    { name: 'Sales.Orders.Delete', displayName: '删除订单', parentName: 'Sales.Orders', isGranted: true },
    { name: 'Sales.Options.Manage', displayName: '维护（禁止删除）', parentName: 'Sales.Options', isGranted: true },
  ] },
  { name: 'Customs', displayName: '报关', permissions: [
    { name: 'Customs.Products.Delete', displayName: '删除物料', parentName: 'Customs.Products', isGranted: true },
    { name: 'Customs.Customers.Delete', displayName: '删除客户', parentName: 'Customs.Customers', isGranted: false },
  ] },
]

describe('取消全部删除权限', () => {
  it('跨模块找出当前已勾选的删除权限，不把维护说明中的删除字样当操作', () => {
    const baseline = Object.fromEntries(groups.flatMap(group => group.permissions).map(p => [p.name, p.isGranted]))
    const selected = listGrantedDeletePermissions(groups, baseline)
    expect(selected.map(p => p.name)).toEqual(['Sales.Orders.Delete', 'Customs.Products.Delete'])
    const changed = { ...baseline }
    selected.forEach(p => { changed[p.name] = false })
    expect(permissionChanges(changed, baseline)).toEqual([
      { name: 'Sales.Orders.Delete', isGranted: false },
      { name: 'Customs.Products.Delete', isGranted: false },
    ])
    expect(changed['Sales.Orders']).toBe(true)
    expect(changed['Sales.Orders.Create']).toBe(true)
    expect(changed['Sales.Options.Manage']).toBe(true)
  })

  it('使用当前草稿而不是加载时状态；不撤销取消、撤回等其他业务操作', () => {
    const granted = { 'Sales.Orders.Delete': false, 'Customs.Customers.Delete': true, 'Sales.Orders.Cancel': true }
    expect(listGrantedDeletePermissions(groups, granted).map(p => p.name)).toEqual(['Customs.Customers.Delete'])
    expect(permissionChanges(granted, granted)).toEqual([])
  })
})
