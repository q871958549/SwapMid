export interface ExecutionFilterValues {
  jobKey?: string
  status?: string
  triggerKind?: string
  isDryRun?: string
  startedFrom?: string
  windowStartedAt?: string
  windowEndedAt?: string
  startedTo?: string
}

/** 把页面筛选映射为后端查询；统计卡的精确窗口优先于日期控件的自然日起点。 */
export function appendExecutionFilters(params: URLSearchParams, filters: ExecutionFilterValues) {
  if (filters.jobKey) params.set('JobKey', filters.jobKey)
  if (filters.status) params.set('Status', filters.status)
  if (filters.triggerKind) params.set('TriggerKind', filters.triggerKind)
  if (filters.isDryRun) params.set('IsDryRun', filters.isDryRun)
  if (filters.windowStartedAt && filters.windowEndedAt) {
    params.set('StartedFrom', filters.windowStartedAt)
    params.set('StartedTo', filters.windowEndedAt)
  } else {
    if (filters.startedFrom) params.set('StartedFrom', filters.startedFrom)
    if (filters.startedTo) params.set('StartedTo', `${filters.startedTo} 23:59:59.9999999`)
  }
  return params
}
