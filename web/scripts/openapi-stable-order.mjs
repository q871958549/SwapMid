// JSON 对象成员顺序不影响契约。沿用已有快照的成员顺序，避免反射顺序变化产生大批生成噪音。
// 值与数组顺序来自运行时；仅文档换行统一 LF，避免 Windows XML 注释在 Linux CI 中产生伪漂移。
// examples/default/enum/const 等业务字面量完整保留，不规范化其中的同名属性。
export function stableOpenapiOrder(value, previous, literal = false, path = []) {
  if (Array.isArray(value)) {
    return value.map((item, index) => stableOpenapiOrder(
      item,
      Array.isArray(previous) ? previous[index] : undefined,
      literal,
      path,
    ))
  }
  if (value === null || typeof value !== 'object') return value

  const oldKeys = previous !== null && typeof previous === 'object' && !Array.isArray(previous)
    ? Object.keys(previous).filter(key => Object.hasOwn(value, key))
    : []
  const knownKeys = new Set(oldKeys)
  const newKeys = Object.keys(value).filter(key => !knownKeys.has(key)).sort()
  return Object.fromEntries([...oldKeys, ...newKeys].map(key => [
    key,
    !literal && (key === 'summary' || key === 'description') && typeof value[key] === 'string'
      ? value[key].replace(/\r\n?/g, '\n')
      : stableOpenapiOrder(value[key], previous?.[key], literal || key.startsWith('x-')
        || ['example', 'examples', 'default', 'enum', 'const'].includes(key)
        || (path.at(-2) === 'links' && ['parameters', 'requestBody'].includes(key)), [...path, key]),
  ]))
}
