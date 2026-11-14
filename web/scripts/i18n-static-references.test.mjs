import assert from 'node:assert/strict'
import test from 'node:test'
import { auditDynamicKeyAllowlist } from './i18n-dynamic-key-audit.mjs'
import {
  analyzeI18nReferencesInSource,
  createI18nStaticBatchBudget,
  findMissingStaticReferencesInSource,
  inspectStaticPropertyValuesInSource,
} from './i18n-static-references.mjs'

function assertStaticEvaluationLimit(callback, messagePattern) {
  assert.throws(callback, (error) => {
    assert.equal(error?.name, 'I18nStaticEvaluationLimitError')
    assert.equal(error?.code, 'I18N_STATIC_EVALUATION_LIMIT')
    assert.match(error.message, messagePattern)
    return true
  })
}

function assertStaticBatchLimit(callback, messagePattern) {
  assert.throws(callback, (error) => {
    assert.equal(error?.name, 'I18nStaticBatchLimitError')
    assert.equal(error?.code, 'I18N_STATIC_BATCH_LIMIT')
    assert.match(error.message, messagePattern)
    return true
  })
}

function binaryChoiceFixture(count, expressionFactory) {
  const names = Array.from({ length: count }, (_, index) => `choice${index}`)
  const declarations = names.map((name, index) => (
    `const ${name} = flag${index} ? 'left${index}' : 'right${index}'`
  ))
  return `
    function Component() {
      const t = useTranslations('Example')
      ${declarations.join('\n      ')}
      return t(${expressionFactory(names)})
    }
  `
}

test('同文件不同词法作用域复用 t 时 namespace 不串联', () => {
  const source = `
    function alphaComponent() {
      const t = useTranslations('Alpha')
      t('onlyInBeta')
    }

    async function betaComponent() {
      const t = (await getTranslations('Beta')) as Translator
      t.raw('onlyInBeta')
      t.rich('missingInBeta')
    }
  `
  const knownKeys = new Set(['Alpha.ownKey', 'Beta.onlyInBeta'])

  assert.deepEqual(
    findMissingStaticReferencesInSource(source, knownKeys, 'fixture.tsx')
      .map(({ line, variable, namespace, key }) => ({ line, variable, namespace, key })),
    [
      { line: 4, variable: 't', namespace: 'Alpha', key: 'onlyInBeta' },
      { line: 10, variable: 't', namespace: 'Beta', key: 'missingInBeta' },
    ],
  )
})

test('内层非 translator 同名参数会遮蔽外层 translator', () => {
  const source = `
    const t = useTranslations('Alpha')
    function helper(t) {
      t('notATranslationCall')
    }
    t('valid')
  `

  assert.deepEqual(
    findMissingStaticReferencesInSource(source, new Set(['Alpha.valid']), 'shadow.tsx'),
    [],
  )
})

test('动态键可从条件、useState 字符串联合类型和 const 数组 map 属性展开', () => {
  const source = `
    type ErrorKind = 'notSupported' | 'permissionDenied'
    const PRESETS = [
      {labelKey: 'knownOaQuery'},
      {labelKey: 'knownWmsQuery'},
    ] as const

    function Component() {
      const t = useTranslations('Example')
      const hour = 13
      const greetingKey = hour < 12 ? 'morning' : 'afternoon'
      const [errorKind] = useState<ErrorKind | null>(null)
      t(greetingKey)
      if (errorKind) t(errorKind)
      return PRESETS.map((item) => t(item.labelKey))
    }
  `
  const knownKeys = new Set([
    'Example.morning',
    'Example.afternoon',
    'Example.notSupported',
    'Example.permissionDenied',
    'Example.knownOaQuery',
    'Example.knownWmsQuery',
  ])

  const result = analyzeI18nReferencesInSource(source, knownKeys, 'dynamic.tsx')

  assert.deepEqual(result.missing, [])
  assert.deepEqual(result.unresolvedDynamicReferences, [])
  assert.deepEqual([...result.usedKeys].sort(), [...knownKeys].sort())
  assert.deepEqual(
    result.expandedDynamicReferences.map(({ expression, keys }) => ({ expression, keys })),
    [
      { expression: 'greetingKey', keys: ['afternoon', 'morning'] },
      { expression: 'errorKind', keys: ['notSupported', 'permissionDenied'] },
      { expression: 'item.labelKey', keys: ['knownOaQuery', 'knownWmsQuery'] },
    ],
  )
})

