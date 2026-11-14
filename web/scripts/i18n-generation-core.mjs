import { createHash } from 'node:crypto'

import { extractIcuArgumentNames, flattenMessages } from './i18n-message-contracts.mjs'
import { I18N_JSON_FILE_MAX_BYTES } from './i18n-safe-files.mjs'

export const GENERATOR_VERSION = 1

export const GENERATION_CONFIG_LIMITS = Object.freeze({
  protectedTerms: 128,
  glossaryEntries: 256,
  overrideEntries: 2_000,
  termCharacters: 128,
  glossaryValueCharacters: 256,
  overrideKeyCharacters: 512,
  overrideValueCharacters: 20_000,
  totalCharacters: 512_000,
  traditionalOutputCharacters: 100_000,
})

const CONTROL_CHARACTERS = /[\u0000-\u001f\u007f]/
const BIDI_CONTROL_CHARACTERS = /[\u202a-\u202e\u2066-\u2069]/
const RESERVED_TERM_MARKER = '__SWAPMIDI18N_TERM_'
const FORBIDDEN_KEYS = new Set(['__proto__', 'prototype', 'constructor'])

export function hashText(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex').slice(0, 24)
}

function compareUnicodeCodePoints(left, right) {
  return left < right ? -1 : left > right ? 1 : 0
}

export function serializeJson(
  value,
  { label = 'i18n JSON 生成物', maxBytes = I18N_JSON_FILE_MAX_BYTES } = {},
) {
  if (!Number.isSafeInteger(maxBytes) || maxBytes <= 0) {
    throw new Error('i18n JSON 生成物上限必须是正安全整数')
  }
  const serialized = `${JSON.stringify(value, null, 2)}\n`
  const bytes = Buffer.byteLength(serialized, 'utf8')
  if (bytes > maxBytes) {
    throw new Error(`${label}格式化后为 ${bytes} 字节，超过上限 ${maxBytes} 字节`)
  }
  return serialized
}

export function assertStringLeaves(catalog, catalogName) {
  const invalid = [...flattenMessages(catalog)].filter(([, value]) => typeof value !== 'string')
  if (invalid.length === 0) return

  const preview = invalid.slice(0, 10).map(([key]) => key).join(', ')
  throw new Error(`${catalogName} 包含非字符串叶子节点：${preview}`)
}

export function mapCatalog(catalog, transform, prefix = '') {
  if (typeof catalog === 'string') return transform(catalog, prefix)
  if (!catalog || typeof catalog !== 'object' || Array.isArray(catalog)) return catalog

  return Object.fromEntries(
    Object.entries(catalog).map(([key, value]) => {
      const fullKey = prefix ? `${prefix}.${key}` : key
      return [key, mapCatalog(value, transform, fullKey)]
    }),
  )
}

export function buildCatalogFromSource(sourceCatalog, valuesByKey) {
  return mapCatalog(sourceCatalog, (_sourceValue, key) => {
    if (!valuesByKey.has(key)) throw new Error(`缺少生成值：${key}`)
    return valuesByKey.get(key)
  })
}

export function extractRichTextTagNames(message) {
  if (typeof message !== 'string') return []

  const tags = new Set()
  const pattern = /<\/?([A-Za-z][A-Za-z0-9_-]*)\b[^>]*>/g
  for (const match of message.matchAll(pattern)) tags.add(match[1])
  return [...tags].sort()
}

function sortedIcuArguments(message) {
  return [...extractIcuArgumentNames(message)].sort()
}

export function validateMessageContract(key, source, translated, locale) {
  if (typeof translated !== 'string') {
    throw new Error(`${locale} 的 ${key} 不是字符串`)
  }

  const sourceArguments = sortedIcuArguments(source)
  const translatedArguments = sortedIcuArguments(translated)
  if (sourceArguments.join('\u0000') !== translatedArguments.join('\u0000')) {
    throw new Error(
      `${locale} 的 ${key} 改变了 ICU 参数：期望 [${sourceArguments.join(', ')}]，实际 [${translatedArguments.join(', ')}]`,
    )
  }

  const sourceTags = extractRichTextTagNames(source)
  const translatedTags = extractRichTextTagNames(translated)
  if (sourceTags.join('\u0000') !== translatedTags.join('\u0000')) {
    throw new Error(
      `${locale} 的 ${key} 改变了富文本标签：期望 [${sourceTags.join(', ')}]，实际 [${translatedTags.join(', ')}]`,
    )
  }
}

function relevantGenerationRules(source, config) {
  const protectedTerms = config.protectedTerms.filter((term) => source.includes(term))
  const englishTerms = Object.entries(config.englishTerms)
    .filter(([term]) => source.includes(term))
    .sort(([left], [right]) => compareUnicodeCodePoints(left, right))
  return { protectedTerms, englishTerms }
}

