'use client'

import { createContext, useContext } from 'react'

// 把服务端 session 的 grantedPolicies 注入到客户端组件树，
// 让深层 Client Component（如 DataTable）也能按权限控制 UI（导出/导入等）。
const PermissionsContext = createContext<Record<string, boolean>>({})

export function PermissionsProvider({
  value,
  children,
}: {
  value: Record<string, boolean>
  children: React.ReactNode
}) {
  return <PermissionsContext.Provider value={value}>{children}</PermissionsContext.Provider>
}

export function useHasPermission(permission?: string): boolean {
  const policies = useContext(PermissionsContext)
  if (!permission) return true
  return policies[permission] === true
}
