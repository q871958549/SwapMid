import { existsSync, readdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import * as OpenCC from 'opencc-js'

import {
  assertTranslationBudget,
  assertStringLeaves,
  buildCatalogFromSource,
  buildGenerationState,
  planEnglishGeneration,
  planTraditionalGeneration,
  serializeJson,
  validateConfig,
  validateMessageContract,
} from './i18n-generation-core.mjs'
import { translateEnglish } from './i18n-english-translator.mjs'
import {
  I18N_JSON_FILE_MAX_BYTES,
  readRegularFileBoundedSync,
  writeFileAtomic,
} from './i18n-safe-files.mjs'

const WEB_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const REPOSITORY_ROOT = process.env.I18N_REPOSITORY_ROOT
  ? resolve(process.env.I18N_REPOSITORY_ROOT)
  : resolve(WEB_ROOT, '..')
const RESOURCE_ROOT = resolve(
  REPOSITORY_ROOT,
  'src/SwapMid.Domain.Shared/Localization/SwapMid',
)
const PATHS = {
  source: resolve(RESOURCE_ROOT, 'zh-Hans.json'),
  traditional: resolve(RESOURCE_ROOT, 'zh-Hant.json'),
  english: resolve(RESOURCE_ROOT, 'en.json'),
  config: resolve(REPOSITORY_ROOT, 'web/i18n/generation-config.json'),
  state: resolve(
    REPOSITORY_ROOT,
    'src/SwapMid.Domain.Shared/I18n/translation-state.json',
  ),
  errorCodesDirectory: resolve(
    REPOSITORY_ROOT,
    'src/SwapMid.Domain.Shared',
  ),
}
const MAX_BACKEND_RESOURCE_KEYS = 5_000
const MAX_BACKEND_RESOURCE_VALUE_CHARS = 20_000
const MAX_BACKEND_RESOURCE_TOTAL_VALUE_CHARS = 1_000_000
const MAX_DOMAIN_ERROR_DECLARATIONS = 5_000

const INITIAL_ENGLISH = {
  'Menu:ContactUs': 'Contact Us',
  'Menu:ArticleSample': 'Article Sample',
  Home: 'Home',
  Date: 'Date',
  'Menu:Dashboard': 'Dashboard',
  'Menu:HomePage': 'Home',
  Dashboard: 'Dashboard',
  Language: 'Language',
  Search: 'Search',
  LoadMore: 'Load More',
  Settings: 'Settings',
  Theme: 'Theme',
  DeviceTheme: 'Device Theme',
  Dark: 'Dark',
  Light: 'Light',
  Unspecified: 'Follow System',
  SeeAllUsers: 'View All Users',
  TakePhoto: 'Take Photo',
  ChoosePhoto: 'Choose Photo',
  'Permission:SwapMid': 'SwapMid Integration Middleware',
  'Permission:AccountManagement:Users:Impersonate': 'Impersonate User',
}

const WORD_OVERRIDES = new Map([
  ['Api', 'API'],
  ['Hr', 'HR'],
  ['It', 'IT'],
  ['Mini', 'Mini'],
  ['Oidc', 'OIDC'],
])

function humanizePascalCase(value) {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .split(' ')
    .map((word) => WORD_OVERRIDES.get(word) ?? word)
    .join(' ')
}

function readDomainErrorCodeSources(domainShared, { maxBytes }) {
  let entries
  try {
    entries = readdirSync(domainShared, { withFileTypes: true })
  } catch (error) {
    throw new Error(`无法枚举领域错误码目录：${error.message}`)
  }
  const names = entries
    .filter((entry) => entry.isFile()
      && /^SwapMidDomainErrorCodes.*\.cs$/.test(entry.name))
    .map((entry) => entry.name)
    .sort()
  if (names.length === 0) {
    throw new Error('缺少 SwapMidDomainErrorCodes*.cs')
  }
  return names.map((name) => readRegularFileBoundedSync(resolve(domainShared, name), {
    maxBytes,
    label: `后端领域错误码常量文件 ${name}`,
  }).text).join('\n')
}

function domainErrorNames() {
  const source = readDomainErrorCodeSources(PATHS.errorCodesDirectory, {
    maxBytes: I18N_JSON_FILE_MAX_BYTES,
  })
  const names = new Map()
  const pattern = /public\s+const\s+string\s+(\w+)\s*=\s*"(SwapMid:[^"]+)"/g
  let match
  let declarations = 0
  while ((match = pattern.exec(source)) !== null) {
    declarations += 1
    if (declarations > MAX_DOMAIN_ERROR_DECLARATIONS) {
      throw new Error(`后端领域错误码声明不得超过 ${MAX_DOMAIN_ERROR_DECLARATIONS}`)
    }
    names.set(match[2], match[1])
  }
  return names
}

