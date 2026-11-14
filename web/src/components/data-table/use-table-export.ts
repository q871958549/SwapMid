import { type Table } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { STRUCTURAL_COL_IDS } from './use-table-persistence'
import { resolveColumnTitle } from './data-table-columns'
import { exportRowsToXlsx, readFirstSheetAsObjects } from '@/lib/excel'

// 后端日期时间多为 UTC ISO 串（…T…Z / ±偏移），导出时转成浏览器本地（北京）时间；
// 纯日期串（yyyy-MM-dd）与普通文本不受影响。
const ISO_DATETIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?$/

function formatLocalDateTime(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

function toExportCell(value: unknown): string {
  if (value === null || value === undefined) return ''
  if (value instanceof Date) return formatLocalDateTime(value)
  if (typeof value === 'string' && ISO_DATETIME.test(value)) {
    const d = new Date(value)
    if (!Number.isNaN(d.getTime())) return formatLocalDateTime(d)
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

interface UseTableExportOptions<TData> {
  table: Table<TData>
  exportFileName: string
  /** 服务端分页时拉「全部匹配行」（按需分页父组件提供）；不传则导出仅含当前已过滤行。 */
  onExportAll?: () => Promise<TData[]>
  /** 服务端流式导出：后端直接生成文件。传了它就短路掉下面整套「拉行 → 建对象 → 生成 xlsx」。 */
  onServerExport?: () => Promise<void>
  onImportRows?: (rows: Record<string, string>[], file: File) => void | Promise<void>
}

// 表格导出（Excel）/导入能力：导出取当前可见数据列、按 accessor 求值并把 ISO 日期转本地时间；
// 服务端分页经 onExportAll 拉全量。导入读首个 sheet 为对象数组交给 onImportRows。
export function useTableExport<TData>({
  table,
  exportFileName,
  onExportAll,
  onServerExport,
  onImportRows,
}: UseTableExportOptions<TData>) {
  const t = useTranslations('Common')

  async function handleExport() {
    // 服务端流式导出优先：行根本不进浏览器，故列可见性/列顺序这些前端概念在这条路上不适用
    // （服务端固定全列，见 useCrudResource 的 serverExportPath 注释）。
    if (onServerExport) {
      await onServerExport()
      return
    }

    const exportColumns = table
      .getAllLeafColumns()
      .filter((col) => col.getIsVisible() && !STRUCTURAL_COL_IDS.has(col.id))

    // 服务端分页：onExportAll 拉「全部匹配行」原始对象，按列 accessor 取值导出；
    // 否则导出当前（已过滤的）行模型。
    const rawAll = onExportAll ? await onExportAll() : null
    const rows = rawAll
      ? rawAll.map((raw, i) =>
          Object.fromEntries(exportColumns.map((col) => [
            resolveColumnTitle(col, table, t),
            toExportCell(col.accessorFn ? col.accessorFn(raw, i) : (raw as Record<string, unknown>)[col.id]),
          ]))
        )
      : table.getFilteredRowModel().rows.map((row) =>
          Object.fromEntries(exportColumns.map((col) => [resolveColumnTitle(col, table, t), toExportCell(row.getValue(col.id))]))
        )
    const filename = exportFileName.replace(/\.(csv|xlsx|xls)$/i, '') || 'table-export'
    await exportRowsToXlsx(filename, rows)
  }

  // 导入按钮只在 onImportRows 传入时才渲染（见 DataTable 的 showImport），故此处回调必然存在。
  // 曾经这里有个 `window.alert('读取到 N 行')` 兜底分支，配合 enableImport 默认 true，造成
  // 82 个页面挂着「点了只弹一句话就没下文」的死按钮（2026-07-22 清理）。勿再加回兜底——
  // 没有实现就不该有按钮。
  async function handleImportFile(file: File) {
    if (!onImportRows) return
    const rows = await readFirstSheetAsObjects(file)
    await onImportRows(rows, file)
  }

  return { handleExport, handleImportFile }
}
