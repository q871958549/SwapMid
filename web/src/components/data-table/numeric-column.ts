import { type ColumnMeta, type RowData } from '@tanstack/react-table'
import './data-table-filters'

/**
 * 数量、金额、百分比、天数等列右对齐。
 * 显式 `meta.numeric === false` 可退出（标题碰巧带「金额」但内容不是数字时）。
 * 首选列定义自己声明：`column-factories` 的 `numberColumn` / `moneyColumn` / `percentColumn` 设 `meta.numeric: true`。
 * `filterVariant: 'numberRange'` 与标题关键词只作兜底；`scripts/ui-health.mjs` 读下面的 `NUMERIC_TITLE*` 正则
 * 统计只靠推断的列（`inferredNumericColumns`），改正则的写法要同步那边。
 */
/** 数量、金额、百分比、天数、比率、占比、单价、总价、税额、净额、万元。用码点写，避免裸中文扫描把关键词当成界面文案。 */
const NUMERIC_TITLE = /(\u6570\u91cf|\u91d1\u989d|\u767e\u5206\u6bd4|\u5929\u6570|\u6bd4\u7387|\u5360\u6bd4|\u5355\u4ef7|\u603b\u4ef7|\u7a0e\u989d|\u51c0\u989d|\u4e07\u5143)/
/**
 * 按结尾判定，可带括号单位（「毛重（kg）」）：
 * - 以「数」结尾（行数、件数、人数、次数），除参数、函数、取数；
 * - 以「量」结尾（产量、库存量、重量），除质量、变量、常量、批量；
 * - 以「率」结尾（完成率、毛利率、税率），除频率——执行频率常写成文字；
 * - 毛重、净重、体积、余额、货值、总额、合计、小计、价格、成本、工时、时长、库存。
 * 此前只认上面那组词，「行数」「件数」「工时」左对齐而「金额」右对齐，同一张表里数字列各朝一边。
 */
const NUMERIC_TITLE_END = /((?<!\u53c2|\u51fd|\u53d6)\u6570|(?<!\u8d28|\u53d8|\u5e38|\u6279)\u91cf|(?<!\u9891)\u7387|\u6bdb\u91cd|\u51c0\u91cd|\u4f53\u79ef|\u4f59\u989d|\u8d27\u503c|\u603b\u989d|\u5408\u8ba1|\u5c0f\u8ba1|\u4ef7\u683c|\u6210\u672c|\u5de5\u65f6|\u65f6\u957f|\u5e93\u5b58)(?:[\uff08(][^)\uff09]*[)\uff09])?$/

type NumericMeta = Pick<ColumnMeta<RowData, unknown>, 'numeric' | 'filterVariant'>

export function isNumericColumn(
  meta: NumericMeta | undefined,
  title?: string | null,
): boolean {
  if (meta?.numeric === false) return false
  if (meta?.numeric === true) return true
  if (meta?.filterVariant === 'numberRange') return true
  const text = title?.trim()
  return !!text && (NUMERIC_TITLE.test(text) || NUMERIC_TITLE_END.test(text))
}

/** 日期、时间、月份、年份、截止、到期。同样用码点写。 */
const DATE_TITLE = /(\u65e5\u671f|\u65f6\u95f4|\u6708\u4efd|\u5e74\u4efd|\u622a\u6b62|\u5230\u671f)/

/**
 * 单击排序先降序的列：数字与日期——看最大、最新的最常见。其余列先升序。
 * 列定义上写了 `sortDescFirst` 时以它为准。
 */
export function isDescFirstColumn(
  meta: NumericMeta | undefined,
  title?: string | null,
  sortDescFirst?: boolean,
): boolean {
  if (sortDescFirst !== undefined) return sortDescFirst
  if (meta?.filterVariant === 'dateRange') return true
  if (isNumericColumn(meta, title)) return true
  const text = title?.trim()
  return !!text && DATE_TITLE.test(text)
}

export type SortState = false | 'asc' | 'desc'

/** 单击循环：默认 → 先手方向 → 反方向 → 默认。 */
export function nextSortState(current: SortState, descFirst: boolean): SortState {
  const first = descFirst ? 'desc' : 'asc'
  if (!current) return first
  if (current === first) return first === 'asc' ? 'desc' : 'asc'
  return false
}
