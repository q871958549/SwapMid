import { lstat, realpath } from 'node:fs/promises'
import { isAbsolute, posix, relative, resolve } from 'node:path'
import { readRegularFileBounded } from './i18n-safe-files.mjs'

export const I18N_AUDIT_REGISTRY_RELATIVE_PATH = 'i18n/reference-audit-registry.json'

export const I18N_AUDIT_REGISTRY_LIMITS = Object.freeze({
  bytes: 128 * 1024,
  dynamicEntries: 128,
  translatorBindings: 32,
  fullKeysPerEntry: 256,
  totalFullKeys: 2048,
  pathLength: 240,
  namespaceLength: 128,
  expressionLength: 512,
  reasonLength: 1000,
  keyLength: 256,
  identifierLength: 128,
  expectedOccurrences: 16,
})

const CONTROL_CHARACTERS = /[\u0000-\u001f\u007f]/
const BIDI_CONTROL_CHARACTERS = /[\u202a-\u202e\u2066-\u2069]/
const PRINTABLE_ASCII = /^[\u0020-\u007e]+$/
const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/
const FORBIDDEN_PROPERTY_NAMES = new Set(['__proto__', 'prototype', 'constructor'])

function invalid(message) {
  throw new Error(`i18n 引用审计注册表无效：${message}`)
}

function assertRecord(value, label) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    invalid(`${label} 必须是对象`)
  }
  return value
}

function assertExactKeys(value, required, optional, label) {
  const record = assertRecord(value, label)
  const allowed = new Set([...required, ...optional])
  for (const key of Object.keys(record)) {
    if (!allowed.has(key)) invalid(`${label} 含未知字段 ${JSON.stringify(key)}`)
  }
  for (const key of required) {
    if (!Object.hasOwn(record, key)) invalid(`${label} 缺少字段 ${key}`)
  }
  return record
}

function boundedString(value, label, maxLength, { ascii = false, minLength = 1, pattern } = {}) {
  if (typeof value !== 'string') invalid(`${label} 必须是字符串`)
  if (value !== value.trim()) invalid(`${label} 不得含首尾空白`)
  if (value.length < minLength || value.length > maxLength) {
    invalid(`${label} 长度必须在 ${minLength}..${maxLength} 之间`)
  }
  if (CONTROL_CHARACTERS.test(value)) invalid(`${label} 不得含控制字符`)
  if (BIDI_CONTROL_CHARACTERS.test(value)) invalid(`${label} 不得含双向文本控制字符`)
  if (value !== value.normalize('NFC')) invalid(`${label} 必须使用 Unicode NFC 规范形式`)
  if (ascii && !PRINTABLE_ASCII.test(value)) invalid(`${label} 只允许可打印 ASCII 字符`)
  if (pattern && !pattern.test(value)) invalid(`${label} 格式不合法`)
  return value
}

function sourcePath(value, label) {
  const path = boundedString(
    value,
    label,
    I18N_AUDIT_REGISTRY_LIMITS.pathLength,
    { ascii: true },
  )
  if (
    isAbsolute(path)
    || /^[A-Za-z]:/.test(path)
    || path.startsWith('/')
    || path.includes('\\')
    || path.includes(':')
    || posix.normalize(path) !== path
    || path.split('/').some(part => part === '' || part === '.' || part === '..')
  ) {
    invalid(`${label} 必须是 src 下规范化的 POSIX 相对路径`)
  }
  if (!/\.(?:ts|tsx)$/.test(path)) invalid(`${label} 只允许 .ts/.tsx 源文件`)
  return path
}

function namespace(value, label) {
  return boundedString(
    value,
    label,
    I18N_AUDIT_REGISTRY_LIMITS.namespaceLength,
    { ascii: true, pattern: /^[A-Za-z][A-Za-z0-9_.-]*$/ },
  )
}

function identifier(value, label) {
  const result = boundedString(
    value,
    label,
    I18N_AUDIT_REGISTRY_LIMITS.identifierLength,
    { ascii: true, pattern: IDENTIFIER },
  )
  if (FORBIDDEN_PROPERTY_NAMES.has(result)) invalid(`${label} 使用了保留名称`)
  return result
}