function initialEnglishValue(key, errorNames) {
  if (INITIAL_ENGLISH[key]) return INITIAL_ENGLISH[key]
  if (key.startsWith('SwapMid:') && errorNames.has(key)) {
    return humanizePascalCase(errorNames.get(key))
  }
  if (!key.startsWith('Permission:')) return null

  const parts = key.split(':')
  const leaf = parts.at(-1)
  const actionLabels = {
    Adjust: 'Adjust',
    Cancel: 'Cancel',
    Confirm: 'Confirm',
    Create: 'Create',
    Delete: 'Delete',
    Execute: 'Execute',
    Export: 'Export',
    Import: 'Import',
    Manage: 'Manage',
    Process: 'Process',
    Publish: 'Publish',
    Refill: 'Refill',
    Remind: 'Remind',
    Replay: 'Replay',
    Review: 'Review',
    Trigger: 'Trigger',
    Unbind: 'Unbind',
    Update: 'Update',
    ViewSensitive: 'View Sensitive Data',
  }
  if (parts.length >= 4 && actionLabels[leaf]) return actionLabels[leaf]
  return humanizePascalCase(leaf)
}

function readJson(path, label) {
  try {
    return JSON.parse(
      readRegularFileBoundedSync(path, {
        maxBytes: I18N_JSON_FILE_MAX_BYTES,
        label,
      }).text,
    )
  } catch (error) {
    throw new Error(`无法读取 ${label}（${path}）：${error.message}`)
  }
}

function textsOf(resource, label) {
  if (resource === null || Array.isArray(resource) || typeof resource !== 'object') {
    throw new Error(`${label} 根节点必须是对象`)
  }
  const texts = resource.texts ?? resource.Texts
  if (!texts || typeof texts !== 'object' || Array.isArray(texts)) {
    throw new Error(`${label} 缺少 texts 对象`)
  }
  let keys = 0
  let totalValueChars = 0
  for (const key in texts) {
    if (!Object.hasOwn(texts, key)) continue
    keys += 1
    if (keys > MAX_BACKEND_RESOURCE_KEYS) {
      throw new Error(`${label} 本地化资源键不得超过 ${MAX_BACKEND_RESOURCE_KEYS}`)
    }
    const value = texts[key]
    if (typeof value !== 'string') throw new Error(`${label} 本地化资源值必须是字符串：${key}`)
    if (value.length > MAX_BACKEND_RESOURCE_VALUE_CHARS) {
      throw new Error(`${label} 单条资源不得超过 ${MAX_BACKEND_RESOURCE_VALUE_CHARS} 字符：${key}`)
    }
    if (value.length > MAX_BACKEND_RESOURCE_TOTAL_VALUE_CHARS - totalValueChars) {
      throw new Error(`${label} 资源文本总量不得超过 ${MAX_BACKEND_RESOURCE_TOTAL_VALUE_CHARS} 字符`)
    }
    totalValueChars += value.length
  }
  return texts
}

function resource(culture, texts) {
  return { culture, texts }
}

