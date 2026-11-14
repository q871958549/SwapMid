import { describe, it, expect } from 'vitest'
import { formatDateTime, parsePermissionChangeLine } from './role-history'

describe('parsePermissionChangeLine', () => {
  it('同时含新增与移除两段', () => {
    const result = parsePermissionChangeLine('ops +[Perm.A,Perm.B] -[Perm.C]')
    expect(result.added).toEqual(['Perm.A', 'Perm.B'])
    expect(result.removed).toEqual(['Perm.C'])
  })

  it('仅含新增段（无移除）', () => {
    const result = parsePermissionChangeLine('ops +[Perm.A]')
    expect(result.added).toEqual(['Perm.A'])
    expect(result.removed).toEqual([])
  })

  it('仅含移除段（无新增）', () => {
    const result = parsePermissionChangeLine('ops -[Perm.A,Perm.B,Perm.C]')
    expect(result.added).toEqual([])
    expect(result.removed).toEqual(['Perm.A', 'Perm.B', 'Perm.C'])
  })

  it('roleName 段未被剥离时同样健壮（正则按括号锚点提取，不依赖行首内容）', () => {
    const result = parsePermissionChangeLine('SwapMid.SomeRole +[Perm.A] -[Perm.B]')
    expect(result.added).toEqual(['Perm.A'])
    expect(result.removed).toEqual(['Perm.B'])
  })

  it('无匹配（空行/无变化摘要）返回两个空数组，不抛异常', () => {
    expect(parsePermissionChangeLine('')).toEqual({ added: [], removed: [] })
    expect(parsePermissionChangeLine('ops')).toEqual({ added: [], removed: [] })
  })
})

describe('formatDateTime', () => {
  it('格式化为 yyyy-MM-dd HH:mm:ss', () => {
    expect(formatDateTime('2026-07-20T08:05:09Z')).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/)
  })

  it('空值 / 无法解析的字符串走兜底分支', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime(undefined)).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('not-a-date')
  })
})
