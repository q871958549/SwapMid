import type { Require } from './_require'

// ===== 个性化（Layout 域）=====
// 用户界面偏好（主题/字号/语言）：读搭车 application-configuration 或 GET /user-preference，写 POST /user-preference。
export type UserPreferenceDto = Require<
  import('../generated').SwapMidLayoutUserPreferenceDto,
  'theme' | 'fontSize' | 'language'
>
export type SaveUserPreferenceInput = import('../generated').SwapMidLayoutSaveUserPreferenceInput

// 列表视图偏好（全局行密度 + 单表列序/显隐/列宽/左固定列/每页条数/自定义档位/命名视图预设）：GET/PUT /api/v1/table-preference。
export interface TableSavedSortItemDto {
  id: string
  desc: boolean
}

export interface TableSavedColumnFilterDto {
  id: string
  value: string
}

export interface TableSavedPresetDto {
  id: string
  name: string
  order?: string[] | null
  visibility?: Record<string, boolean> | null
  sizing?: Record<string, number> | null
  pinned?: string[] | null
  search?: string | null
  sortItems?: TableSavedSortItemDto[] | null
  columnFilters?: TableSavedColumnFilterDto[] | null
  urlFilters?: Record<string, string> | null
}

export interface TableViewPreferenceDto {
  order?: string[] | null
  visibility?: Record<string, boolean> | null
  sizing?: Record<string, number> | null
  pinned?: string[] | null
  pageSize?: number | null
  customPageSizes?: number[] | null
  presets?: TableSavedPresetDto[] | null
}

export interface TablePreferenceSnapshotDto {
  density: string
  tables: Record<string, TableViewPreferenceDto>
}

export interface SaveTableViewPreferenceInput {
  tableKey: string
  view?: TableViewPreferenceDto | null
}

export interface SaveTableDensityInput {
  density: string
}
