import assert from 'node:assert/strict'
import { test } from 'node:test'
import { stableOpenapiOrder } from './openapi-stable-order.mjs'

test('对象只换顺序时快照逐字不变，且不修改输入', () => {
  const previous = { paths: { '/b': { get: { summary: '乙', responses: {} } }, '/a': {} } }
  const current = { paths: { '/a': {}, '/b': { get: { responses: {}, summary: '乙' } } } }
  const before = JSON.stringify(current)
  assert.equal(JSON.stringify(stableOpenapiOrder(current, previous)), JSON.stringify(previous))
  assert.equal(JSON.stringify(current), before)
})

test('新增删除和字段内容变化仍然可见，不能把旧契约值带回', () => {
  const previous = { paths: { '/old': {}, '/same': { description: '旧', required: true } } }
  const current = { paths: { '/new': {}, '/same': { description: '新', nullable: true } } }
  const ordered = stableOpenapiOrder(current, previous)
  assert.deepEqual(ordered, current)
  assert.equal(Object.hasOwn(ordered.paths, '/old'), false)
  assert.equal(ordered.paths['/same'].description, '新')
  assert.deepEqual(Object.keys(ordered.paths), ['/same', '/new'])
})

test('数组元素顺序、null、标量与类型变化完整保留', () => {
  const current = { required: ['b', 'a'], tags: [{ name: 'B' }, { name: 'A' }], x: null, y: [false, 0, ''] }
  const previous = { tags: [{ name: 'A' }, { name: 'B' }], required: ['a', 'b'], x: {}, y: {} }
  assert.deepEqual(stableOpenapiOrder(current, previous), current)
})

test('没有基线时递归排序对象成员，原型名称作为普通自有字段保留', () => {
  const current = JSON.parse('{"z":1,"__proto__":{"polluted":true},"a":{"z":2,"a":3}}')
  const ordered = stableOpenapiOrder(current)
  assert.deepEqual(ordered, current)
  assert.deepEqual(Object.keys(ordered), ['__proto__', 'a', 'z'])
  assert.deepEqual(Object.keys(ordered.a), ['a', 'z'])
  assert.equal({}.polluted, undefined)
})

test('仅文档换行跨平台稳定，示例默认值与枚举内容保留原字节', () => {
  const literal = { description: '业务\r\n数据', summary: '原样\r保留' }
  const current = { paths: { '/x': { get: { summary: '第一行\r\n第二行', description: '说明\r尾行' } } },
    components: { schemas: { Sample: { description: '模型\r\n说明', example: literal, default: literal,
      examples: [literal], enum: [literal], const: literal } } } }
  const ordered = stableOpenapiOrder(current)
  assert.equal(ordered.paths['/x'].get.summary, '第一行\n第二行')
  assert.equal(ordered.paths['/x'].get.description, '说明\n尾行')
  assert.equal(ordered.components.schemas.Sample.description, '模型\n说明')
  for (const key of ['example', 'default', 'const']) assert.deepEqual(ordered.components.schemas.Sample[key], literal)
  assert.deepEqual(ordered.components.schemas.Sample.examples, [literal])
  assert.deepEqual(ordered.components.schemas.Sample.enum, [literal])
  assert.equal(current.paths['/x'].get.summary, '第一行\r\n第二行')
})

test('Link参数及请求体字面量和扩展载荷不作为文档处理', () => {
  const link = { description: '跳转\r\n说明', parameters: { description: '业务\r\n参数', summary: '摘要\r原文' },
    requestBody: { description: '请求体\r\n内容' } }
  const current = { paths: { '/x': { get: { responses: { 200: { links: { next: link } } },
    'x-payload': { description: '扩展\r\n内容', summary: '扩展\r摘要' } } } } }
  const result = stableOpenapiOrder(current)
  const actual = result.paths['/x'].get.responses[200].links.next
  assert.equal(actual.description, '跳转\n说明')
  assert.deepEqual(actual.parameters, link.parameters)
  assert.deepEqual(actual.requestBody, link.requestBody)
  assert.deepEqual(result.paths['/x'].get['x-payload'], current.paths['/x'].get['x-payload'])
})
