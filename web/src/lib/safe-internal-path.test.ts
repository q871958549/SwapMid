import { describe, expect, it } from 'vitest'
import { isSafeInternalPath } from './safe-internal-path'

describe('isSafeInternalPath', () => {
  it.each([
    '/dashboard',
    '/project-management/samples?tab=待审批&from=/dashboard',
    '/docs/api-reference#section',
  ])('允许站内绝对路径：%s', (value) => {
    expect(isSafeInternalPath(value)).toBe(true)
  })

  it.each([
    undefined,
    null,
    '',
    'dashboard',
    'https://evil.example/steal',
    '//evil.example/steal',
    '///evil.example/steal',
    '/\\evil.example/steal',
    '/safe\n//evil.example',
    '/safe\u007fpath',
  ])('拒绝非站内路径或解析歧义输入：%s', (value) => {
    expect(isSafeInternalPath(value)).toBe(false)
  })
})
