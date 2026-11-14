'use client'

import { useSyncExternalStore } from 'react'
import { isInFlight, subscribeInFlight } from '@/lib/inflight-action'

/** 订阅模块级 in-flight。卸载重挂后仍能看到同一 key 的进行中状态。 */
export function useInFlight(key: string): boolean {
  return useSyncExternalStore(
    subscribeInFlight,
    () => isInFlight(key),
    () => false,
  )
}
