import assert from 'node:assert/strict'
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'

import {
  I18N_AUDIT_REGISTRY_LIMITS,
  loadI18nAuditRegistry,
} from './i18n-audit-registry.mjs'

function validRegistry(expression = 'remoteKey') {
  return {
    version: 1,
    dynamicKeyAllowlist: [
      {
        file: 'feature/client.tsx',
        functionName: 'Feature',
        variable: 't',
        method: 'plain',
        namespace: 'Example',
        expression,
        expectedOccurrences: 1,
        fullKeys: ['Example.allowed'],
        reason: '候选键由受控协议枚举固定，运行时只能落在该闭集内',
      },
    ],
    translatorBindingAllowlist: [
      {
        id: 'feature-helper-t',
        file: 'feature/helper.ts',
        functionName: 'renderLabel',
        variable: 't',
        namespace: 'Example',
        reason: 'translator 由组件显式注入纯函数，普通函数不能直接调用 hook',
      },
    ],
  }
}

function createWorkspace(contents = validRegistry()) {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-registry-'))
  mkdirSync(join(root, 'i18n'))
  writeFileSync(
    join(root, 'i18n', 'reference-audit-registry.json'),
    typeof contents === 'string' ? contents : `${JSON.stringify(contents, null, 2)}\n`,
    'utf8',
  )
  return root
}

