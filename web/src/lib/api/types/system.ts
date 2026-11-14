import type { Require } from './_require'

export type AuditLogDto = Require<
  import('../generated').SwapMidSystemManagementAuditLogDto,
  'id' | 'executionTime' | 'executionDuration' | 'hasException'
>
export type AuditLogActionDto = import('../generated').SwapMidSystemManagementAuditLogActionDto
export type DataDictionaryItemDto = Require<
  import('../generated').SwapMidSystemManagementDataDictionaryItemDto,
  'id' | 'label' | 'value' | 'sortOrder' | 'isEnabled'
>
