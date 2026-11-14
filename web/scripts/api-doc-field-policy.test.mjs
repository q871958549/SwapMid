import { strict as assert } from 'node:assert'
import { test } from 'node:test'
import { apiResponseFieldSchema, shouldDocumentApiField } from './api-doc-field-policy.mjs'

test('顶层列表数组与ABP分页列表均保留元素引用供字段展开', () => {
  const item = { $ref: '#/components/schemas/PipelineExecutionDto' }
  assert.equal(apiResponseFieldSchema({ type: 'array', items: item }), item)
  assert.equal(apiResponseFieldSchema({ properties: { items: { type: 'array', items: item } } }), item)
  assert.equal(apiResponseFieldSchema({ properties: { items: { type: 'array', items: item }, totalCount: { type: 'integer' } } }), item)
})

test('复合统计响应必须保留明细列表、计数和状态分布', () => {
  const item = { $ref: '#/components/schemas/ReconciliationItemDto' }
  const response = { properties: {
    items: { type: 'array', items: item },
    pendingVerificationItems: { type: 'array', items: item },
    platformActiveCount: { type: 'integer' },
    statusDistribution: { type: 'array', items: { type: 'object' } },
  } }
  assert.equal(apiResponseFieldSchema(response), response)
})

test('普通对象继续展开，空schema、标量与无元素数组不会伪造字段', () => {
  const object = { properties: { value: { type: 'string' } } }
  assert.equal(apiResponseFieldSchema(object), object)
  for (const schema of [undefined, { type: 'string' }, { type: 'array' }]) assert.equal(apiResponseFieldSchema(schema), null)
})

test('确认接口的必填版本不能被当作内部字段隐藏', () => {
  assert.equal(shouldDocumentApiField('concurrencyStamp', {}, 'request', true), true)
})
test('显式说明用途的业务版本在请求和响应中都展示', () => {
  for (const source of ['request', 'response']) {
    assert.equal(shouldDocumentApiField('concurrencyStamp', { description: '编辑版本，保存时原样回传' }, source, false), true)
  }
})
test('没有业务说明的基类版本和其他内部字段仍隐藏', () => {
  assert.equal(shouldDocumentApiField('concurrencyStamp', {}, 'response', false), false)
  assert.equal(shouldDocumentApiField('extraProperties', {}, 'response', false), false)
  assert.equal(shouldDocumentApiField('previewToken', {}, 'request', true), true)
})
