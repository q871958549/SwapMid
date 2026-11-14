import { describe, it, expect } from 'vitest'
import { statusAwareFilter, pinyinAwareGlobalFilter, pinyinInitials } from './data-table-filters'

// FilterFn 仅依赖 row.getValue(columnId)；构造最小桩行驱动纯函数。
function rowWith(value: unknown) {
  return { getValue: () => value } as unknown as Parameters<typeof statusAwareFilter>[0]
}
const COL = 'col'
const noopMeta = () => {}

function statusFilter(value: unknown, filterValue: unknown): boolean {
  return statusAwareFilter(rowWith(value), COL, filterValue, noopMeta)
}
function globalFilter(value: unknown, filterValue: unknown): boolean {
  return pinyinAwareGlobalFilter(rowWith(value), COL, filterValue, noopMeta)
}

describe('pinyinInitials', () => {
  it('中文取首字母小写串', () => {
    expect(pinyinInitials('唐远')).toBe('ty')
    expect(pinyinInitials('北京')).toBe('bj')
    expect(pinyinInitials('张三')).toBe('zs')
  })

  it('非中文返回空串', () => {
    expect(pinyinInitials('Hello')).toBe('')
    expect(pinyinInitials('123')).toBe('')
    expect(pinyinInitials('')).toBe('')
  })

  it('重复调用结果稳定（命中缓存）', () => {
    expect(pinyinInitials('唐远')).toBe(pinyinInitials('唐远'))
  })
})

describe('statusAwareFilter · 下拉 {eq} 精确匹配', () => {
  it('字符串相等保留、不等剔除', () => {
    expect(statusFilter('active', { eq: 'active' })).toBe(true)
    expect(statusFilter('inactive', { eq: 'active' })).toBe(false)
  })

  it('数字 / 布尔按 String 归一比较', () => {
    expect(statusFilter(1, { eq: 1 })).toBe(true)
    expect(statusFilter(1, { eq: '1' })).toBe(true) // 类型不符也按 String 归一
    expect(statusFilter(true, { eq: true })).toBe(true)
    expect(statusFilter(false, { eq: true })).toBe(false)
  })
})

describe('statusAwareFilter · 文本模糊 + 拼音首字母', () => {
  it('原样包含匹配（大小写不敏感）', () => {
    expect(statusFilter('北京市', '北京')).toBe(true)
    expect(statusFilter('Beijing', 'bei')).toBe(true)
    expect(statusFilter('北京', 'xyz')).toBe(false)
  })

  it('中文值按拼音首字母命中', () => {
    expect(statusFilter('北京', 'bj')).toBe(true)
    expect(statusFilter('唐远', 'ty')).toBe(true)
    expect(statusFilter('唐远', 'zs')).toBe(false)
  })
})

describe('statusAwareFilter · 空筛选值放行', () => {
  it('undefined / null / 空串一律保留', () => {
    expect(statusFilter('x', undefined)).toBe(true)
    expect(statusFilter('x', null)).toBe(true)
    expect(statusFilter('x', '')).toBe(true)
  })
})

describe('pinyinAwareGlobalFilter', () => {
  it('包含或拼音首字母命中即保留', () => {
    expect(globalFilter('唐远', '唐')).toBe(true)
    expect(globalFilter('唐远', 'ty')).toBe(true)
    expect(globalFilter('abc', 'ab')).toBe(true)
  })

  it('空 query 放行所有非空值', () => {
    expect(globalFilter('abc', '')).toBe(true)
    expect(globalFilter('abc', '   ')).toBe(true) // trim 后为空
  })

  it('null / undefined 值直接剔除', () => {
    expect(globalFilter(null, 'x')).toBe(false)
    expect(globalFilter(undefined, 'x')).toBe(false)
  })

  it('无匹配剔除', () => {
    expect(globalFilter('abc', 'xyz')).toBe(false)
    expect(globalFilter('北京', 'sh')).toBe(false)
  })
})

describe('statusAwareFilter · 区间 {from,to,kind} 闭区间', () => {
  it('数字区间：闭区间内（含端点）保留、越界剔除', () => {
    expect(statusFilter(50, { from: '10', to: '100', kind: 'number' })).toBe(true)
    expect(statusFilter(10, { from: '10', to: '100', kind: 'number' })).toBe(true)
    expect(statusFilter(100, { from: '10', to: '100', kind: 'number' })).toBe(true)
    expect(statusFilter(5, { from: '10', to: '100', kind: 'number' })).toBe(false)
    expect(statusFilter(101, { from: '10', to: '100', kind: 'number' })).toBe(false)
  })

  it('数字区间：单边 from / to，无界放行', () => {
    expect(statusFilter(50, { from: '60', kind: 'number' })).toBe(false)
    expect(statusFilter(50, { to: '60', kind: 'number' })).toBe(true)
    expect(statusFilter(50, { kind: 'number' })).toBe(true)
  })

  it('数字区间：非数字值剔除', () => {
    expect(statusFilter('abc', { from: '10', kind: 'number' })).toBe(false)
  })

  it('日期区间：yyyy-MM-dd 闭区间（含端点）', () => {
    expect(statusFilter('2026-06-15', { from: '2026-06-01', to: '2026-06-30', kind: 'date' })).toBe(true)
    expect(statusFilter('2026-06-01', { from: '2026-06-01', to: '2026-06-30', kind: 'date' })).toBe(true)
    expect(statusFilter('2026-07-01', { from: '2026-06-01', to: '2026-06-30', kind: 'date' })).toBe(false)
    expect(statusFilter('2026-05-31', { from: '2026-06-01', kind: 'date' })).toBe(false)
  })

  it('日期区间：带时间的 ISO 取前 10 位比较（含当天 23:59）', () => {
    expect(statusFilter('2026-06-30T23:59:00Z', { from: '2026-06-01', to: '2026-06-30', kind: 'date' })).toBe(true)
  })
})

describe('statusAwareFilter · 多选 {in} 集合命中', () => {
  it('值 ∈ in 集合保留、∉ 剔除', () => {
    expect(statusFilter('active', { in: ['active', 'pending'] })).toBe(true)
    expect(statusFilter('pending', { in: ['active', 'pending'] })).toBe(true)
    expect(statusFilter('closed', { in: ['active', 'pending'] })).toBe(false)
  })

  it('数字 / 布尔按 String 归一命中', () => {
    expect(statusFilter(1, { in: [1, 2] })).toBe(true)
    expect(statusFilter(3, { in: [1, 2] })).toBe(false)
    expect(statusFilter(true, { in: [true] })).toBe(true)
  })

  it('空集 in:[] 视为未筛选、放行', () => {
    expect(statusFilter('anything', { in: [] })).toBe(true)
  })
})
