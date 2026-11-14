import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const checker = fileURLToPath(new URL('./check-i18n-keys.mjs', import.meta.url))

function registry() {
  return {
    version: 1,
    dynamicKeyAllowlist: [
      {
        file: 'lib/nav-utils.ts',
        functionName: 'translateNavTitle',
        variable: 't',
        method: 'plain',
        namespace: 'Nav',
        expression: 'item.titleKey',
        expectedOccurrences: 1,
        fullKeys: ['Nav.items.allowed'],
        reason: '导航标题候选键由固定 nav-config 测试夹具静态供给',
      },
      {
        file: 'lib/nav-utils.ts',
        functionName: 'translateNavDescription',
        variable: 't',
        method: 'plain',
        namespace: 'Nav',
        expression: 'domain.descriptionKey',
        expectedOccurrences: 1,
        fullKeys: ['Nav.domains.allowed.description'],
        reason: '导航描述候选键由固定 nav-config 测试夹具静态供给',
      },
      {
        file: 'feature/helper.ts',
        functionName: 'renderLabel',
        variable: 't',
        method: 'plain',
        namespace: 'Example',
        expression: 'remoteKey',
        expectedOccurrences: 1,
        fullKeys: ['Example.allowed'],
        reason: '测试夹具用受控闭集模拟同一 PR 新增的动态翻译键契约',
      },
    ],
    translatorBindingAllowlist: [
      {
        id: 'fixture-nav-t',
        file: 'lib/nav-utils.ts',
        variable: 't',
        namespace: 'Nav',
        reason: '测试夹具模拟调用方把 Nav translator 注入导航纯函数',
      },
      {
        id: 'fixture-helper-t',
        file: 'feature/helper.ts',
        functionName: 'renderLabel',
        variable: 't',
        namespace: 'Example',
        reason: '测试夹具由调用方显式注入 Example translator 到纯函数',
      },
    ],
  }
}

function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
}

function writeRegistry(root, value) {
  writeJson(join(root, 'i18n', 'reference-audit-registry.json'), value)
}

function writeMessages(root, { includeAddedNavKey = false } = {}) {
  const messages = {
    Example: { allowed: '允许', static: '静态' },
    Nav: {
      domains: { allowed: { description: '允许的描述' } },
      items: {
        allowed: '允许的导航项',
        ...(includeAddedNavKey ? { added: '新增导航项' } : {}),
      },
    },
  }
  for (const locale of ['zh-Hans', 'zh-Hant', 'en']) {
    writeJson(join(root, 'messages', `${locale}.json`), messages)
  }
}

function writeNavConfig(root, titleKeys = ['items.allowed']) {
  mkdirSync(join(root, 'src', 'components', 'layout'), { recursive: true })
  writeFileSync(
    join(root, 'src', 'components', 'layout', 'nav-config.ts'),
    `export const nav = [${titleKeys.map(titleKey => `{ titleKey: ${JSON.stringify(titleKey)} }`).join(', ')}]\n`
      + "export const domains = [{ descriptionKey: 'domains.allowed.description' }]\n",
    'utf8',
  )
}

function createPrWorkspace() {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-pr-input-'))
  writeMessages(root)
  mkdirSync(join(root, 'src', 'feature'), { recursive: true })
  mkdirSync(join(root, 'src', 'lib'), { recursive: true })
  writeFileSync(
    join(root, 'src', 'feature', 'client.ts'),
    "export function Feature() { const t = useTranslations('Example'); return t('static') }\n",
    'utf8',
  )
  writeFileSync(
    join(root, 'src', 'feature', 'helper.ts'),
    'export function renderLabel(t, remoteKey) { return t(remoteKey) }\n',
    'utf8',
  )
  writeFileSync(
    join(root, 'src', 'lib', 'nav-utils.ts'),
    'export function translateNavTitle(t, item) { return t(item.titleKey) }\n'
      + 'export function translateNavDescription(t, domain) { return t(domain.descriptionKey) }\n',
    'utf8',
  )
  writeNavConfig(root)
  writeRegistry(root, registry())
  return root
}

function runChecker(root, ...args) {
  return spawnSync(process.execPath, [checker, ...args], {
    cwd: dirname(checker),
    encoding: 'utf8',
    env: { ...process.env, I18N_WORKSPACE_ROOT: root },
    timeout: 30_000,
  })
}

test('可信 checker 完整消费同 PR 注册表且不执行 PR scripts', () => {
  const root = createPrWorkspace()
  const sentinel = join(root, 'executed.txt')
  mkdirSync(join(root, 'scripts'))
  writeFileSync(
    join(root, 'scripts', 'i18n-dynamic-key-allowlist.mjs'),
    `import { writeFileSync } from 'node:fs'; writeFileSync(${JSON.stringify(sentinel)}, 'executed')`,
    'utf8',
  )
  try {
    const result = runChecker(root)
    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
    assert.match(result.stdout, /i18n 契约有效/)
    assert.equal(existsSync(sentinel), false)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('移除同 PR 动态键登记时可信硬门禁失败', () => {
  const root = createPrWorkspace()
  const value = registry()
  value.dynamicKeyAllowlist = []
  writeRegistry(root, value)
  try {
    const result = runChecker(root, '--validate-audit-input')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /未登记的动态翻译键/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('移除同 PR translator 绑定时可信硬门禁失败', () => {
  const root = createPrWorkspace()
  const value = registry()
  value.translatorBindingAllowlist = []
  writeRegistry(root, value)
  try {
    const result = runChecker(root, '--validate-audit-input')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /动态 namespace 禁止使用|失效的动态键审计项/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('PR 源码树中的符号链接在读取前被可信 checker 拒绝', (context) => {
  const root = createPrWorkspace()
  const outside = join(root, 'outside.ts')
  const link = join(root, 'src', 'feature', 'linked.ts')
  writeFileSync(outside, 'export const secret = true\n', 'utf8')
  try {
    try {
      symlinkSync(outside, link, 'file')
    } catch (error) {
      if (error.code === 'EPERM') {
        context.skip('当前 Windows 策略不允许创建符号链接')
        return
      }
      throw error
    }
    const result = runChecker(root, '--validate-audit-input')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /源码树.*符号链接/)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('导航新增缺失键但未同步注册表时可信硬门禁失败', () => {
  const root = createPrWorkspace()
  writeNavConfig(root, ['items.allowed', 'items.missing'])
  try {
    const result = runChecker(root, '--validate-audit-input')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /固定动态键来源.*未登记.*Nav\.items\.missing/s)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('导航新增合法词典键但注册表快照未同步时可信硬门禁失败', () => {
  const root = createPrWorkspace()
  writeMessages(root, { includeAddedNavKey: true })
  writeNavConfig(root, ['items.allowed', 'items.added'])
  try {
    const result = runChecker(root, '--validate-audit-input')
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /固定动态键来源.*未登记.*Nav\.items\.added/s)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})
