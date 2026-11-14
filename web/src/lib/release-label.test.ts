import { describe, expect, it } from 'vitest'
import { getReleaseDisplay } from './release-label'

describe('getReleaseDisplay', () => {
  it('开发未注入时显示 dev', () => {
    expect(getReleaseDisplay('')).toEqual({ text: 'dev', shortText: 'dev', title: 'local development' })
    expect(getReleaseDisplay(undefined)).toEqual({ text: 'dev', shortText: 'dev', title: 'local development' })
    expect(getReleaseDisplay('  ')).toEqual({ text: 'dev', shortText: 'dev', title: 'local development' })
  })

  it('生产 IMAGE_TAG 收成日期与短 SHA', () => {
    expect(getReleaseDisplay('20260907-015638-93c9cc4dd609')).toEqual({
      text: '20260907 · 93c9cc4d',
      shortText: '20260907',
      title: '20260907-015638-93c9cc4dd609',
    })
  })

  it('UAT IMAGE_TAG 收成短 SHA', () => {
    expect(getReleaseDisplay('git-93c9cc4dd609')).toEqual({
      text: '93c9cc4d',
      shortText: '93c9cc4d',
      title: 'git-93c9cc4dd609',
    })
  })

  it('无法解析的注入值原样展示', () => {
    expect(getReleaseDisplay('custom-build')).toEqual({
      text: 'custom-build',
      shortText: 'custom-build',
      title: 'custom-build',
    })
  })
})
