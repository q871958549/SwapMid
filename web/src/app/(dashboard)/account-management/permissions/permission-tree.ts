'use client'

// 权限树的类型与纯逻辑：DTO 类型、树构建/遍历/过滤、勾选态计算，以及角色远程搜索取数。
// 与 UI 无关，供 client.tsx 与 permission-nodes.tsx 共用。
import type { RemoteComboboxOption } from '@/components/ui/remote-combobox'

export interface AbpRole {
  id: string
  name: string
  isDefault: boolean
  isStatic: boolean
  isPublic: boolean
}

export interface Permission {
  name: string
  displayName: string
  parentName: string | null
  isGranted: boolean
}

/** 与后端 PermissionGuideKind 数值枚举对齐。 */
export const PermissionGuideKind = {
  Action: 0,
  DataScope: 1,
  Dangerous: 2,
  OpsOnly: 3,
} as const

export type PermissionGuide = import('@/lib/api/types').PermissionGuideDto

export interface PermissionGroup {
  name: string
  displayName: string
  permissions: Permission[]
}

export interface PermissionResponse {
  entityDisplayName: string
  groups: PermissionGroup[]
}

export interface PermNode {
  perm: Permission
  children: PermNode[]
}

export type CheckedState = boolean | 'indeterminate'

// 角色远程搜索：输入关键字 → 服务端按 filter 过滤，每次最多取 20 条，避免一次性加载全部角色。
// 空 query（初次打开）返回前 20 个角色作为默认列表。
// 纯函数（非组件），拿不到 hook：静态角色描述文案由调用方（组件内 useTranslations）经参数传入。
export async function fetchRoleOptions(
  query: string,
  systemRoleLabel: string,
  signal: AbortSignal,
): Promise<RemoteComboboxOption[]> {
  const params = new URLSearchParams({ maxResultCount: '20', skipCount: '0', sorting: 'name' })
  if (query) params.set('filter', query)
  const res = await fetch(`/api/proxy/account-roles?${params.toString()}`, { signal })
  if (!res.ok) throw new Error(await res.text())
  const data: { items: AbpRole[] } = await res.json()
  return data.items.map(role => ({
    value: role.name,
    label: role.name,
    description: role.isStatic ? systemRoleLabel : undefined,
  }))
}

export function buildTree(permissions: Permission[]): PermNode[] {
  const map = new Map<string, PermNode>()
  const roots: PermNode[] = []
  for (const permission of permissions) map.set(permission.name, { perm: permission, children: [] })
  for (const permission of permissions) {
    const node = map.get(permission.name)!
    if (permission.parentName && map.has(permission.parentName)) {
      map.get(permission.parentName)!.children.push(node)
    } else {
      roots.push(node)
    }
  }
  return roots
}

export function collectNames(nodes: PermNode[]): string[] {
  return nodes.flatMap(node => [node.perm.name, ...collectNames(node.children)])
}

export function collectNodeNames(node: PermNode): string[] {
  return [node.perm.name, ...collectNames(node.children)]
}

export function findNode(nodes: PermNode[], name: string): PermNode | undefined {
  for (const node of nodes) {
    if (node.perm.name === name) return node
    const child = findNode(node.children, name)
    if (child) return child
  }
  return undefined
}

export function findNodePath(nodes: PermNode[], name: string, path: PermNode[] = []): PermNode[] | undefined {
  for (const node of nodes) {
    const nextPath = [...path, node]
    if (node.perm.name === name) return nextPath
    const childPath = findNodePath(node.children, name, nextPath)
    if (childPath) return childPath
  }

  return undefined
}

export function normalizeSearch(value: string): string {
  return value.trim().toLowerCase()
}

function permissionMatches(
  permission: Permission,
  query: string,
  guide?: PermissionGuide,
): boolean {
  if (permission.displayName.toLowerCase().includes(query) || permission.name.toLowerCase().includes(query)) {
    return true
  }
  if (!guide) return false
  const haystack = [guide.impact, guide.suggested, guide.confuseWith]
    .filter((part): part is string => !!part)
    .join(' ')
    .toLowerCase()
  return haystack.includes(query)
}

