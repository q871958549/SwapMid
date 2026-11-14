import { describe, expect, it } from 'vitest'
import { isNumericColumn } from './numeric-column'

describe('isNumericColumn', () => {
  it('标题含数量、金额、百分比、天数时右对齐', () => {
    expect(isNumericColumn(undefined, '数量')).toBe(true)
    expect(isNumericColumn(undefined, '出库金额')).toBe(true)
    expect(isNumericColumn(undefined, '净额(万元)')).toBe(true)
    expect(isNumericColumn(undefined, '完成百分比')).toBe(true)
    expect(isNumericColumn(undefined, '逾期天数')).toBe(true)
  })

  it('以数、量、率结尾和计量类词结尾的标题右对齐，可带括号单位', () => {
    for (const title of ['行数', '件数', '面试人数', '迟到早退次数', '实排产量', '库存量(基本)', '毛利率', '税率（%）',
      '整票毛重（kg）', '净重', '体积', '银行存款余额', '货值', '价税合计', '小计', '报价成本', '计划工时', '加班总时长', '账面库存']) {
      expect(isNumericColumn(undefined, title), title).toBe(true)
    }
  })

  it('普通文本列不对齐到右侧', () => {
    expect(isNumericColumn(undefined, '名称')).toBe(false)
    expect(isNumericColumn(undefined, '城市')).toBe(false)
  })

  it('以数、量、率结尾但不是数字的词不右对齐', () => {
    for (const title of ['参数', '函数', '自动取数', '质量', '变量', '常量', '批量', '执行频率', '成本中心', '库存地点']) {
      expect(isNumericColumn(undefined, title), title).toBe(false)
    }
  })

  it('numberRange 筛选列视为数字列', () => {
    expect(isNumericColumn({ filterVariant: 'numberRange' }, '工单')).toBe(true)
  })

  it('显式 false 可以退出标题推断', () => {
    expect(isNumericColumn({ numeric: false }, '金额说明')).toBe(false)
  })

  it('显式 true 即使标题不像数字也右对齐', () => {
    expect(isNumericColumn({ numeric: true }, '备注')).toBe(true)
  })
})
