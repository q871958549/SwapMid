'use client'

import { createContext, useContext, useEffect, useMemo } from 'react'
import {
  createAuthenticatedBrowserStorage,
  type AuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'

export interface SessionIdentityBinding {
  identityVersion: string
  storagePrincipalId: string
}

interface SessionIdentityContextValue {
  binding: SessionIdentityBinding
  browserStorage: AuthenticatedBrowserStorage
}

const SessionIdentityContext = createContext<SessionIdentityContextValue | null>(null)

export function SessionIdentityProvider({
  identityVersion,
  storagePrincipalId,
  children,
}: {
  identityVersion: string
  storagePrincipalId: string
  children: React.ReactNode
}) {
  const binding = useMemo(
    () => ({ identityVersion, storagePrincipalId }),
    [identityVersion, storagePrincipalId],
  )
  const browserStorage = useMemo(
    () => createAuthenticatedBrowserStorage(binding),
    [binding],
  )
  const value = useMemo(
    () => ({ binding, browserStorage }),
    [binding, browserStorage],
  )

  // 负责一次性清理旧无作用域业务键，并持续拦截滚动发布期间旧标签页写回的遗留 localStorage。
  // read/write 自身也会先执行幂等 initialize，因此不依赖父子 effect 的具体触发顺序。
  useEffect(() => browserStorage.installBoundary(), [browserStorage])

  return (
    <SessionIdentityContext.Provider value={value}>
      {children}
    </SessionIdentityContext.Provider>
  )
}

/** Provider 之外返回 null，适合本身也会出现在匿名页面的共享组件。 */
export function useOptionalSessionIdentity(): SessionIdentityBinding | null {
  return useContext(SessionIdentityContext)?.binding ?? null
}

/** 当前文档首屏绑定的完整身份与浏览器存储命名空间。 */
export function useSessionIdentity(): SessionIdentityBinding {
  const value = useOptionalSessionIdentity()
  if (!value) {
    throw new Error('SessionIdentityProvider is required')
  }
  return value
}

/** 当前文档首屏绑定的身份世代；用于写请求前置条件与显式身份切换 CAS。 */
export function useSessionIdentityVersion(): string {
  return useSessionIdentity().identityVersion
}

/** 当前文档首屏身份闭包绑定的已认证浏览器存储。 */
export function useAuthenticatedBrowserStorage(): AuthenticatedBrowserStorage {
  const value = useContext(SessionIdentityContext)
  if (!value) {
    throw new Error('SessionIdentityProvider is required')
  }
  return value.browserStorage
}

/** 匿名/已认证共用组件使用；匿名页面返回 null。 */
export function useOptionalAuthenticatedBrowserStorage(): AuthenticatedBrowserStorage | null {
  return useContext(SessionIdentityContext)?.browserStorage ?? null
}