test('无法证明为有限集合的动态键必须进入待审计列表', () => {
  const source = `
    function Component({messageKey}) {
      const t = useTranslations('Example')
      return t(messageKey)
    }
  `

  const result = analyzeI18nReferencesInSource(source, new Set(), 'unresolved.tsx')

  assert.deepEqual(result.missing, [])
  assert.equal(result.unresolvedDynamicReferences.length, 1)
  assert.deepEqual(
    (({ file, line, namespace, expression }) => ({ file, line, namespace, expression }))(
      result.unresolvedDynamicReferences[0],
    ),
    { file: 'unresolved.tsx', line: 4, namespace: 'Example', expression: 'messageKey' },
  )
})

test('动态键展开后的缺失分支也会报错', () => {
  const source = `
    const t = useTranslations('Example')
    const key = ok ? 'present' : 'missing'
    t(key)
  `

  assert.deepEqual(
    findMissingStaticReferencesInSource(source, new Set(['Example.present']), 'missing-dynamic.tsx')
      .map(({ namespace, key, dynamic }) => ({ namespace, key, dynamic })),
    [{ namespace: 'Example', key: 'missing', dynamic: true }],
  )
})

test('不可展开动态键只有完整且仍在使用的审计项才能放行', () => {
  const reference = {
    file: 'feature.tsx',
    line: 7,
    functionName: 'Feature',
    variable: 't',
    method: 'plain',
    namespace: 'Example',
    namespaceExpression: 'Example',
    expression: 'remoteKey',
  }
  const entry = {
    file: 'feature.tsx',
    functionName: 'Feature',
    variable: 't',
    method: 'plain',
    namespace: 'Example',
    expression: 'remoteKey',
    expectedOccurrences: 1,
    fullKeys: ['Example.allowed'],
    reason: '键来自受控后端枚举，协议测试固定全部候选值',
  }
  const knownKeys = new Set(['Example.allowed'])

  assert.deepEqual(auditDynamicKeyAllowlist([reference], [entry], knownKeys), [])
  assert.match(auditDynamicKeyAllowlist([reference], [], knownKeys)[0], /未登记/)
  assert.match(auditDynamicKeyAllowlist([], [entry], knownKeys)[0], /失效/)
  assert.match(
    auditDynamicKeyAllowlist([reference, { ...reference, line: 8 }], [entry], knownKeys).at(-1),
    /调用次数漂移.*expected=1 actual=2/,
  )
  assert.match(
    auditDynamicKeyAllowlist([reference], [{ ...entry, fullKeys: ['Example.missing'] }], knownKeys)[0],
    /不存在的键/,
  )
})

test('显式登记的 translator helper 参数也纳入动态键展开', () => {
  const source = `
    const LABELS = {first: 'labelOne', second: 'labelTwo'} as const
    const ITEMS = ['labelThree', 'labelFour'] as const
    function helper(t, columnId) {
      const labelKey = LABELS[columnId]
      t(labelKey)
      return ITEMS.map((key) => t(key))
    }
  `
  const binding = {
    id: 'fixture-helper-t',
    file: 'helper.ts',
    functionName: 'helper',
    variable: 't',
    namespace: 'Example',
  }
  const knownKeys = new Set([
    'Example.labelOne',
    'Example.labelTwo',
    'Example.labelThree',
    'Example.labelFour',
  ])

  const result = analyzeI18nReferencesInSource(source, knownKeys, 'helper.ts', [binding])

  assert.deepEqual(result.missing, [])
  assert.deepEqual(result.unresolvedDynamicReferences, [])
  assert.deepEqual(
    result.expandedDynamicReferences.map(item => item.keys),
    [
      ['labelOne', 'labelTwo'],
      ['labelFour', 'labelThree'],
    ],
  )
  assert.deepEqual([...result.usedTranslatorBindingIds], ['fixture-helper-t'])
})