export function sourceFingerprint(key, source, config) {
  return hashText(
    JSON.stringify({
      generatorVersion: GENERATOR_VERSION,
      promptVersion: config.promptVersion,
      key,
      source,
      rules: relevantGenerationRules(source, config),
      override: config.englishOverrides[key] ?? null,
    }),
  )
}

export function createStateEntry(key, source, english, config) {
  return {
    sourceHash: sourceFingerprint(key, source, config),
    englishHash: hashText(english),
  }
}

function applicableTraditionalTerms(source, config) {
  return Object.entries(config.traditionalTerms)
    .filter(([term]) => source.includes(term))
    .sort(
      ([left], [right]) =>
        right.length - left.length || compareUnicodeCodePoints(left, right),
    )
}

export function traditionalSourceFingerprint(key, source, config) {
  return hashText(
    JSON.stringify({
      generatorVersion: GENERATOR_VERSION,
      key,
      source,
      terms: applicableTraditionalTerms(source, config),
      override: config.traditionalOverrides[key] ?? null,
    }),
  )
}

export function generateTraditionalValue(key, source, config, convert) {
  const override = config.traditionalOverrides[key]
  if (typeof override === 'string') {
    validateMessageContract(key, source, override, 'zh-Hant')
    return override
  }

  const terms = applicableTraditionalTerms(source, config)
  let value = ''
  for (let index = 0; index < source.length;) {
    const match = terms.find(([simplified]) => source.startsWith(simplified, index))
    if (match) {
      value += match[1]
      index += match[0].length
    } else {
      value += source[index]
      index += 1
    }
    if (value.length > GENERATION_CONFIG_LIMITS.traditionalOutputCharacters) {
      throw new Error(
        `zh-Hant 的 ${key} 应用术语后超过 ${GENERATION_CONFIG_LIMITS.traditionalOutputCharacters} 字符上限`,
      )
    }
  }
  value = convert(value)
  validateMessageContract(key, source, value, 'zh-Hant')
  return value
}

export function planTraditionalGeneration(sourceCatalog, traditionalCatalog, state, config, convert) {
  const sourceMessages = flattenMessages(sourceCatalog)
  const traditionalMessages = flattenMessages(traditionalCatalog)
  const stateEntries = state?.entries ?? {}
  const values = new Map()
  const pending = []
  const manualEdits = []

  for (const [key, source] of sourceMessages) {
    const override = config.traditionalOverrides[key]
    if (typeof override === 'string') {
      validateMessageContract(key, source, override, 'zh-Hant')
      values.set(key, override)
      continue
    }

    const current = traditionalMessages.get(key)
    const entry = stateEntries[key]
    if (
      typeof current === 'string' &&
      entry?.traditionalSourceHash === traditionalSourceFingerprint(key, source, config)
    ) {
      if (entry.traditionalHash !== hashText(current)) {
        manualEdits.push(key)
      } else {
        validateMessageContract(key, source, current, 'zh-Hant')
        values.set(key, current)
      }
      continue
    }

    const generated = generateTraditionalValue(key, source, config, convert)
    values.set(key, generated)
    pending.push(key)
  }

  return { values, pending, manualEdits }
}

/** 平台界面默认以简中为事实源；仅受维护前缀强制要求繁中和英文与简中源严格同步。 */
export const MAINTAINED_TRANSLATION_PREFIXES = ['PublicPortal.']

export const FRONTEND_TRANSLATION_BUDGET = Object.freeze({
  label: '前端 i18n',
  maxEntries: 1_000,
  maxSourceCharacters: 250_000,
})

export const FRONTEND_SKIPPED_SOURCE_BUDGET = Object.freeze({
  label: '前端 i18n（仅简中）',
  maxEntries: 20_000,
  maxSourceCharacters: 1_000_000,
})

export function isMaintainedTranslationKey(key) {
  return MAINTAINED_TRANSLATION_PREFIXES.some((prefix) => key.startsWith(prefix))
}

export function partitionMaintainedPending(pending) {
  const maintainedPending = []
  const skippedPending = []
  for (const entry of pending) {
    if (isMaintainedTranslationKey(entry.key)) {
      maintainedPending.push(entry)
    } else {
      skippedPending.push(entry)
    }
  }
  return { maintainedPending, skippedPending }
}

export function resolveSkippedEnglishValue(key, sourceValue, existingEnglishValue) {
  if (typeof existingEnglishValue === 'string') {
    try {
      validateMessageContract(key, sourceValue, existingEnglishValue, 'en')
      return existingEnglishValue
    } catch {
      // 简中源变更了 ICU 参数或标签契约时，不再复用旧英文译文，安全回退为简中源文
    }
  }
  validateMessageContract(key, sourceValue, sourceValue, 'en')
  return sourceValue
}

