import { existsSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import * as OpenCC from 'opencc-js'

import {
  FRONTEND_SKIPPED_SOURCE_BUDGET,
  FRONTEND_TRANSLATION_BUDGET,
  assertTranslationBudget,
  assertStringLeaves,
  buildCatalogFromSource,
  buildGenerationState,
  isMaintainedTranslationKey,
  partitionMaintainedPending,
  planEnglishGeneration,
  planTraditionalGeneration,
  resolveSkippedEnglishValue,
  serializeJson,
  validateConfig,
  validateMessageContract,
} from './i18n-generation-core.mjs'
import { flattenMessages } from './i18n-message-contracts.mjs'
import { translateEnglish } from './i18n-english-translator.mjs'
import {
  I18N_JSON_FILE_MAX_BYTES,
  readRegularFileBoundedSync,
  writeFileAtomic,
} from './i18n-safe-files.mjs'

const ROOT = process.env.I18N_WORKSPACE_ROOT
  ? resolve(process.env.I18N_WORKSPACE_ROOT)
  : resolve(dirname(fileURLToPath(import.meta.url)), '..')
const PATHS = {
  source: resolve(ROOT, 'messages/zh-Hans.json'),
  traditional: resolve(ROOT, 'messages/zh-Hant.json'),
  english: resolve(ROOT, 'messages/en.json'),
  config: resolve(ROOT, 'i18n/generation-config.json'),
  state: resolve(ROOT, 'i18n/translation-state.json'),
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

async function main() {
  const check = process.argv.includes('--check')
  const bootstrap = process.argv.includes('--bootstrap')
  const validateInput = process.argv.includes('--validate-input')
  if ([check, bootstrap, validateInput].filter(Boolean).length > 1) {
    throw new Error('--check、--bootstrap 与 --validate-input 只能使用一个')
  }

  const source = readJson(PATHS.source, '简中词典')
  const traditional = readJson(PATHS.traditional, '繁中词典')
  const english = readJson(PATHS.english, '英文词典')
  const config = readJson(PATHS.config, '生成配置')
  validateConfig(config)
  assertStringLeaves(source, 'zh-Hans')
  assertStringLeaves(traditional, 'zh-Hant')
  assertStringLeaves(english, 'en')

  // 只做繁体字形转换；台湾词组转换会把“内存在”误判成“记忆体在”。
  // 需要的地区术语由 generation-config.json 中的显式词表处理。
  const convert = OpenCC.Converter({ from: 'cn', to: 'tw' })

  if (bootstrap) {
    const state = buildGenerationState(source, traditional, english, config)
    writeFileAtomic(
      PATHS.state,
      serializeJson(state, { label: 'i18n/translation-state.json' }),
    )
    console.log(
      `[i18n] 已接管现有英文词典并生成状态（${Object.keys(state.entries).length} 个键）；后续只翻译新增或变更项。`,
    )
    return
  }

  if (!existsSync(PATHS.state)) {
    throw new Error('缺少 i18n/translation-state.json；首次接管请运行 pnpm i18n:bootstrap')
  }
  const state = readJson(PATHS.state, '英文生成状态')
  const traditionalPlan = planTraditionalGeneration(source, traditional, state, config, convert)
  const maintainedTraditionalEdits = traditionalPlan.manualEdits.filter(isMaintainedTranslationKey)
  if (maintainedTraditionalEdits.length > 0) {
    throw new Error(
      '检测到繁中生成物被直接修改。请把确需人工锁定的译文放入 generation-config.json 的 traditionalOverrides：' +
        maintainedTraditionalEdits.slice(0, 20).join(', '),
    )
  }
  const generatedTraditional = buildCatalogFromSource(source, traditionalPlan.values)
  const traditionalContent = serializeJson(generatedTraditional, {
    label: 'messages/zh-Hant.json',
  })

  const plan = planEnglishGeneration(source, english, state, config)
  const maintainedEnglishEdits = plan.manualEdits.filter(isMaintainedTranslationKey)
  if (maintainedEnglishEdits.length > 0) {
    throw new Error(
      '检测到英文生成物被直接修改。请把确需人工锁定的译文放入 generation-config.json 的 englishOverrides：' +
        maintainedEnglishEdits.slice(0, 20).join(', '),
    )
  }

  const { maintainedPending, skippedPending } = partitionMaintainedPending(plan.pending)
  const skippedBudget = assertTranslationBudget(skippedPending, FRONTEND_SKIPPED_SOURCE_BUDGET)
  const budget = assertTranslationBudget(maintainedPending, FRONTEND_TRANSLATION_BUDGET)
  if (validateInput) {
    console.log(
      `[i18n] 翻译输入有效：待翻译 ${budget.entries} 条 / ${budget.sourceCharacters} 字符，` +
        `仅简中免译 ${skippedBudget.entries} 条 / ${skippedBudget.sourceCharacters} 字符，未越过预算。`,
    )
    return
  }

  if (check) {
    if (maintainedPending.length > 0) {
      throw new Error(
        `产品查询英文词典有 ${maintainedPending.length} 个新增或变更键尚未生成：` +
          maintainedPending
            .slice(0, 10)
            .map(({ key }) => key)
            .join(', '),
      )
    }
    console.log(
      `[i18n] 产品查询译文无待生成项。平台其余 ${skippedPending.length} 个新增或变更键不再要求繁中/英文。`,
    )
    return
  }

  if (maintainedPending.length > 0) {
    const translated = await translateEnglish(maintainedPending, config, {
      onProgress: ({ current, total, count }) =>
        console.log(`[i18n] 正在生成英文 ${current}/${total}（本批 ${count} 条）`),
    })
    for (const { key, source: sourceValue } of maintainedPending) {
      const value = translated.get(key)
      validateMessageContract(key, sourceValue, value, 'en')
      plan.preserved.set(key, value)
    }
  }
  if (skippedPending.length > 0) {
    const englishMessages = flattenMessages(english)
    for (const { key, source: sourceValue } of skippedPending) {
      plan.preserved.set(
        key,
        resolveSkippedEnglishValue(key, sourceValue, englishMessages.get(key)),
      )
    }
  }

  const generatedEnglish = buildCatalogFromSource(source, plan.preserved)
  const generatedState = buildGenerationState(source, generatedTraditional, generatedEnglish, config)
  const englishContent = serializeJson(generatedEnglish, { label: 'messages/en.json' })
  const stateContent = serializeJson(generatedState, { label: 'i18n/translation-state.json' })
  writeFileAtomic(PATHS.traditional, traditionalContent)
  writeFileAtomic(PATHS.english, englishContent)
  writeFileAtomic(PATHS.state, stateContent)
  console.log(
    `[i18n] 同步完成：繁中新增/变更 ${traditionalPlan.pending.length} 条；` +
      `英文翻译 ${maintainedPending.length} 条（仅简中免译 ${skippedPending.length} 条）；总计 ${plan.preserved.size} 条。`,
  )
}

main().catch((error) => {
  console.error(`[i18n] ${error.message}`)
  process.exitCode = 1
})
