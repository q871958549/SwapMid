import { buildTree, collectNodeNames, normalizeSearch, type Permission, type PermissionGroup, type PermissionGuide, type PermNode } from './permission-tree'

export type GrantFilter = 'all' | 'granted' | 'ungranted' | 'changed'
export interface PermissionScope {
  group: PermissionGroup
  nodes: PermNode[]
  /** 真正匹配筛选的权限；为了上下文保留的父节点不计入批量操作。 */
  names: string[]
}

export function selectPermissionScopes(groups: PermissionGroup[], query: string, filter: GrantFilter,
  granted: Record<string, boolean>, baseline: Record<string, boolean>, guides: Record<string, PermissionGuide>): PermissionScope[] {
  const search = normalizeSearch(query)
  return groups.flatMap(group => {
    const names: string[] = []
    const groupMatches = !search || `${group.displayName} ${group.name}`.toLowerCase().includes(search)
    function visit(nodes: PermNode[], inheritedMatch: boolean): PermNode[] {
      return nodes.flatMap(node => {
        const guide = guides[node.perm.name]
        const text = [node.perm.displayName, node.perm.name, guide?.impact, guide?.suggested, guide?.confuseWith].join(' ').toLowerCase()
        const textMatches = inheritedMatch || text.includes(search)
        const checked = granted[node.perm.name] === true
        const stateMatches = filter === 'all' || (filter === 'granted' && checked) || (filter === 'ungranted' && !checked)
          || (filter === 'changed' && checked !== (baseline[node.perm.name] === true))
        const matched = textMatches && stateMatches
        if (matched) names.push(node.perm.name)
        const children = visit(node.children, textMatches)
        return matched || children.length ? [{ ...node, children }] : []
      })
    }
    const nodes = visit(buildTree(group.permissions), groupMatches)
    return nodes.length ? [{ group, nodes, names }] : []
  })
}

/** 授予子权限时补齐父权限；取消父权限时同步撤销后代，不改变无关分支。 */
export function applyPermissionSelection(groups: PermissionGroup[], current: Record<string, boolean>, names: string[], value: boolean) {
  const index = new Map(groups.flatMap(group => group.permissions).map(permission => [permission.name, permission]))
  const children = new Map<string, string[]>()
  for (const permission of index.values()) {
    if (permission.parentName) children.set(permission.parentName, [...(children.get(permission.parentName) ?? []), permission.name])
  }
  const next = { ...current }
  const visited = new Set<string>()
  function update(name: string) {
    if (!index.has(name) || visited.has(name)) return
    visited.add(name)
    next[name] = value
    if (value) {
      const parent = index.get(name)?.parentName
      if (parent) update(parent)
    } else {
      children.get(name)?.forEach(update)
    }
  }
  names.forEach(update)
  return next
}

export function namesInResource(node: PermNode, scopeNames: string[]) {
  const resourceNames = new Set(collectNodeNames(node))
  return scopeNames.filter(name => resourceNames.has(name))
}

export function permissionBreadcrumb(permission: Permission, permissions: Permission[]) {
  const index = new Map(permissions.map(item => [item.name, item]))
  const labels = [permission.displayName || permission.name]
  const visited = new Set([permission.name])
  let parent = permission.parentName
  while (parent && !visited.has(parent)) {
    visited.add(parent)
    const item = index.get(parent)
    if (!item) break
    labels.unshift(item.displayName || item.name)
    parent = item.parentName
  }
  return labels.join(' / ')
}