async function main() {
  const check = process.argv.includes('--check')
  const bootstrap = process.argv.includes('--bootstrap')
  const validateInput = process.argv.includes('--validate-input')
  if ([check, bootstrap, validateInput].filter(Boolean).length > 1) {
    throw new Error('--check、--bootstrap 与 --validate-input 只能使用一个')
  }

  const sourceResource = readJson(PATHS.source, '后端简中资源')
  const traditionalResource = readJson(PATHS.traditional, '后端繁中资源')
  const englishResource = readJson(PATHS.english, '后端英文资源')
  const source = textsOf(sourceResource, '后端简中资源')
  const currentTraditional = textsOf(traditionalResource, '后端繁中资源')
  const currentEnglish = textsOf(englishResource, '后端英文资源')
  const config = readJson(PATHS.config, '生成配置')
  validateConfig(config)
  assertStringLeaves(source, '后端 zh-Hans')
  assertStringLeaves(currentTraditional, '后端 zh-Hant')
  assertStringLeaves(currentEnglish, '后端 en')

  const convert = OpenCC.Converter({ from: 'cn', to: 'tw' })

  if (bootstrap) {
    const errorNames = domainErrorNames()
    const traditionalValues = new Map()
    const englishValues = new Map()
    for (const [key, sourceValue] of Object.entries(source)) {
      const traditional =
        currentTraditional[key] ??
        planTraditionalGeneration(
          { [key]: sourceValue },
          {},
          { entries: {} },
          config,
          convert,
        ).values.get(key)
      const english = currentEnglish[key] ?? initialEnglishValue(key, errorNames)
      if (typeof english !== 'string') {
        throw new Error(`后端英文资源缺少 ${key}，且没有初始种子；请先补译后再接管`)
      }
      validateMessageContract(key, sourceValue, traditional, 'zh-Hant')
      validateMessageContract(key, sourceValue, english, 'en')
      traditionalValues.set(key, traditional)
      englishValues.set(key, english)
    }

    const traditional = buildCatalogFromSource(source, traditionalValues)
    const english = buildCatalogFromSource(source, englishValues)
    const state = buildGenerationState(source, traditional, english, config)
    const outputs = new Map([
      [
        PATHS.traditional,
        serializeJson(resource('zh-Hant', traditional), { label: '后端 zh-Hant.json' }),
      ],
      [PATHS.english, serializeJson(resource('en', english), { label: '后端 en.json' })],
      [PATHS.state, serializeJson(state, { label: '后端 i18n/translation-state.json' })],
    ])
    for (const [path, content] of outputs) writeFileAtomic(path, content)
    console.log(`[backend-i18n] 已接管并补齐 ${Object.keys(source).length} 个后端三语资源键。`)
    return
  }

  if (!existsSync(PATHS.state)) {
    throw new Error('缺少后端 i18n 生成状态；首次接管请运行 pnpm i18n:backend-bootstrap')
  }
  const state = readJson(PATHS.state, '后端生成状态')
  const traditionalPlan = planTraditionalGeneration(
    source,
    currentTraditional,
    state,
    config,
    convert,
  )
  const englishPlan = planEnglishGeneration(source, currentEnglish, state, config)
  if (traditionalPlan.manualEdits.length > 0 || englishPlan.manualEdits.length > 0) {
    throw new Error(
      '检测到后端生成物被直接修改；请通过 generation-config.json 覆盖层修订：' +
        [...traditionalPlan.manualEdits, ...englishPlan.manualEdits].slice(0, 20).join(', '),
    )
  }

  const traditional = buildCatalogFromSource(source, traditionalPlan.values)
  const traditionalContent = serializeJson(resource('zh-Hant', traditional), {
    label: '后端 zh-Hant.json',
  })
  const budget = assertTranslationBudget(englishPlan.pending, {
    label: '后端 i18n',
    maxEntries: 500,
    maxSourceCharacters: 100_000,
  })
  if (validateInput) {
    console.log(
      `[backend-i18n] 翻译输入有效：待翻译 ${budget.entries} 条 / ${budget.sourceCharacters} 字符，未越过预算。`,
    )
    return
  }

  if (!check && englishPlan.pending.length > 0) {
    const translated = await translateEnglish(englishPlan.pending, config, {
      onProgress: ({ current, total, count }) =>
        console.log(`[backend-i18n] 正在生成英文 ${current}/${total}（本批 ${count} 条）`),
    })
    for (const { key, source: sourceValue } of englishPlan.pending) {
      const value = translated.get(key)
      validateMessageContract(key, sourceValue, value, 'en')
      englishPlan.preserved.set(key, value)
    }
  }

  if (check) {
    console.log(
      `[backend-i18n] 后端三语资源状态检查通过（待增量翻译 ${englishPlan.pending.length} 条）。`,
    )
    return
  }

  const english = buildCatalogFromSource(source, englishPlan.preserved)
  const generatedState = buildGenerationState(source, traditional, english, config)
  const outputs = new Map([
    [PATHS.traditional, traditionalContent],
    [PATHS.english, serializeJson(resource('en', english), { label: '后端 en.json' })],
    [
      PATHS.state,
      serializeJson(generatedState, { label: '后端 i18n/translation-state.json' }),
    ],
  ])

  for (const [path, content] of outputs) writeFileAtomic(path, content)
  console.log(
    `[backend-i18n] 同步完成：繁中新增/变更 ${traditionalPlan.pending.length} 条；` +
      `英文新增/变更 ${englishPlan.pending.length} 条。`,
  )
}

main().catch((error) => {
  console.error(`[backend-i18n] ${error.message}`)
  process.exitCode = 1
})
