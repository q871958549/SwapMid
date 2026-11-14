import { renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import {
  SessionIdentityProvider,
  useAuthenticatedBrowserStorage,
  useOptionalAuthenticatedBrowserStorage,
  useOptionalSessionIdentity,
  useSessionIdentity,
  useSessionIdentityVersion,
} from './session-identity-provider'
import { authenticatedBrowserStoragePolicies } from '@/lib/browser-storage-policy'

function wrapper({ children }: { children: React.ReactNode }) {
  return (
    <SessionIdentityProvider
      identityVersion="identity-v1"
      storagePrincipalId="sp1-principal"
    >
      {children}
    </SessionIdentityProvider>
  )
}

describe('SessionIdentityProvider', () => {
  it('向消费者提供完整绑定，并保持版本兼容 hook', () => {
    const binding = renderHook(() => useSessionIdentity(), { wrapper })
    const version = renderHook(() => useSessionIdentityVersion(), { wrapper })

    expect(binding.result.current).toEqual({
      identityVersion: 'identity-v1',
      storagePrincipalId: 'sp1-principal',
    })
    expect(version.result.current).toBe('identity-v1')
  })

  it('提供绑定首屏身份的稳定存储对象', () => {
    const { result, rerender } = renderHook(
      () => useAuthenticatedBrowserStorage(),
      { wrapper },
    )
    const first = result.current

    rerender()

    expect(result.current).toBe(first)
    expect(result.current.key(authenticatedBrowserStoragePolicies.tabs))
      .toContain(':identity:identity-v1:tabs')
    expect(result.current.key(authenticatedBrowserStoragePolicies.recentVisits))
      .toContain(':principal:sp1-principal:recent-visits')
  })

  it('optional hook 在 Provider 外返回 null', () => {
    const identity = renderHook(() => useOptionalSessionIdentity())
    const storage = renderHook(() => useOptionalAuthenticatedBrowserStorage())

    expect(identity.result.current).toBeNull()
    expect(storage.result.current).toBeNull()
  })

  it('required hook 在 Provider 外快速失败', () => {
    expect(() => renderHook(() => useSessionIdentity()))
      .toThrow('SessionIdentityProvider is required')
    expect(() => renderHook(() => useAuthenticatedBrowserStorage()))
      .toThrow('SessionIdentityProvider is required')
  })
})
