import { describe, expect, it } from 'vitest'
import {
  buildDepartmentTree,
  collapseAllExpandedIds,
  collectAncestorIds,
  collectExpandableIds,
  defaultExpandedIds,
  filterTreeByQuery,
  findDeptNode,
  flattenVisibleRows,
  type DeptRecord,
} from './department-tree'

const depts: DeptRecord[] = [
  { id: 'root', name: '大研发平台', order: 1 },
  { id: 'product', parentId: 'root', name: '产品中心', order: 1 },
  { id: 'group', parentId: 'product', name: '产品组', order: 1 },
  { id: 'rd', parentId: 'root', name: '研发中心', order: 2 },
  { id: 'elec', parentId: 'rd', name: '电子研发部', code: 'RD-E', order: 1 },
  { id: 'orphan', parentId: 'missing', name: '无上级部门', order: 9 },
  { id: 'disabled', name: '已停用', isEnabled: false, order: 0 },
]

describe('buildDepartmentTree', () => {
  it('按 parentId 建树，缺失的上级当根，停用部门不入树', () => {
    const tree = buildDepartmentTree(depts)
    expect(tree.map(n => n.id)).toEqual(['root', 'orphan'])
    expect(tree[0].children.map(n => n.id)).toEqual(['product', 'rd'])
    expect(tree[0].children[0].children.map(n => n.id)).toEqual(['group'])
    expect(tree.some(n => n.id === 'disabled' || n.children.some(c => c.id === 'disabled'))).toBe(false)
  })

  it('同级按 order 再按名称', () => {
    const tree = buildDepartmentTree([
      { id: 'a', name: '乙', order: 2 },
      { id: 'b', name: '甲', order: 2 },
      { id: 'c', name: '丙', order: 1 },
    ])
    expect(tree.map(n => n.id)).toEqual(['c', 'b', 'a'])
  })
})

describe('展开与可见行', () => {
  const tree = buildDepartmentTree(depts)

  it('默认展开根节点一级，并展开选中项的祖先与自身', () => {
    const none = defaultExpandedIds(tree)
    expect([...none]).toEqual(['root'])

    const deep = defaultExpandedIds(tree, 'group')
    expect(deep.has('root')).toBe(true)
    expect(deep.has('product')).toBe(true)
    expect(deep.has('rd')).toBe(false)
  })

  it('收起全部后只保留选中路径', () => {
    const collapsed = collapseAllExpandedIds(tree, 'group')
    expect([...collapsed].sort()).toEqual(['product', 'root'])
    expect(collapseAllExpandedIds(tree).size).toBe(0)
  })

  it('flatten 只输出已展开路径上的行', () => {
    const rows = flattenVisibleRows(tree, new Set(['root']))
    expect(rows.map(r => r.node.id)).toEqual(['root', 'product', 'rd', 'orphan'])
    expect(rows.find(r => r.node.id === 'root')).toMatchObject({ depth: 0, expandable: true, expanded: true })
    expect(rows.find(r => r.node.id === 'product')).toMatchObject({ depth: 1, expandable: true, expanded: false })
    expect(rows.find(r => r.node.id === 'group')).toBeUndefined()
  })

  it('collectAncestorIds / findDeptNode / collectExpandableIds', () => {
    expect(collectAncestorIds(tree, 'elec')).toEqual(['rd', 'root'])
    expect(findDeptNode(tree, 'elec')?.name).toBe('电子研发部')
    expect(collectExpandableIds(tree).sort()).toEqual(['product', 'rd', 'root'])
  })
})

describe('filterTreeByQuery', () => {
  const tree = buildDepartmentTree(depts)

  it('保留命中节点及其祖先，丢掉旁支', () => {
    const filtered = filterTreeByQuery(tree, '电子')
    expect(filtered.map(n => n.id)).toEqual(['root'])
    expect(filtered[0].children.map(n => n.id)).toEqual(['rd'])
    expect(filtered[0].children[0].children.map(n => n.id)).toEqual(['elec'])
  })

  it('按编码命中', () => {
    const filtered = filterTreeByQuery(tree, 'rd-e')
    expect(findDeptNode(filtered, 'elec')).not.toBeNull()
  })

  it('空查询原样返回', () => {
    expect(filterTreeByQuery(tree, '  ').map(n => n.id)).toEqual(tree.map(n => n.id))
  })
})
