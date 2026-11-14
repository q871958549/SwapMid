// @vitest-environment node

import { Linter } from 'eslint'
import { describe, expect, it } from 'vitest'
import plugin, { findCustomFocusRings } from './focus-ring-token.mjs'

const ruleName = 'swapmid/focus-ring-token'

function lint(code) {
  const linter = new Linter({ configType: 'flat' })
  return linter.verify(
    code,
    {
      files: ['**/*.tsx'],
      languageOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module',
        parserOptions: { ecmaFeatures: { jsx: true } },
      },
      plugins: { swapmid: plugin },
      rules: { [ruleName]: 'error' },
    },
    { filename: 'src/app/(dashboard)/demo/client.tsx' },
  )
}

describe('swapmid/focus-ring-token', () => {
  it.each([
    ['外扩令牌', 'outline-none focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color)'],
    ['内收令牌', 'focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) ring-inset'],
    ['红色令牌', 'focus-visible:ring-(--focus-ring-color-destructive) aria-invalid:ring-(--focus-ring-color-destructive)'],
    ['伪元素上的令牌', 'focus-visible:after:ring-(length:--focus-ring-width) focus-visible:after:ring-inset'],
    ['去掉原生轮廓', 'focus-visible:outline-none focus:outline-hidden focus-visible:ring-0'],
    ['不在焦点变体下的环', 'ring-1 ring-border data-[state=checked]:ring-2 hover:ring-4 aria-invalid:ring-destructive/40'],
    ['焦点时只改边框', 'focus-visible:border-ring'],
  ])('放行：%s', (_name, classes) => {
    expect(lint(`const c = '${classes}'`)).toEqual([])
  })

  it.each([
    ['shadcn 旧写法', 'focus-visible:ring-ring/50 focus-visible:ring-[3px]', ['focus-visible:ring-ring/50', 'focus-visible:ring-[3px]']],
    ['2px 实色', 'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring', ['focus-visible:ring-2', 'focus-visible:ring-ring']],
    ['focus 加间隙', 'focus:ring-2 focus:ring-offset-2 ring-offset-background', ['focus:ring-2', 'focus:ring-offset-2']],
    ['原生轮廓', 'focus-visible:outline-2 focus-visible:outline-primary', ['focus-visible:outline-2', 'focus-visible:outline-primary']],
    ['伪元素', 'focus-visible:after:ring-[3px]', ['focus-visible:after:ring-[3px]']],
    ['group / peer / has', 'group-focus-visible:ring-2 peer-focus:outline has-[:focus-visible]:ring-ring', ['group-focus-visible:ring-2', 'peer-focus:outline', 'has-[:focus-visible]:ring-ring']],
    ['负偏移与 important', 'focus-visible:-outline-offset-2 focus-visible:ring-2!', ['focus-visible:-outline-offset-2', 'focus-visible:ring-2!']],
    ['红色变体自写', 'focus-visible:ring-destructive/20 dark:focus-visible:ring-destructive/40', ['focus-visible:ring-destructive/20', 'dark:focus-visible:ring-destructive/40']],
  ])('拦截：%s', (_name, classes, hits) => {
    expect(findCustomFocusRings(classes)).toEqual(hits)
    const messages = lint(`const c = '${classes}'`)
    expect(messages).toHaveLength(1)
    expect(messages[0].ruleId).toBe(ruleName)
  })

  it('cn 参数、模板字符串和 JSX 属性都检查', () => {
    const code = [
      "const a = cn('rounded', ok && 'focus-visible:ring-2')",
      'const b = `p-2 ${x} focus-visible:ring-ring`',
      'const c = <button className="focus:ring-1" />',
    ].join('\n')
    expect(lint(code)).toHaveLength(3)
  })
})
