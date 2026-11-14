import assert from 'node:assert/strict'
import test from 'node:test'

import {
  FRONTEND_SKIPPED_SOURCE_BUDGET,
  FRONTEND_TRANSLATION_BUDGET,
  assertTranslationBudget,
  buildCatalogFromSource,
  buildGenerationState,
  extractRichTextTagNames,
  generateTraditionalValue,
  hashText,
  partitionMaintainedPending,
  planEnglishGeneration,
  planTraditionalGeneration,
  resolveSkippedEnglishValue,
  serializeJson,
  traditionalSourceFingerprint,
  validateConfig,
  validateMessageContract,
} from './i18n-generation-core.mjs'
import { I18N_JSON_FILE_MAX_BYTES } from './i18n-safe-files.mjs'

const config = {
  version: 1,
  promptVersion: 1,
  protectedTerms: ['API'],
  englishTerms: { 保存: 'Save' },
  englishOverrides: {},
  traditionalTerms: { 保存: '儲存' },
  traditionalOverrides: {},
}

test('格式化生成物与输入读取共用 4 MiB 字节上限', () => {
  const catalog = Object.fromEntries(
    Array.from({ length: 20_000 }, (_, index) => {
      const prefix = `key-${String(index).padStart(5, '0')}-`
      return [`${prefix}${'x'.repeat(200 - prefix.length)}`, '']
    }),
  )
  const compactBytes = Buffer.byteLength(JSON.stringify(catalog), 'utf8')
  assert.ok(compactBytes < I18N_JSON_FILE_MAX_BYTES)
  assert.throws(
    () => serializeJson(catalog, { label: '测试词典' }),
    new RegExp(`测试词典格式化后为 \\d+ 字节，超过上限 ${I18N_JSON_FILE_MAX_BYTES} 字节`),
  )
})

test('待翻译预算同时限制键数、单条长度与总字符数', () => {
  const pending = [
    { key: 'Example.one', source: '一二三' },
    { key: 'Example.two', source: '四五' },
  ]
  assert.deepEqual(
    assertTranslationBudget(pending, {
      label: '测试',
      maxEntries: 2,
      maxSourceCharacters: 5,
      maxSourceCharactersPerEntry: 3,
    }),
    { entries: 2, sourceCharacters: 5 },
  )
  assert.throws(
    () => assertTranslationBudget(pending, {
      label: '测试',
      maxEntries: 1,
      maxSourceCharacters: 5,
    }),
    /待翻译键数 2 超过上限 1/,
  )
  assert.throws(
    () => assertTranslationBudget(pending, {
      label: '测试',
      maxEntries: 2,
      maxSourceCharacters: 4,
    }),
    /待翻译源文 5 字符超过上限 4/,
  )
  assert.throws(
    () => assertTranslationBudget(pending, {
      label: '测试',
      maxEntries: 2,
      maxSourceCharacters: 5,
      maxSourceCharactersPerEntry: 2,
    }),
    /单条文案 Example.one 的 3 字符超过上限 2/,
  )
})

test('生成配置拒绝空术语、重复术语、未知字段与保留占位符', () => {
  assert.doesNotThrow(() => validateConfig(config))
  assert.throws(
    () => validateConfig({ ...config, protectedTerms: [''] }),
    /长度必须为 2\.\.128/,
  )
  assert.throws(
    () => validateConfig({ ...config, protectedTerms: ['API', 'API'] }),
    /含重复项 API/,
  )
  assert.throws(
    () => validateConfig({ ...config, englishTerms: { API: 'Application API' } }),
    /不得同时出现在 protectedTerms 与 englishTerms/,
  )
  assert.throws(
    () => validateConfig({ ...config, unexpected: true }),
    /字段不合法.*unexpected/,
  )
  assert.throws(
    () => validateConfig({
      ...config,
      englishTerms: { 保存: '__SWAPMIDI18N_TERM_0__' },
    }),
    /保留术语占位符前缀/,
  )
})

test('繁中术语按原始源文一次性最长匹配且对输出设硬上限', () => {
  const safeConfig = {
    ...config,
    traditionalTerms: { 保存: '儲存', 儲存: '不要二次处理' },
  }
  assert.equal(
    generateTraditionalValue('Common.once', '保存儲存', safeConfig, value => value),
    '儲存不要二次处理',
  )

  const expandingConfig = {
    ...config,
    traditionalTerms: { aa: 'x'.repeat(256) },
  }
  assert.throws(
    () => generateTraditionalValue(
      'Common.large',
      'aa'.repeat(1_000),
      expandingConfig,
      value => value,
    ),
    /应用术语后超过 100000 字符上限/,
  )
})

test('英文状态只把新增或简中已变化的键列为待翻译', () => {
  const source = { Common: { save: '保存', api: 'API 地址' } }
  const english = { Common: { save: 'Save', api: 'API URL' } }
  const traditional = { Common: { save: '儲存', api: 'API 地址' } }
  const state = buildGenerationState(source, traditional, english, config)

  const unchanged = planEnglishGeneration(source, english, state, config)
  assert.equal(unchanged.pending.length, 0)
  assert.deepEqual(unchanged.manualEdits, [])

  const changedSource = { Common: { save: '保存修改', api: 'API 地址', cancel: '取消' } }
  const changed = planEnglishGeneration(changedSource, english, state, config)
  assert.deepEqual(
    changed.pending.map(({ key }) => key),
    ['Common.save', 'Common.cancel'],
  )
  assert.equal(changed.preserved.get('Common.api'), 'API URL')
})

