import assert from 'node:assert/strict'
import test from 'node:test'
import {
  extractIcuArgumentNames,
  findIcuArgumentDrift,
  flattenMessages,
} from './i18n-message-contracts.mjs'

test('提取普通、plural/select、嵌套与转义后的 ICU 参数', () => {
  const message = [
    "Hello {name}; '{ignored}'",
    '{count, plural, offset:1 =0 {None} one {{name} has one} other {{count} items}}',
    '{gender, select, male {He} female {She} other {{fallbackName}}}',
    "You''ve paid {amount, number, ::currency/USD}",
  ].join(' ')

  assert.deepEqual(
    [...extractIcuArgumentNames(message)].sort(),
    ['amount', 'count', 'fallbackName', 'gender', 'name'],
  )
})

test('plural/select 的单词分支正文不会被误判为参数', () => {
  assert.deepEqual(
    [...extractIcuArgumentNames('{count, plural, =0 {None} other {Available}}')],
    ['count'],
  )
})

test('逐键报告三语 ICU 参数集合漂移', () => {
  const localeMessages = new Map([
    ['zh-Hans', flattenMessages({ Dialog: { title: '处理「{name}」的 {count} 项' } })],
    ['zh-Hant', flattenMessages({ Dialog: { title: '處理「{name}」的 {count} 項' } })],
    ['en', flattenMessages({ Dialog: { title: 'Process {count} items' } })],
  ])

  assert.deepEqual(findIcuArgumentDrift(localeMessages, 'zh-Hans'), [
    {
      key: 'Dialog.title',
      locale: 'en',
      expected: ['count', 'name'],
      actual: ['count'],
    },
  ])
})

test('消息目录以显式预算拒绝深层、海量与扁平点号键', () => {
  assert.throws(
    () => flattenMessages({ 'flat.key': '禁止' }),
    /键段不合法/,
  )
  assert.throws(
    () => flattenMessages({ a: { b: { c: 'too deep' } } }, '', new Map(), {
      depth: 2,
      nodes: 10,
      leaves: 10,
      keyCharacters: 100,
      valueCharacters: 100,
      totalValueCharacters: 100,
    }),
    /深度超过上限 2/,
  )
  assert.throws(
    () => flattenMessages({ a: '1', b: '2' }, '', new Map(), {
      depth: 2,
      nodes: 10,
      leaves: 1,
      keyCharacters: 100,
      valueCharacters: 100,
      totalValueCharacters: 100,
    }),
    /叶子数超过上限 1/,
  )
})
