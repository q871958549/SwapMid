'use client'

import { useEffect } from 'react'
import { useOptionalSessionIdentity } from '@/components/providers/session-identity-provider'
import { ensureCloudTablePreferencesLoaded } from '@/components/data-table/table-preference-cloud'

/**
 * 已认证壳层挂载后按当前账号单飞拉取一次列表视图偏好快照，回填本机缓存并驱动当前打开的 DataTable 热更新。
 */
export function TablePreferenceCloudSync() {
  const principalId = useOptionalSessionIdentity()?.storagePrincipalId ?? null
  useEffect(() => {
    void ensureCloudTablePreferencesLoaded(principalId)
  }, [principalId])
  return null
}