test('未登记命名空间的 helper t 调用无论字面量或动态键都不得静默跳过', () => {
  const source = `
    function helper(t, remoteKey) {
      t('literalKey')
      return t(remoteKey)
    }
  `

  const result = analyzeI18nReferencesInSource(source, new Set(), 'helper.ts')

  assert.equal(result.unresolvedDynamicReferences.length, 2)
  assert.equal(result.unresolvedDynamicReferences[0].namespace, null)
  assert.equal(result.unresolvedDynamicReferences[0].expression, "'literalKey'")
  assert.equal(result.unresolvedDynamicReferences[1].expression, 'remoteKey')
})

test('helper translator 可由同一函数全部相对键的 namespace 交集推断', () => {
  const source = `
    function helper(t) {
      t('common.save')
      return t('role.name')
    }
  `
  const knownKeys = new Set([
    'Account.common.save',
    'Enterprise.common.save',
    'Account.role.name',
  ])
  const result = analyzeI18nReferencesInSource(source, knownKeys, 'helper.ts')

  assert.deepEqual(result.unresolvedDynamicReferences, [])
  assert.deepEqual([...result.usedKeys].sort(), ['Account.common.save', 'Account.role.name'])
  assert.deepEqual(result.inferredTranslatorReferences[0].namespaces, ['Account'])
})

test('7634 键下大量 helper 调用只构建一次有界后缀索引', { timeout: 5_000 }, () => {
  const helperCalls = Array.from({ length: 2_000 }, () => "t('common.save')").join('\n      ')
  const source = `
    function helper(t) {
      ${helperCalls}
    }
  `
  const knownKeys = new Set([
    'Account.common.save',
    ...Array.from(
      { length: 7_633 },
      (_, index) => `Filler${index}.section${index}.item${index}`,
    ),
  ])
  const startedAt = Date.now()

  const result = analyzeI18nReferencesInSource(source, knownKeys, 'many-helper-calls.ts')

  assert.deepEqual(result.unresolvedDynamicReferences, [])
  assert.equal(result.inferredTranslatorReferences.length, 2_000)
  assert.deepEqual([...result.usedKeys], ['Account.common.save'])
  assert.ok(Date.now() - startedAt < 2_000, 'helper namespace 推断不得退化为 knownKeys × 调用数扫描')
})

test('helper namespace 后缀索引关联量超过预算时 hard fail', () => {
  const knownKeys = new Set(Array.from(
    { length: 8_193 },
    (_, index) => `Namespace${index}.shared`,
  ))
  const source = `
    function helper(t) {
      return t('shared')
    }
  `

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, knownKeys, 'namespace-index-budget.ts'),
    /namespace 后缀索引关联量硬上限 8192/,
  )
})

test('25 个二元联合字符串拼接在笛卡尔积物化前快速 hard fail', { timeout: 2_000 }, () => {
  const source = binaryChoiceFixture(25, names => names.join(' + '))
  const startedAt = Date.now()

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, new Set(), 'binary-explosion.tsx'),
    /单表达式候选数硬上限 2048.*字符串二元拼接笛卡尔积/,
  )
  assert.ok(Date.now() - startedAt < 1_000, '组合爆炸必须在 1 秒内被前置容量检查拒绝')
})

test('24 个二元联合模板插值在笛卡尔积物化前 hard fail', { timeout: 2_000 }, () => {
  const source = binaryChoiceFixture(
    24,
    names => '`' + names.map(name => '${' + name + '}').join('') + '`',
  )

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, new Set(), 'template-explosion.tsx'),
    /单表达式候选数硬上限 2048.*模板表达式拼接笛卡尔积/,
  )
})

