import type { Require } from './_require'

// ===== 个性化（Layout 域）=====
// 用户界面偏好（主题/字号/语言）：读搭车 application-configuration 或 GET /user-preference，写 POST /user-preference。
export type UserPreferenceDto = Require<
  import('../generated').SwapMidLayoutUserPreferenceDto,
  'theme' | 'fontSize' | 'language'
>
export type SaveUserPreferenceInput = import('../generated').SwapMidLayoutSaveUserPreferenceInput

// 列表视图偏好（全局行密度 + 单表列序/显隐/列宽/左固定列/每页条数/自定义档位/命名视图预设）：GET/PUT /api/v1/table-preference。
export type TableSavedSortItemDto = Require<import('../generated').SwapMidLayoutTableSavedSortItemDto, 'id'>
export type TableSavedColumnFilterDto = Require<import('../generated').SwapMidLayoutTableSavedColumnFilterDto, 'id' | 'value'>
export type TableSavedPresetDto = Omit<
  Require<import('../generated').SwapMidLayoutTableSavedPresetDto, 'id' | 'name'>,
  'sortItems' | 'columnFilters'
> & {
  sortItems?: TableSavedSortItemDto[] | null
  columnFilters?: TableSavedColumnFilterDto[] | null
}
export type TableViewPreferenceDto = Omit<import('../generated').SwapMidLayoutTableViewPreferenceDto, 'presets'> & {
  presets?: TableSavedPresetDto[] | null
}
export type TablePreferenceSnapshotDto = {
  density: string
  tables: Record<string, TableViewPreferenceDto>
}
export type SaveTableViewPreferenceInput = {
  tableKey: string
  view?: TableViewPreferenceDto | null
}
export type SaveTableDensityInput = import('../generated').SwapMidLayoutSaveTableDensityInput
