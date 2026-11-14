/**
 * 服务端分页单次返回硬顶。必须与后端
 * LimitedResultRequestDto.MaxMaxResultCount 保持一致。
 *
 * 这是「请求合法」上限，不是交互列表的默认档位——下拉/导出分块仍可能用到它。
 */
export const SERVER_MAX_PAGE_SIZE = 5000

/**
 * 交互列表（DataTable 服务端分页）的默认单页上限。
 *
 * 自定义每页条数一旦写进下拉，用户就会拿它当常规操作；5000 行会把账号角色 N+1、
 * 宽 DTO、RSC 首屏一起放大成「切一下档位转很久」。需要更宽的列表显式传入
 * （样品/扫码 500），不要把硬顶当默认。
 */
export const INTERACTIVE_LIST_MAX_PAGE_SIZE = 200

const DEFAULT_PAGE_SIZE = 15
const MAX_SKIP_COUNT = 2_147_483_647

function positiveSafeInteger(value: string | number | undefined): number | null {
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null
}

/**
 * 把 URL 中 1-based 的 page/size 收敛为后端可表达的分页参数。
 *
 * 浏览器地址栏是外部输入：小数、负数、Infinity、超大安全整数都不能直接进入
 * ABP 的 SkipCount/MaxResultCount，否则会产生模型绑定 400、整数溢出或超量查询。
 */
export function parseServerListPagination(
  page: string | undefined,
  size: string | undefined,
  defaultPageSize = DEFAULT_PAGE_SIZE,
  /**
   * 单页上限（默认 `INTERACTIVE_LIST_MAX_PAGE_SIZE`，仍被 `SERVER_MAX_PAGE_SIZE` 硬顶）。
   * 需要比默认更宽的列表显式传入——如样品/扫码传 500，与后端 `ListMaxPageSize` 静默钳制对齐。
   *
   * 钳制刻意收在本函数内而不由调用方自己 `Math.min`：`skipCount` 与 `pageIndex` 的封顶都依赖最终
   * `pageSize`，调用方事后再钳一次就得自己重算这两个值，口径极易漂（漏算 = 深页 skipCount 与
   * 实际页大小不匹配、翻页错位）。
   */
  maxPageSize = INTERACTIVE_LIST_MAX_PAGE_SIZE,
): {
  pageIndex: number
  pageSize: number
  skipCount: number
} {
  const effectiveMax = Math.min(
    positiveSafeInteger(maxPageSize) ?? INTERACTIVE_LIST_MAX_PAGE_SIZE,
    SERVER_MAX_PAGE_SIZE,
  )
  const safeDefaultPageSize = Math.min(
    positiveSafeInteger(defaultPageSize) ?? DEFAULT_PAGE_SIZE,
    effectiveMax,
  )
  const pageSize = Math.min(
    positiveSafeInteger(size) ?? safeDefaultPageSize,
    effectiveMax,
  )
  const requestedPage = positiveSafeInteger(page) ?? 1
  const maxPageIndex = Math.floor(MAX_SKIP_COUNT / pageSize)
  const pageIndex = Math.min(requestedPage - 1, maxPageIndex)

  return {
    pageIndex,
    pageSize,
    skipCount: pageIndex * pageSize,
  }
}