function reason(value, label) {
  return boundedString(
    value,
    label,
    I18N_AUDIT_REGISTRY_LIMITS.reasonLength,
    { minLength: 10 },
  )
}

function validateFullKeys(value, label) {
  if (!Array.isArray(value) || value.length === 0) invalid(`${label} 必须是非空数组`)
  if (value.length > I18N_AUDIT_REGISTRY_LIMITS.fullKeysPerEntry) {
    invalid(`${label} 最多 ${I18N_AUDIT_REGISTRY_LIMITS.fullKeysPerEntry} 项`)
  }
  const seen = new Set()
  const keys = value.map((item, index) => {
    const key = boundedString(
      item,
      `${label}[${index}]`,
      I18N_AUDIT_REGISTRY_LIMITS.keyLength,
      { ascii: true, pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/ },
    )
    if (seen.has(key)) invalid(`${label} 含重复键 ${key}`)
    seen.add(key)
    return key
  })
  const sorted = [...keys].sort()
  if (keys.some((key, index) => key !== sorted[index])) invalid(`${label} 必须按字典序排序`)
  return Object.freeze(keys)
}

function validateDynamicEntry(value, index) {
  const label = `dynamicKeyAllowlist[${index}]`
  const record = assertExactKeys(
    value,
    [
      'file',
      'functionName',
      'variable',
      'method',
      'namespace',
      'expression',
      'expectedOccurrences',
      'fullKeys',
      'reason',
    ],
    [],
    label,
  )
  if (record.functionName !== null && typeof record.functionName !== 'string') {
    invalid(`${label}.functionName 必须是字符串或 null`)
  }
  if (
    !Number.isInteger(record.expectedOccurrences)
    || record.expectedOccurrences < 1
    || record.expectedOccurrences > I18N_AUDIT_REGISTRY_LIMITS.expectedOccurrences
  ) {
    invalid(
      `${label}.expectedOccurrences 必须是 1..${I18N_AUDIT_REGISTRY_LIMITS.expectedOccurrences} 的整数`,
    )
  }
  const method = boundedString(record.method, `${label}.method`, 16, { ascii: true })
  if (!['plain', 'rich', 'markup', 'raw'].includes(method)) invalid(`${label}.method 不受支持`)

  const entry = {
    file: sourcePath(record.file, `${label}.file`),
    functionName: record.functionName === null
      ? null
      : identifier(record.functionName, `${label}.functionName`),
    variable: identifier(record.variable, `${label}.variable`),
    method,
    namespace: namespace(record.namespace, `${label}.namespace`),
    expression: boundedString(
      record.expression,
      `${label}.expression`,
      I18N_AUDIT_REGISTRY_LIMITS.expressionLength,
      { ascii: true },
    ),
    expectedOccurrences: record.expectedOccurrences,
    fullKeys: validateFullKeys(record.fullKeys, `${label}.fullKeys`),
    reason: reason(record.reason, `${label}.reason`),
  }
  return Object.freeze(entry)
}

function validateTranslatorBinding(value, index) {
  const label = `translatorBindingAllowlist[${index}]`
  const record = assertExactKeys(
    value,
    ['id', 'file', 'variable', 'namespace', 'reason'],
    ['functionName'],
    label,
  )
  const binding = {
    id: boundedString(
      record.id,
      `${label}.id`,
      I18N_AUDIT_REGISTRY_LIMITS.identifierLength,
      { ascii: true, pattern: /^[A-Za-z0-9][A-Za-z0-9_-]*$/ },
    ),
    file: sourcePath(record.file, `${label}.file`),
    variable: identifier(record.variable, `${label}.variable`),
    namespace: namespace(record.namespace, `${label}.namespace`),
    reason: reason(record.reason, `${label}.reason`),
  }
  if (Object.hasOwn(record, 'functionName')) {
    binding.functionName = identifier(record.functionName, `${label}.functionName`)
  }
  return Object.freeze(binding)
}

function assertUnique(items, fingerprint, label) {
  const seen = new Set()
  for (const item of items) {
    const value = fingerprint(item)
    if (seen.has(value)) invalid(`${label} 含重复项 ${value}`)
    seen.add(value)
  }
}