test('直接修改英文生成物会被识别，覆盖层则可审计地接管译文', () => {
  const source = { Common: { save: '保存' } }
  const english = { Common: { save: 'Save' } }
  const traditional = { Common: { save: '儲存' } }
  const state = buildGenerationState(source, traditional, english, config)

  const drifted = planEnglishGeneration(source, { Common: { save: 'Persist' } }, state, config)
  assert.deepEqual(drifted.manualEdits, ['Common.save'])
  assert.equal(drifted.preserved.get('Common.save'), 'Persist')

  const overriddenConfig = {
    ...config,
    englishOverrides: { 'Common.save': 'Persist' },
  }
  const overridden = planEnglishGeneration(source, english, state, overriddenConfig)
  assert.equal(overridden.pending.length, 0)
  assert.equal(overridden.preserved.get('Common.save'), 'Persist')
})

test('生成词典保持简中结构与键顺序，繁中使用安全术语并支持逐键覆盖', () => {
  const source = { Dialog: { title: '处理「{name}」', action: '<strong>保存</strong>' } }
  const traditionalConfig = {
    ...config,
    traditionalOverrides: { 'Dialog.action': '<strong>儲存</strong>' },
  }
  const traditional = {
    Dialog: {
      title: generateTraditionalValue('Dialog.title', source.Dialog.title, traditionalConfig, (value) =>
        value.replace('处理', '處理'),
      ),
      action: generateTraditionalValue(
        'Dialog.action',
        source.Dialog.action,
        traditionalConfig,
        (value) => value,
      ),
    },
  }

  assert.deepEqual(traditional, {
    Dialog: { title: '處理「{name}」', action: '<strong>儲存</strong>' },
  })
  assert.deepEqual(
    buildCatalogFromSource(
      source,
      new Map([
        ['Dialog.title', 'Process "{name}"'],
        ['Dialog.action', '<strong>Save</strong>'],
      ]),
    ),
    {
      Dialog: { title: 'Process "{name}"', action: '<strong>Save</strong>' },
    },
  )
})

test('繁中状态保留既有优质译文，只生成简中发生变化的键', () => {
  const source = { Common: { save: '保存', api: 'API 地址' } }
  const traditional = { Common: { save: '儲存', api: 'API 位址' } }
  const english = { Common: { save: 'Save', api: 'API URL' } }
  const state = buildGenerationState(source, traditional, english, config)
  const unchanged = planTraditionalGeneration(source, traditional, state, config, (value) => value)
  assert.deepEqual(unchanged.pending, [])
  assert.equal(unchanged.values.get('Common.api'), 'API 位址')

  const changed = planTraditionalGeneration(
    { Common: { save: '保存修改', api: 'API 地址' } },
    traditional,
    state,
    config,
    (value) => value.replace('修改', '修改'),
  )
  assert.deepEqual(changed.pending, ['Common.save'])
  assert.equal(changed.values.get('Common.api'), 'API 位址')
})

test('同长度中文术语按 Unicode 码点排序，状态哈希不依赖操作系统区域设置', () => {
  const source = '导入和导出'
  const deterministicConfig = {
    ...config,
    traditionalTerms: { 导出: '匯出', 导入: '匯入' },
  }

  assert.equal(
    traditionalSourceFingerprint('Common.transfer', source, deterministicConfig),
    hashText(
      JSON.stringify({
        generatorVersion: 1,
        key: 'Common.transfer',
        source,
        terms: [
          ['导入', '匯入'],
          ['导出', '匯出'],
        ],
        override: null,
      }),
    ),
  )
})

test('翻译不能丢失 ICU 参数或富文本标签', () => {
  assert.deepEqual(extractRichTextTagNames('<strong>{name}</strong><br></br>'), ['br', 'strong'])
  assert.throws(
    () => validateMessageContract('Dialog.title', '处理 {name}', 'Process', 'en'),
    /改变了 ICU 参数/,
  )
  assert.throws(
    () => validateMessageContract('Dialog.title', '<strong>保存</strong>', 'Save', 'en'),
    /改变了富文本标签/,
  )
})

test('平台仅简中键与受维护翻译键拆分预算且安全回退英文目录值', () => {
  const bulkSkipped = Array.from({ length: 1_050 }, (_, index) => ({
    key: `Platform.item${index}`,
    source: `平台文案 ${index}`,
  }))
  const pending = [
    ...bulkSkipped,
    { key: 'PublicPortal.title', source: '公开门户查询' },
  ]

  const { maintainedPending, skippedPending } = partitionMaintainedPending(pending)
  assert.equal(maintainedPending.length, 1)
  assert.equal(skippedPending.length, 1_050)
  assert.doesNotThrow(() =>
    assertTranslationBudget(skippedPending, FRONTEND_SKIPPED_SOURCE_BUDGET),
  )
  assert.doesNotThrow(() =>
    assertTranslationBudget(maintainedPending, FRONTEND_TRANSLATION_BUDGET),
  )

  assert.throws(
    () =>
      assertTranslationBudget(
        [{ key: 'Platform.bad', source: '前缀 __SWAPMIDI18N_TERM_0__' }],
        FRONTEND_SKIPPED_SOURCE_BUDGET,
      ),
    /保留术语占位符前缀/,
  )

  assert.equal(
    resolveSkippedEnglishValue('Platform.saved', '已保存 {count} 项', 'Saved {count} items'),
    'Saved {count} items',
  )
  assert.equal(
    resolveSkippedEnglishValue('Platform.changedArgs', '已保存 {total} 项', 'Saved {count} items'),
    '已保存 {total} 项',
  )
  assert.equal(
    resolveSkippedEnglishValue('Platform.brandNew', '早读 SOP 统计', undefined),
    '早读 SOP 统计',
  )
})