export function filterNodes(
  nodes: PermNode[],
  query: string,
  guides: Record<string, PermissionGuide> = {},
): PermNode[] {
  if (!query) return nodes
  return nodes.flatMap(node => {
    const selfMatch = permissionMatches(node.perm, query, guides[node.perm.name])
    const filteredChildren = filterNodes(node.children, query, guides)
    if (selfMatch) return [{ ...node, children: node.children }]
    if (filteredChildren.length > 0) return [{ ...node, children: filteredChildren }]
    return []
  })
}

export function getCheckedState(names: string[], granted: Record<string, boolean>): CheckedState {
  if (names.length === 0) return false
  const grantedCount = names.filter(name => granted[name]).length
  if (grantedCount === 0) return false
  if (grantedCount === names.length) return true
  return 'indeterminate'
}

export function getGrantedCount(names: string[], granted: Record<string, boolean>): number {
  return names.filter(name => granted[name]).length
}

export function getPermissionTitle(permission: Permission): string {
  return permission.displayName || permission.name
}

export function listNewlyGrantedOps(
  granted: Record<string, boolean>,
  baselineGranted: Record<string, boolean>,
  guides: Record<string, PermissionGuide>,
): string[] {
  return Object.entries(granted)
    .filter(([name, isGranted]) =>
      isGranted
      && baselineGranted[name] !== true
      && guides[name]?.kind === PermissionGuideKind.OpsOnly)
    .map(([name]) => name)
}

export async function fetchPermissionGuides(signal?: AbortSignal): Promise<Record<string, PermissionGuide>> {
  const res = await fetch('/api/proxy/account-permission-guides', { signal })
  if (!res.ok) throw new Error(await res.text())
  const items = await res.json() as PermissionGuide[]
  const next: Record<string, PermissionGuide> = {}
  items.forEach(item => { if (item.name) next[item.name] = item })
  return next
}

// 权限/组 name 展示层剥前缀：平台自有组的 name 恒带 `SwapMid.` 固定前缀（34 字符纯噪声，
// 见 SwapMidPermissions.GroupName），窄列 mono 小字里必然截断；用户真正要分辨的只有
// 前缀之后的部分。仅影响展示——搜索（filterNodes/permissionMatches）与提交仍用 name 全名。
// ABP 内置组（AbpIdentity.* 等）不带该前缀，原样返回。
const PLATFORM_NAME_PREFIX = 'SwapMid.'

export function formatPermName(name: string): string {
  return name.startsWith(PLATFORM_NAME_PREFIX) ? name.slice(PLATFORM_NAME_PREFIX.length) : name
}

// P1-3 角色权限对比：按权限名集合运算，与分组/树结构解耦（对比视图按扁平清单展示，不需要父子层级）。
export interface GrantedDiff {
  /** 仅角色 A 已授予的权限名。 */
  onlyA: string[]
  /** 仅角色 B 已授予的权限名。 */
  onlyB: string[]
  /** 两角色均已授予的权限名。 */
  both: string[]
}

export function diffGranted(aGranted: string[], bGranted: string[]): GrantedDiff {
  const setA = new Set(aGranted)
  const setB = new Set(bGranted)
  const onlyA: string[] = []
  const onlyB: string[] = []
  const both: string[] = []
  for (const name of setA) {
    if (setB.has(name)) both.push(name)
    else onlyA.push(name)
  }
  for (const name of setB) {
    if (!setA.has(name)) onlyB.push(name)
  }
  return { onlyA, onlyB, both }
}

// 按权限标识识别删除操作，不用显示文案（说明里可能写着“禁止删除”）。
export function listGrantedDeletePermissions(groups: PermissionGroup[], granted: Record<string, boolean>): Permission[] {
  return groups.flatMap(group => group.permissions).filter(permission =>
    /[.:]Delete(?:[A-Z][a-zA-Z]*)?$/.test(permission.name) && granted[permission.name] === true)
}

export function permissionChanges(granted: Record<string, boolean>, baseline: Record<string, boolean>) {
  return Object.entries(granted)
    .filter(([name, value]) => value !== baseline[name])
    .map(([name, isGranted]) => ({ name, isGranted }))
}
