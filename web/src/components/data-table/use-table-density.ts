import { useCallback, useSyncExternalStore } from 'react'
import { useOptionalSessionIdentity } from '@/components/providers/session-identity-provider'
import {
  scheduleCloudTableDensitySync,
  TABLE_DENSITY_CHANGE_EVENT,
} from './table-preference-cloud'
import { readStoredDensity, saveDensity, type TableDensity } from './use-table-persistence'

// 同页多个表格、其他标签页都要跟着变：本页用自定义事件，跨标签页靠 storage 事件。
function subscribe(onChange: () => void) {
  window.addEventListener(TABLE_DENSITY_CHANGE_EVENT, onChange)
  window.addEventListener('storage', onChange)
  return () => {
    window.removeEventListener(TABLE_DENSITY_CHANGE_EVENT, onChange)
    window.removeEventListener('storage', onChange)
  }
}

/** 列表行密度（所有列表共用）。服务端与首帧按「标准」渲染，挂载后读本机设置并防抖同步云端。 */
export function useTableDensity(): [TableDensity, (next: TableDensity) => void] {
  const principalId = useOptionalSessionIdentity()?.storagePrincipalId ?? null
  const density = useSyncExternalStore(subscribe, readStoredDensity, () => 'standard' as const)
  const setDensity = useCallback((next: TableDensity) => {
    saveDensity(next)
    window.dispatchEvent(new Event(TABLE_DENSITY_CHANGE_EVENT))
    scheduleCloudTableDensitySync(principalId, next)
  }, [principalId])
  return [density, setDensity]
}
