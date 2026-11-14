'use client'

import { useEffect, useState } from 'react'
import { parsePasswordPolicy, type PasswordPolicy } from '@/lib/password-policy'

/** 已登录页经 session 代理拉当前密码规则；失败返回 null，由 UI 显示中性提示。 */
export function usePasswordPolicy(enabled: boolean): PasswordPolicy | null {
  const [policy, setPolicy] = useState<PasswordPolicy | null>(null)

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    void fetch('/api/proxy/account-management/password-policy', { cache: 'no-store' })
      .then(async (res) => (res.ok ? parsePasswordPolicy(await res.json()) : null))
      .then((next) => {
        if (!cancelled) setPolicy(next)
      })
      .catch(() => {
        if (!cancelled) setPolicy(null)
      })
    return () => {
      cancelled = true
    }
  }, [enabled])

  return policy
}