export function planEnglishGeneration(sourceCatalog, englishCatalog, state, config) {
  const sourceMessages = flattenMessages(sourceCatalog)
  const englishMessages = flattenMessages(englishCatalog)
  const stateEntries = state?.entries ?? {}
  const preserved = new Map()
  const pending = []
  const manualEdits = []

  for (const [key, source] of sourceMessages) {
    const override = config.englishOverrides[key]
    if (typeof override === 'string') {
      validateMessageContract(key, source, override, 'en')
      preserved.set(key, override)
      continue
    }

    const english = englishMessages.get(key)
    const entry = stateEntries[key]
    const sourceHash = sourceFingerprint(key, source, config)

    if (typeof english === 'string' && entry?.sourceHash === sourceHash) {
      if (entry.englishHash !== hashText(english)) {
        manualEdits.push(key)
        // 平台文案不再强制重译。哈希漂移的现译文仍要留在全量目录里，否则同步写回会在第一个键上报「缺少生成值」。
        if (!isMaintainedTranslationKey(key)) {
          validateMessageContract(key, source, english, 'en')
          preserved.set(key, english)
        }
      } else {
        validateMessageContract(key, source, english, 'en')
        preserved.set(key, english)
      }
      continue
    }

    pending.push({ key, source })
  }

  return { preserved, pending, manualEdits }
}

export function assertTranslationBudget(
  pending,
  {
    label,
    maxEntries,
    maxSourceCharacters,
    maxSourceCharactersPerEntry = 20_000,
  },
) {
  if (pending.length > maxEntries) {
    throw new Error(`${label} 待翻译键数 ${pending.length} 超过上限 ${maxEntries}`)
  }
  let characters = 0
  for (const entry of pending) {
    if (entry.source.includes(RESERVED_TERM_MARKER)) {
      throw new Error(`${label} 单条文案 ${entry.key} 使用了保留术语占位符前缀`)
    }
    let count = 0
    for (let index = 0; index < entry.source.length;) {
      count += 1
      index += entry.source.codePointAt(index) > 0xffff ? 2 : 1
      if (count > maxSourceCharactersPerEntry) break
    }
    if (count > maxSourceCharactersPerEntry) {
      throw new Error(
        `${label} 单条文案 ${entry.key} 的 ${count} 字符超过上限 ${maxSourceCharactersPerEntry}`,
      )
    }
    characters += count
  }
  if (characters > maxSourceCharacters) {
    throw new Error(`${label} 待翻译源文 ${characters} 字符超过上限 ${maxSourceCharacters}`)
  }
  return { entries: pending.length, sourceCharacters: characters }
}

export function buildGenerationState(sourceCatalog, traditionalCatalog, englishCatalog, config) {
  const sourceMessages = flattenMessages(sourceCatalog)
  const traditionalMessages = flattenMessages(traditionalCatalog)
  const englishMessages = flattenMessages(englishCatalog)
  const entries = {}

  for (const [key, source] of sourceMessages) {
    const traditional = traditionalMessages.get(key)
    const english = englishMessages.get(key)
    if (typeof traditional !== 'string') throw new Error(`繁中词典缺少键：${key}`)
    if (typeof english !== 'string') throw new Error(`英文词典缺少键：${key}`)
    validateMessageContract(key, source, traditional, 'zh-Hant')
    validateMessageContract(key, source, english, 'en')
    entries[key] = {
      ...createStateEntry(key, source, english, config),
      traditionalSourceHash: traditionalSourceFingerprint(key, source, config),
      traditionalHash: hashText(traditional),
    }
  }

  return {
    version: 1,
    generatorVersion: GENERATOR_VERSION,
    promptVersion: config.promptVersion,
    entries,
  }
}

