import { type FilterFn, type RowData } from '@tanstack/react-table'
import { pinyin } from 'pinyin-pro'

// 列可声明下拉筛选：在列 def 上加 meta.filterVariant='select' + filterOptions（值↔中文）。
// 状态/枚举/布尔列务必用 'select'，避免「在文本框里打状态名却筛不出来」。
declare module '@tanstack/react-table' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    title?: string
    audit?: boolean
    /** 列默认隐藏（非审计列场景，如低价值布尔列）——语义独立于 `audit`，二者可分别命中，参见 data-table.tsx 的 defaultVisibility 推导。 */
    defaultHidden?: boolean
    filterVariant?: 'text' | 'select' | 'multiSelect' | 'numberRange' | 'dateRange'
    /**
     * 数字列（数量、金额、百分比、天数）右对齐并使用 tabular-nums。
     * 不写时按标题关键词或 numberRange 推断；标题碰巧含这些词但不是数字时显式 false。
     */
    numeric?: boolean
    filterOptions?: { value: string | number | boolean; label: string }[]
    /**
     * 横滑时钉在左侧。只给宽表的身份列（及它左边必须一起留下的窄列，如启用开关）。
     * 钉住的列不参与拖拽重排，顺序固定在选择列之后。
     */
    pin?: 'left'
  }
}

// 全局搜索：原样包含匹配之外，中文值再按拼音首字母匹配（如输入 "ty" 命中 "田烨"）。
// 首字母串按原文缓存（列表数据高度重复，键级缓存即可把 pinyin 开销摊到一次）。
const _pinyinInitialCache = new Map<string, string>()
export function pinyinInitials(text: string): string {
  let initials = _pinyinInitialCache.get(text)
  if (initials === undefined) {
    // i18n 例外登记（批次4k扫描仪表已排查，勿删）：/[一-龥]/ 是 Unicode 码点区间检测正则（判断字符串
    // 是否含中文字符,决定要不要算拼音首字母),逻辑用途裸中文匹配串,不是待翻译 UI 文案。
    initials = /[一-龥]/.test(text)
      ? (pinyin(text, { pattern: 'first', toneType: 'none', type: 'array' }) as string[]).join('').toLowerCase()
      : ''
    _pinyinInitialCache.set(text, initials)
  }
  return initials
}

// 区间筛选值：数字列 / 日期列的 [from, to] 闭区间（kind 区分比较方式，由筛选行 UI 写入）。
export interface RangeFilterValue {
  from?: string
  to?: string
  kind: 'number' | 'date'
}

function isRangeFilter(fv: unknown): fv is RangeFilterValue {
  return typeof fv === 'object' && fv !== null && 'kind' in fv
}

// 闭区间匹配：数字按数值比；日期取 v 的 yyyy-MM-dd 前缀按字典序比（等价日期序，兼容带时间的 ISO）。
function matchRange(value: unknown, range: RangeFilterValue): boolean {
  if (range.kind === 'number') {
    const nv = Number(value)
    if (Number.isNaN(nv)) return false
    if (range.from != null && range.from !== '' && nv < Number(range.from)) return false
    if (range.to != null && range.to !== '' && nv > Number(range.to)) return false
    return true
  }
  const sv = String(value ?? '').slice(0, 10)
  if (!sv) return false
  if (range.from && sv < range.from) return false
  if (range.to && sv > range.to) return false
  return true
}

// 多选枚举筛选值：值 ∈ in 集合即保留（in 空 = 未筛选）。由筛选行多选 UI 写入。
export interface MultiSelectFilterValue {
  in: (string | number | boolean)[]
}

function isMultiSelectFilter(fv: unknown): fv is MultiSelectFilterValue {
  return typeof fv === 'object' && fv !== null && 'in' in fv && Array.isArray((fv as { in: unknown }).in)
}

// 列筛选函数：区间用 {from,to,kind} 闭区间；多选用 {in:[...]} 集合命中；下拉用 {eq:value} 精确匹配（String 归一）；
// 文本则模糊包含，中文值额外支持拼音首字母（与 lib/nav-search 共用 pinyinInitials）。
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const statusAwareFilter: FilterFn<any> = (row, columnId, filterValue) => {
  if (filterValue === undefined || filterValue === null || filterValue === '') return true
  const v = row.getValue(columnId)
  if (isRangeFilter(filterValue)) {
    return matchRange(v, filterValue)
  }
  if (isMultiSelectFilter(filterValue)) {
    if (filterValue.in.length === 0) return true
    return filterValue.in.map(String).includes(String(v ?? ''))
  }
  if (typeof filterValue === 'object' && filterValue !== null && 'eq' in (filterValue as Record<string, unknown>)) {
    return String(v ?? '') === String((filterValue as { eq: unknown }).eq)
  }
  const text = String(v ?? '').toLowerCase()
  const query = String(filterValue).trim().toLowerCase()
  if (text.includes(query)) return true
  return /^[a-z0-9]+$/.test(query) && pinyinInitials(String(v ?? '')).includes(query)
}

// 全局搜索过滤：行任一列原样包含或拼音首字母命中即保留。
export const pinyinAwareGlobalFilter: FilterFn<unknown> = (row, columnId, filterValue) => {
  const raw = row.getValue(columnId)
  if (raw === null || raw === undefined) return false
  const query = String(filterValue ?? '').trim().toLowerCase()
  if (!query) return true
  const text = String(raw).toLowerCase()
  if (text.includes(query)) return true
  return /^[a-z0-9]+$/.test(query) && pinyinInitials(String(raw)).includes(query)
}