async function assertRejected(contents, pattern) {
  const root = createWorkspace(contents)
  try {
    await assert.rejects(loadI18nAuditRegistry(root), pattern)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

test('注册表从调用方 workspace 读取，同 PR 新登记立即可见', async () => {
  const firstRoot = createWorkspace(validRegistry('firstKey'))
  const secondRoot = createWorkspace(validRegistry('samePrKey'))
  try {
    const [first, second] = await Promise.all([
      loadI18nAuditRegistry(firstRoot),
      loadI18nAuditRegistry(secondRoot),
    ])
    assert.equal(first.dynamicKeyAllowlist[0].expression, 'firstKey')
    assert.equal(second.dynamicKeyAllowlist[0].expression, 'samePrKey')
    assert.equal(Object.isFrozen(second.dynamicKeyAllowlist), true)
    assert.equal(Object.isFrozen(second.dynamicKeyAllowlist[0]), true)
  } finally {
    rmSync(firstRoot, { recursive: true, force: true })
    rmSync(secondRoot, { recursive: true, force: true })
  }
})

test('PR 中伪造旧式可执行 allowlist 不会被加载', async () => {
  const root = createWorkspace()
  const sentinel = join(root, 'executed.txt')
  mkdirSync(join(root, 'scripts'))
  writeFileSync(
    join(root, 'scripts', 'i18n-dynamic-key-allowlist.mjs'),
    `import { writeFileSync } from 'node:fs'; writeFileSync(${JSON.stringify(sentinel)}, 'executed')`,
    'utf8',
  )
  try {
    await loadI18nAuditRegistry(root)
    assert.equal(existsSync(sentinel), false)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('拒绝非法 JSON、未知字段和版本漂移', async () => {
  await assertRejected('{not-json', /不是合法 JSON/)
  await assertRejected(
    '{"version":1,"dynamicKeyAllowlist":[],"translatorBindingAllowlist":[],"__proto__":{}}',
    /未知字段 "__proto__"/,
  )
  await assertRejected({ ...validRegistry(), version: 2 }, /version 必须严格等于 1/)
})

test('动态键条目必须显式枚举 fullKeys 与精确调用次数', async () => {
  const missingKeys = validRegistry()
  delete missingKeys.dynamicKeyAllowlist[0].fullKeys
  await assertRejected(missingKeys, /缺少字段 fullKeys/)

  const invalidOccurrences = validRegistry()
  invalidOccurrences.dynamicKeyAllowlist[0].expectedOccurrences = 0
  await assertRejected(invalidOccurrences, /expectedOccurrences 必须是 1\.\.16/)

  const keySource = validRegistry()
  keySource.dynamicKeyAllowlist[0].keySource = { file: 'feature/config.ts', property: 'labelKey' }
  await assertRejected(keySource, /未知字段 "keySource"/)
})

test('拒绝绝对路径、路径穿越、反斜杠与非源码扩展名', async () => {
  const paths = ['../secret.ts', '/tmp/secret.ts', 'C:/secret.ts', 'feature\\client.tsx', 'feature/data.json']
  for (const path of paths) {
    const registry = validRegistry()
    registry.dynamicKeyAllowlist[0].file = path
    await assertRejected(registry, /POSIX 相对路径|只允许 \.ts\/\.tsx/)
  }
})

test('fullKeys 必须唯一、规范且按字典序排列', async () => {
  const unsorted = validRegistry()
  unsorted.dynamicKeyAllowlist[0].fullKeys = ['Example.beta', 'Example.alpha']
  await assertRejected(unsorted, /必须按字典序排序/)

  const invalidKey = validRegistry()
  invalidKey.dynamicKeyAllowlist[0].fullKeys = ['Example.allowed\nforged']
  await assertRejected(invalidKey, /不得含控制字符/)
})

test('拒绝重复动态项、重复 fullKeys 与重复 translator id', async () => {
  const duplicateEntry = validRegistry()
  duplicateEntry.dynamicKeyAllowlist.push({ ...duplicateEntry.dynamicKeyAllowlist[0] })
  await assertRejected(duplicateEntry, /dynamicKeyAllowlist 含重复项/)

  const duplicateKey = validRegistry()
  duplicateKey.dynamicKeyAllowlist[0].fullKeys.push('Example.allowed')
  await assertRejected(duplicateKey, /含重复键 Example.allowed/)

  const duplicateBinding = validRegistry()
  duplicateBinding.translatorBindingAllowlist.push({
    ...duplicateBinding.translatorBindingAllowlist[0],
  })
  await assertRejected(duplicateBinding, /translatorBindingAllowlist 含重复项/)

  const duplicateSelector = validRegistry()
  duplicateSelector.translatorBindingAllowlist.push({
    ...duplicateSelector.translatorBindingAllowlist[0],
    id: 'different-id-same-selector',
  })
  await assertRejected(duplicateSelector, /translatorBindingAllowlist selector 含重复项/)
})

test('拒绝控制字符、过短理由与未知嵌套字段', async () => {
  const control = validRegistry('remote\nkey')
  await assertRejected(control, /不得含控制字符/)

  const shortReason = validRegistry()
  shortReason.dynamicKeyAllowlist[0].reason = '太短'
  await assertRejected(shortReason, /长度必须在 10/)

  const unknown = validRegistry()
  unknown.dynamicKeyAllowlist[0].command = 'node malicious.mjs'
  await assertRejected(unknown, /未知字段 "command"/)
})

test('拒绝重复 JSON 字段造成的审阅歧义', async () => {
  const canonical = JSON.stringify(validRegistry(), null, 2)
  const duplicate = `${canonical.replace('"version": 1', '"version": 2,\n  "version": 1')}\n`
  await assertRejected(duplicate, /无重复字段/)
})

test('在 JSON.parse 前拒绝超大注册表', async () => {
  await assertRejected(
    ' '.repeat(I18N_AUDIT_REGISTRY_LIMITS.bytes + 1),
    /注册表不得超过 131072 字节/,
  )
})

test('拒绝以符号链接替代注册表', async (context) => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-registry-link-'))
  mkdirSync(join(root, 'i18n'))
  const real = join(root, 'real.json')
  const link = join(root, 'i18n', 'reference-audit-registry.json')
  writeFileSync(real, `${JSON.stringify(validRegistry(), null, 2)}\n`, 'utf8')
  try {
    try {
      symlinkSync(real, link, 'file')
    } catch (error) {
      if (error.code === 'EPERM') {
        context.skip('当前 Windows 策略不允许创建符号链接')
        return
      }
      throw error
    }
    await assert.rejects(loadI18nAuditRegistry(root), /不得是符号链接/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})
