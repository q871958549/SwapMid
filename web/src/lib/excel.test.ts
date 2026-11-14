import { afterEach, describe, expect, it, vi } from 'vitest'
import ExcelJS from 'exceljs'
import { exportRowsToXlsx, exportSheetsToXlsx, readFirstSheetAsObjects } from './excel'

const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

afterEach(() => {
  vi.restoreAllMocks()
})

describe('Excel 客户端工具', () => {
  it('可写出工作簿并按首行表头读回数据', async () => {
    const workbook = new ExcelJS.Workbook()
    const sheet = workbook.addWorksheet('候选人')
    sheet.addRow(['姓名', '评分', '日期'])
    sheet.addRow(['张三', 95, new Date(2026, 6, 21)])

    const buffer = await workbook.xlsx.writeBuffer()
    const file = new File([buffer], 'candidate.xlsx', { type: XLSX_MIME })

    await expect(readFirstSheetAsObjects(file)).resolves.toEqual([
      { 姓名: '张三', 评分: '95', 日期: '2026-07-21' },
    ])
  })

  it('对象数组导出能完成下载编排', async () => {
    const objectUrl = 'blob:excel-test'
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue(objectUrl)
    const revokeObjectUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    await exportRowsToXlsx('候选人清单', [{ 姓名: '张三', 状态: '待面试' }], { sheetName: '候选人' })

    expect(createObjectUrl).toHaveBeenCalledOnce()
    expect(click).toHaveBeenCalledOnce()
    expect(revokeObjectUrl).toHaveBeenCalledWith(objectUrl)
  })

  it('多 sheet 导出能完成下载编排', async () => {
    const objectUrl = 'blob:excel-sheets'
    vi.spyOn(URL, 'createObjectURL').mockReturnValue(objectUrl)
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    await exportSheetsToXlsx('员工身份核验报表', [
      { name: '已录入', rows: [{ 工号: 'H1' }] },
      { name: '未录入', rows: [] },
    ])

    expect(click).toHaveBeenCalledOnce()
  })
})
