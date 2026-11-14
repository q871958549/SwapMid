// 统一的 Excel 客户端导出 / 读取（exceljs，动态 import 不进路由主 bundle）。
// 历史：曾用 xlsx@0.18.5（原型污染 / ReDoS 漏洞且已停维护），2026-07-12 全站收敛到 exceljs
// （evolution-roadmap P2-7）。新增客户端导出一律用本模块，勿再引入 xlsx；
// 带样式的重版式导出通过 loadExcelJS() 取得运行时，
import type ExcelJS from 'exceljs'
import { loadWithChunkRetry } from './load-with-chunk-retry'

const importExcelJS = () => import('exceljs')

export async function loadExcelJS() {
  return (await loadWithChunkRetry(importExcelJS)).default
}

const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

function downloadXlsx(filenameBase: string, buf: ArrayBuffer) {
  const blob = new Blob([buf], { type: XLSX_MIME })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${filenameBase}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** 对象数组 → 单 sheet 素表下载。键 = 表头（取首行对象键序），值原样写入（数字保持数字可求和）。 */
export async function exportRowsToXlsx(
  filenameBase: string,
  rows: Record<string, unknown>[],
  opts?: { sheetName?: string },
) {
  const ExcelJSRuntime = await loadExcelJS()
  const wb = new ExcelJSRuntime.Workbook()
  const ws = wb.addWorksheet(opts?.sheetName ?? 'Sheet1')
  const headers = rows[0] ? Object.keys(rows[0]) : []
  if (headers.length) {
    ws.addRow(headers)
    for (const r of rows) ws.addRow(headers.map((h) => r[h] ?? ''))
  }
  downloadXlsx(filenameBase, (await wb.xlsx.writeBuffer()) as ArrayBuffer)
}

/** 多 sheet 素表下载。空 sheet 仍会创建，便于对照报表固定版式。 */
export async function exportSheetsToXlsx(
  filenameBase: string,
  sheets: Array<{ name: string; rows: Record<string, unknown>[] }>,
) {
  const ExcelJSRuntime = await loadExcelJS()
  const wb = new ExcelJSRuntime.Workbook()
  for (const sheet of sheets) {
    const ws = wb.addWorksheet(sheet.name)
    const headers = sheet.rows[0] ? Object.keys(sheet.rows[0]) : []
    if (headers.length) {
      ws.addRow(headers)
      for (const r of sheet.rows) ws.addRow(headers.map((h) => r[h] ?? ''))
    }
  }
  downloadXlsx(filenameBase, (await wb.xlsx.writeBuffer()) as ArrayBuffer)
}

// exceljs 的 cell.value 是多态联合（富文本 / 超链接 / 公式…），归一成纯文本。
function cellText(value: ExcelJS.CellValue): string {
  if (value === null || value === undefined) return ''
  if (value instanceof Date) {
    const p = (n: number) => String(n).padStart(2, '0')
    return `${value.getFullYear()}-${p(value.getMonth() + 1)}-${p(value.getDate())}`
  }
  if (typeof value === 'object') {
    if ('richText' in value) return value.richText.map((t) => t.text).join('')
    if ('text' in value) return String(value.text)
    if ('result' in value) return value.result === null || value.result === undefined ? '' : cellText(value.result as ExcelJS.CellValue)
    if ('error' in value) return String(value.error)
    return JSON.stringify(value)
  }
  return String(value)
}

/** 读上传 xlsx 的首个 sheet：首行为表头，其余行 →（表头 → 文本）对象数组；空单元格为 ''，全空行跳过。 */
export async function readFirstSheetAsObjects(file: File): Promise<Record<string, string>[]> {
  const ExcelJSRuntime = await loadExcelJS()
  const wb = new ExcelJSRuntime.Workbook()
  await wb.xlsx.load(await file.arrayBuffer())
  const ws = wb.worksheets[0]
  if (!ws) return []

  const headers: string[] = []
  ws.getRow(1).eachCell({ includeEmpty: false }, (cell, col) => {
    headers[col - 1] = cellText(cell.value)
  })
  if (!headers.some(Boolean)) return []

  const rows: Record<string, string>[] = []
  ws.eachRow({ includeEmpty: false }, (row, rowNumber) => {
    if (rowNumber === 1) return
    const obj: Record<string, string> = {}
    let hasValue = false
    headers.forEach((h, i) => {
      if (!h) return
      const text = cellText(row.getCell(i + 1).value)
      obj[h] = text
      if (text !== '') hasValue = true
    })
    if (hasValue) rows.push(obj)
  })
  return rows
}