export function validateConfig(config) {
  if (!config || typeof config !== 'object' || Array.isArray(config)) {
    throw new Error('generation-config.json 根节点必须是对象')
  }
  const requiredFields = [
    'version',
    'promptVersion',
    'protectedTerms',
    'englishTerms',
    'traditionalTerms',
    'englishOverrides',
    'traditionalOverrides',
  ]
  const actualFields = Object.keys(config)
  const unknownFields = actualFields.filter(field => !requiredFields.includes(field))
  const missingFields = requiredFields.filter(field => !Object.hasOwn(config, field))
  if (unknownFields.length > 0 || missingFields.length > 0) {
    throw new Error(
      `generation-config.json 字段不合法；未知 [${unknownFields.join(', ')}]；缺少 [${missingFields.join(', ')}]`,
    )
  }
  if (config?.version !== 1) throw new Error('generation-config.json 的 version 必须为 1')
  if (!Number.isInteger(config.promptVersion) || config.promptVersion < 1 || config.promptVersion > 1_000_000) {
    throw new Error('generation-config.json 的 promptVersion 必须为 1..1000000 的整数')
  }

  let totalCharacters = 0
  function boundedText(value, label, minimum, maximum) {
    if (typeof value !== 'string') throw new Error(`${label} 必须是字符串`)
    // 配置来自 PR 数据；按 UTF-16 code unit 作保守上限可 O(1) 拒绝巨型字符串，
    // 避免 `[...value]` 为 16 MiB JSON 额外物化数百万元素数组。
    const characters = value.length
    if (characters < minimum || characters > maximum) {
      throw new Error(`${label} 长度必须为 ${minimum}..${maximum} 个字符`)
    }
    if (CONTROL_CHARACTERS.test(value) || BIDI_CONTROL_CHARACTERS.test(value)) {
      throw new Error(`${label} 不得包含控制字符或双向文本控制符`)
    }
    if (value !== value.normalize('NFC')) throw new Error(`${label} 必须使用 Unicode NFC`)
    if (value.includes(RESERVED_TERM_MARKER)) throw new Error(`${label} 使用了保留术语占位符前缀`)
    totalCharacters += characters
    if (totalCharacters > GENERATION_CONFIG_LIMITS.totalCharacters) {
      throw new Error(
        `generation-config.json 文本总量超过 ${GENERATION_CONFIG_LIMITS.totalCharacters} 字符`,
      )
    }
    return value
  }

  if (!Array.isArray(config.protectedTerms)) {
    throw new Error('generation-config.json 的 protectedTerms 必须为字符串数组')
  }
  if (config.protectedTerms.length > GENERATION_CONFIG_LIMITS.protectedTerms) {
    throw new Error(
      `generation-config.json 的 protectedTerms 最多 ${GENERATION_CONFIG_LIMITS.protectedTerms} 项`,
    )
  }
  const protectedTerms = new Set()
  for (const [index, term] of config.protectedTerms.entries()) {
    boundedText(
      term,
      `generation-config.json 的 protectedTerms[${index}]`,
      2,
      GENERATION_CONFIG_LIMITS.termCharacters,
    )
    if (protectedTerms.has(term)) throw new Error(`generation-config.json 的 protectedTerms 含重复项 ${term}`)
    protectedTerms.add(term)
  }

  const objectContracts = [
    ['englishTerms', GENERATION_CONFIG_LIMITS.glossaryEntries, 2, GENERATION_CONFIG_LIMITS.termCharacters, 1, GENERATION_CONFIG_LIMITS.glossaryValueCharacters],
    ['traditionalTerms', GENERATION_CONFIG_LIMITS.glossaryEntries, 2, GENERATION_CONFIG_LIMITS.termCharacters, 1, GENERATION_CONFIG_LIMITS.glossaryValueCharacters],
    ['englishOverrides', GENERATION_CONFIG_LIMITS.overrideEntries, 1, GENERATION_CONFIG_LIMITS.overrideKeyCharacters, 1, GENERATION_CONFIG_LIMITS.overrideValueCharacters],
    ['traditionalOverrides', GENERATION_CONFIG_LIMITS.overrideEntries, 1, GENERATION_CONFIG_LIMITS.overrideKeyCharacters, 1, GENERATION_CONFIG_LIMITS.overrideValueCharacters],
  ]
  for (const [field, maxEntries, keyMin, keyMax, valueMin, valueMax] of objectContracts) {
    if (!config[field] || typeof config[field] !== 'object' || Array.isArray(config[field])) {
      throw new Error(`generation-config.json 的 ${field} 必须为对象`)
    }
    const entries = Object.entries(config[field])
    if (entries.length > maxEntries) {
      throw new Error(`generation-config.json 的 ${field} 最多 ${maxEntries} 项`)
    }
    for (const [entryKey, entryValue] of entries) {
      if (FORBIDDEN_KEYS.has(entryKey)) throw new Error(`generation-config.json 的 ${field} 使用了保留键 ${entryKey}`)
      boundedText(entryKey, `generation-config.json 的 ${field} 键`, keyMin, keyMax)
      boundedText(entryValue, `generation-config.json 的 ${field}.${entryKey}`, valueMin, valueMax)
    }
  }
  for (const term of Object.keys(config.englishTerms)) {
    if (protectedTerms.has(term)) {
      throw new Error(`generation-config.json 的术语 ${term} 不得同时出现在 protectedTerms 与 englishTerms`)
    }
  }
}
