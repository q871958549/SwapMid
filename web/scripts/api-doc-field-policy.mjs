const INTERNAL_FIELDS = new Set(['extraProperties', 'concurrencyStamp', 'isDeleted', 'deleterId', 'deletionTime', 'entityVersion'])

/** 数组和纯列表包装展示元素字段；带汇总或其他业务字段的复合响应保留外壳。 */
export function apiResponseFieldSchema(schema) {
  if (!schema) return null
  if (schema.type === 'array') return schema.items ?? null
  if (schema.properties?.items?.type === 'array'
    && Object.keys(schema.properties).every(name => name === 'items' || name === 'totalCount')) {
    return schema.properties.items.items ?? null
  }
  return schema.properties ? schema : null
}

/** 必填请求字段不能隐藏；业务显式说明的版本字段需展示，以便消费者正确回传。 */
export function shouldDocumentApiField(name, schema, source, required) {
  if (source === 'request' && required) return true
  if (name === 'concurrencyStamp' && typeof schema?.description === 'string' && schema.description.trim()) return true
  return !INTERNAL_FIELDS.has(name)
}