function validateRegistry(value) {
  const record = assertExactKeys(
    value,
    ['version', 'dynamicKeyAllowlist', 'translatorBindingAllowlist'],
    [],
    '根对象',
  )
  if (record.version !== 1) invalid('version 必须严格等于 1')
  if (!Array.isArray(record.dynamicKeyAllowlist)) invalid('dynamicKeyAllowlist 必须是数组')
  if (!Array.isArray(record.translatorBindingAllowlist)) invalid('translatorBindingAllowlist 必须是数组')
  if (record.dynamicKeyAllowlist.length > I18N_AUDIT_REGISTRY_LIMITS.dynamicEntries) {
    invalid(`dynamicKeyAllowlist 最多 ${I18N_AUDIT_REGISTRY_LIMITS.dynamicEntries} 项`)
  }
  if (record.translatorBindingAllowlist.length > I18N_AUDIT_REGISTRY_LIMITS.translatorBindings) {
    invalid(`translatorBindingAllowlist 最多 ${I18N_AUDIT_REGISTRY_LIMITS.translatorBindings} 项`)
  }

  const dynamicKeyAllowlist = Object.freeze(record.dynamicKeyAllowlist.map(validateDynamicEntry))
  const translatorBindingAllowlist = Object.freeze(
    record.translatorBindingAllowlist.map(validateTranslatorBinding),
  )
  assertUnique(
    dynamicKeyAllowlist,
    item => JSON.stringify([
      item.file,
      item.functionName,
      item.variable,
      item.method,
      item.namespace,
      item.expression,
    ]),
    'dynamicKeyAllowlist',
  )
  assertUnique(translatorBindingAllowlist, item => item.id, 'translatorBindingAllowlist')
  assertUnique(
    translatorBindingAllowlist,
    item => JSON.stringify([item.file, item.functionName ?? null, item.variable]),
    'translatorBindingAllowlist selector',
  )
  const totalFullKeys = dynamicKeyAllowlist.reduce((total, entry) => total + entry.fullKeys.length, 0)
  if (totalFullKeys > I18N_AUDIT_REGISTRY_LIMITS.totalFullKeys) {
    invalid(`fullKeys 总数不得超过 ${I18N_AUDIT_REGISTRY_LIMITS.totalFullKeys}`)
  }
  return Object.freeze({ dynamicKeyAllowlist, translatorBindingAllowlist })
}

function assertInside(root, path, label) {
  const child = relative(root, path)
  if (child === '' || child.startsWith('..') || isAbsolute(child)) {
    invalid(`${label} 越出工作区`)
  }
}

async function readRegistryFile(workspaceRoot) {
  const root = await realpath(resolve(workspaceRoot))
  const path = resolve(root, I18N_AUDIT_REGISTRY_RELATIVE_PATH)
  assertInside(root, path, '注册表路径')
  const linkStat = await lstat(path)
  if (!linkStat.isFile() || linkStat.isSymbolicLink()) invalid('注册表必须是普通文件且不得是符号链接')
  const resolvedPath = await realpath(path)
  assertInside(root, resolvedPath, '注册表真实路径')

  return (
    await readRegularFileBounded(path, {
      maxBytes: I18N_AUDIT_REGISTRY_LIMITS.bytes,
      label: 'i18n 引用审计注册表',
    })
  ).text
}

export async function loadI18nAuditRegistry(workspaceRoot) {
  let source
  try {
    source = await readRegistryFile(workspaceRoot)
  } catch (error) {
    if (error instanceof Error && error.message.startsWith('i18n 引用审计注册表无效：')) throw error
    invalid(`无法安全读取 ${I18N_AUDIT_REGISTRY_RELATIVE_PATH}：${error.code ?? error.message}`)
  }

  let parsed
  try {
    parsed = JSON.parse(source)
  } catch {
    invalid(`${I18N_AUDIT_REGISTRY_RELATIVE_PATH} 不是合法 JSON`)
  }
  const registry = validateRegistry(parsed)
  const normalizedSource = source.replace(/\r\n/g, '\n')
  if (normalizedSource.includes('\r') || normalizedSource !== `${JSON.stringify(parsed, null, 2)}\n`) {
    invalid('注册表必须使用两空格缩进、无重复字段并以单个换行结尾')
  }
  return registry
}
