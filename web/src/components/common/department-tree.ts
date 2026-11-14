/** 部门树：由扁平 parentId 列表建树、展开/过滤、可见行展开。供 DepartmentPicker 树状模式使用。 */

export type DeptRecord = {
  id: string
  parentId?: string | null
  name: string
  fullPath?: string | null
  code?: string | null
  order?: number
  isEnabled?: boolean
}

export type DeptTreeNode = {
  id: string
  parentId: string | null
  name: string
  fullPath: string
  code: string
  order: number
  children: DeptTreeNode[]
}

export type VisibleDeptRow = {
  node: DeptTreeNode
  depth: number
  expandable: boolean
  expanded: boolean
}

function compareDept(a: { name: string; order?: number }, b: { name: string; order?: number }) {
  const byOrder = (a.order ?? 0) - (b.order ?? 0)
  if (byOrder !== 0) return byOrder
  return a.name.localeCompare(b.name, 'zh-CN')
}

export function buildDepartmentTree(depts: readonly DeptRecord[]): DeptTreeNode[] {
  const enabled = depts.filter(d => d.isEnabled !== false && d.id)
  const byId = new Map(enabled.map(d => [d.id, d]))
  const grouped = new Map<string | null, DeptRecord[]>()

  for (const dept of enabled) {
    const parentId = dept.parentId && byId.has(dept.parentId) ? dept.parentId : null
    const siblings = grouped.get(parentId)
    if (siblings) siblings.push(dept)
    else grouped.set(parentId, [dept])
  }

  function toNode(dept: DeptRecord): DeptTreeNode {
    const kids = (grouped.get(dept.id) ?? []).slice().sort(compareDept)
    return {
      id: dept.id,
      parentId: dept.parentId && byId.has(dept.parentId) ? dept.parentId : null,
      name: dept.name,
      fullPath: dept.fullPath || dept.name,
      code: dept.code ?? '',
      order: dept.order ?? 0,
      children: kids.map(toNode),
    }
  }

  return (grouped.get(null) ?? []).slice().sort(compareDept).map(toNode)
}

export function findDeptNode(nodes: readonly DeptTreeNode[], id: string | undefined): DeptTreeNode | null {
  if (!id) return null
  for (const node of nodes) {
    if (node.id === id) return node
    const found = findDeptNode(node.children, id)
    if (found) return found
  }
  return null
}

export function collectAncestorIds(nodes: readonly DeptTreeNode[], id: string | undefined): string[] {
  if (!id) return []
  const parentById = new Map<string, string | null>()
  function walk(node: DeptTreeNode) {
    parentById.set(node.id, node.parentId)
    for (const child of node.children) walk(child)
  }
  for (const node of nodes) walk(node)

  const ids: string[] = []
  const seen = new Set<string>()
  let current: string | null | undefined = parentById.get(id) ?? null
  while (current && !seen.has(current)) {
    seen.add(current)
    ids.push(current)
    current = parentById.get(current) ?? null
  }
  return ids
}

export function collectExpandableIds(nodes: readonly DeptTreeNode[]): string[] {
  const ids: string[] = []
  function walk(node: DeptTreeNode) {
    if (node.children.length === 0) return
    ids.push(node.id)
    for (const child of node.children) walk(child)
  }
  for (const node of nodes) walk(node)
  return ids
}

/** 打开下拉时的默认展开：根节点展开一级；若有选中项，再展开其祖先与自身。 */
export function defaultExpandedIds(nodes: readonly DeptTreeNode[], selectedId?: string): Set<string> {
  const expanded = new Set<string>()
  for (const root of nodes) {
    if (root.children.length > 0) expanded.add(root.id)
  }
  if (selectedId) {
    for (const id of collectAncestorIds(nodes, selectedId)) expanded.add(id)
    const selected = findDeptNode(nodes, selectedId)
    if (selected && selected.children.length > 0) expanded.add(selected.id)
  }
  return expanded
}

/** 「收起全部」后仍露出当前选中项：保留选中路径（含自身若有下级）。 */
export function collapseAllExpandedIds(nodes: readonly DeptTreeNode[], selectedId?: string): Set<string> {
  const expanded = new Set<string>()
  if (!selectedId) return expanded
  for (const id of collectAncestorIds(nodes, selectedId)) expanded.add(id)
  const selected = findDeptNode(nodes, selectedId)
  if (selected && selected.children.length > 0) expanded.add(selected.id)
  return expanded
}

export function flattenVisibleRows(
  nodes: readonly DeptTreeNode[],
  expandedIds: ReadonlySet<string>,
): VisibleDeptRow[] {
  const rows: VisibleDeptRow[] = []
  function walk(node: DeptTreeNode, depth: number) {
    const expandable = node.children.length > 0
    const expanded = expandable && expandedIds.has(node.id)
    rows.push({ node, depth, expandable, expanded })
    if (expanded) {
      for (const child of node.children) walk(child, depth + 1)
    }
  }
  for (const node of nodes) walk(node, 0)
  return rows
}

export function matchesDeptQuery(node: DeptTreeNode, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  return node.name.toLowerCase().includes(needle)
    || node.fullPath.toLowerCase().includes(needle)
    || node.code.toLowerCase().includes(needle)
}

/** 保留命中节点及其祖先；无命中的旁支丢掉。搜索时配合「命中枝全展开」使用。 */
export function filterTreeByQuery(nodes: readonly DeptTreeNode[], query: string): DeptTreeNode[] {
  if (!query.trim()) return [...nodes]
  function filter(node: DeptTreeNode): DeptTreeNode | null {
    const children = node.children.map(filter).filter((child): child is DeptTreeNode => child != null)
    if (children.length > 0 || matchesDeptQuery(node, query)) {
      return { ...node, children }
    }
    return null
  }
  return nodes.map(filter).filter((node): node is DeptTreeNode => node != null)
}