test('联合类型 Set 合并在超过单表达式候选上限前 hard fail', () => {
  const variants = Array.from({ length: 2_049 }, (_, index) => `'key${index}'`).join(' | ')
  const source = `
    type HugeKey = ${variants}
    function Component() {
      const t = useTranslations('Example')
      const [key] = useState<HugeKey>('key0')
      return t(key)
    }
  `

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, new Set(), 'union-explosion.tsx'),
    /单表达式候选数硬上限 2048.*字符串联合类型集合合并/,
  )
})

test('单文件重复静态求值超过操作预算时 hard fail', () => {
  const repeatedVariants = Array.from({ length: 1_024 }, () => "'same'").join(' | ')
  const calls = Array.from({ length: 10 }, () => 't(key)').join('\n      ')
  const source = `
    type RepeatedKey = ${repeatedVariants}
    function Component() {
      const t = useTranslations('Example')
      const [key] = useState<RepeatedKey>('same')
      ${calls}
    }
  `

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, new Set(['Example.same']), 'operation-budget.tsx'),
    /单文件求值操作硬上限 20000/,
  )
})

test('单文件静态引用展开超过引用量预算时 hard fail', () => {
  const keys = Array.from({ length: 2_048 }, (_, index) => `key${index}`)
  const variants = keys.map(key => `'${key}'`).join(' | ')
  const source = `
    type ManyKeys = ${variants}
    function Component() {
      const t = useTranslations('Example')
      const [key] = useState<ManyKeys>('key0')
      t(key)
      t(key)
      t(key)
    }
  `
  const knownKeys = new Set(keys.map(key => `Example.${key}`))

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, knownKeys, 'reference-budget.tsx'),
    /单文件静态引用量硬上限 4096/,
  )
})

test('两个文件各自低于单文件上限时仍共享全批次引用预算', () => {
  const batch = createI18nStaticBatchBudget({ references: 1 })
  const knownKeys = new Set(['Example.one', 'Example.two'])
  analyzeI18nReferencesInSource(
    "const t = useTranslations('Example'); t('one')",
    knownKeys,
    'first.ts',
    [],
    batch,
  )

  assertStaticBatchLimit(
    () => analyzeI18nReferencesInSource(
      "const t = useTranslations('Example'); t('two')",
      knownKeys,
      'second.ts',
      [],
      batch,
    ),
    /全批次静态引用量硬上限 1.*second\.ts/,
  )
})

test('helper namespace 后缀索引操作按跨文件批次累计', () => {
  const batch = createI18nStaticBatchBudget({ namespaceIndexOperations: 3 })
  const source = "function helper(t) { return t('shared') }"
  const knownKeys = new Set(['Example.shared'])
  analyzeI18nReferencesInSource(source, knownKeys, 'first-helper.ts', [], batch)

  assertStaticBatchLimit(
    () => analyzeI18nReferencesInSource(source, knownKeys, 'second-helper.ts', [], batch),
    /全批次namespace 后缀索引操作硬上限 3.*second-helper\.ts/,
  )
})

test('固定动态键属性在第 257 个唯一值物化前硬失败', () => {
  const source = `export const items = [${Array.from(
    { length: 257 },
    (_, index) => `{ titleKey: 'item${index}' }`,
  ).join(',')}]`

  assert.throws(
    () => inspectStaticPropertyValuesInSource(source, 'titleKey', 'nav-config.ts'),
    (error) => {
      assert.equal(error?.name, 'I18nStaticPropertyLimitError')
      assert.equal(error?.code, 'I18N_STATIC_PROPERTY_LIMIT')
      assert.match(error.message, /唯一静态值不得超过 256/)
      return true
    },
  )
})

test('helper namespace 交集扫描计入单文件操作预算', () => {
  const knownKeys = new Set(
    Array.from({ length: 64 }, (_, index) => `Namespace${index}.shared`),
  )
  const calls = Array.from({ length: 400 }, () => "t('shared')").join('\n')
  const source = `function render(t) { ${calls} }`

  assertStaticEvaluationLimit(
    () => analyzeI18nReferencesInSource(source, knownKeys, 'helper-intersection.ts'),
    /单文件求值操作硬上限 20000.*候选 namespace 求交/,
  )
})
