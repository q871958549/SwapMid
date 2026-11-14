import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import test from 'node:test'

import { mkdtempSync, mkdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

import {
  collectCSharpSourceFiles,
  extractUserFriendlyExceptionArguments,
  readCSharpSource,
} from './check-backend-i18n.mjs'

const checker = fileURLToPath(new URL('./check-backend-i18n.mjs', import.meta.url))

function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`, 'utf8')
}

function createBackendFixture() {
  const root = mkdtempSync(join(tmpdir(), 'backend-i18n-input-'))
  const domainRoot = join(root, 'src', 'SwapMid.Domain.Shared')
  const resourceRoot = join(domainRoot, 'Localization', 'SwapMid')
  const i18nRoot = join(domainRoot, 'I18n')
  mkdirSync(resourceRoot, { recursive: true })
  writeJson(join(resourceRoot, 'zh-Hans.json'), {
    culture: 'zh-Hans',
    texts: { 'SwapMid:Fixture': '夹具错误' },
  })
  // 故意让可再生语言缺键：--validate-input 只应冻结生成器无法修复的事实源契约。
  writeJson(join(resourceRoot, 'zh-Hant.json'), { culture: 'zh-Hant', texts: {} })
  writeJson(join(resourceRoot, 'en.json'), { culture: 'en', texts: {} })
  writeJson(join(i18nRoot, 'user-friendly-exception-baseline.json'), {
    version: 1,
    total: 0,
    expressions: {},
  })
  writeFileSync(
    join(domainRoot, 'SwapMidDomainErrorCodes.cs'),
    'public static class ErrorCodes { public const string Fixture = "SwapMid:Fixture"; }\n',
    'utf8',
  )
  writeFileSync(
    join(domainRoot, 'SwapMidDomainSharedModule.cs'),
    'new LanguageInfo("zh-Hans"); new LanguageInfo("zh-Hant"); new LanguageInfo("en");\n'
      + 'options.Resources.Get<SwapMidResource>().Add<SwapMidResource>("zh-Hans");\n',
    'utf8',
  )
  return { root, resourceRoot }
}

function runBackendFixture(root, args = ['--validate-input']) {
  return spawnSync(process.execPath, [checker, ...args], {
    encoding: 'utf8',
    env: { ...process.env, I18N_REPOSITORY_ROOT: root },
    timeout: 30_000,
  })
}

test('领域错误码 partial 文件与入口文件一起作为常量来源', () => {
  const { root } = createBackendFixture()
  try {
    writeFileSync(
      join(root, 'src', 'SwapMid.Domain.Shared', 'SwapMidDomainErrorCodes.cs'),
      'public static partial class SwapMidDomainErrorCodes { }\n',
      'utf8',
    )
    writeFileSync(
      join(root, 'src', 'SwapMid.Domain.Shared', 'SwapMidDomainErrorCodes.Fixture.cs'),
      'public static partial class SwapMidDomainErrorCodes { public const string Fixture = "SwapMid:Fixture"; }\n',
      'utf8',
    )
    const result = runBackendFixture(root)
    assert.equal(result.status, 0, result.stderr || result.stdout)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('提取 UserFriendlyException 参数时理解嵌套调用、插值字符串与多行表达式', () => {
  const source = `
    throw new UserFriendlyException("固定文案");
    return new UserFriendlyException(
      $"不支持「{extension}」，允许：{string.Join("、", allowed.OrderBy(x => x))}"
    );
  `

  assert.deepEqual(extractUserFriendlyExceptionArguments(source), [
    '"固定文案"',
    '$"不支持「{extension}」，允许：{string.Join("、", allowed.OrderBy(x => x))}"',
  ])
})

test('不把名称相似的普通方法误计为 UserFriendlyException', () => {
  assert.deepEqual(
    extractUserFriendlyExceptionArguments(`BuildUserFriendlyException("x");`),
    [],
  )
})

test('单文件第 5001 个 UserFriendlyException 在物化前硬失败', () => {
  const source = Array.from(
    { length: 5_001 },
    (_, index) => `new UserFriendlyException("fixture-${index}");`,
  ).join('\n')
  assert.throws(
    () => extractUserFriendlyExceptionArguments(source),
    /UserFriendlyException 总数超过上限 5000/,
  )
})

test('后端源码扫描拒绝符号链接且不会跟随到源码根之外', (context) => {
  const fixture = mkdtempSync(join(tmpdir(), 'backend-i18n-symlink-'))
  const sourceRoot = join(fixture, 'src')
  const outside = join(fixture, 'outside')
  mkdirSync(sourceRoot)
  mkdirSync(outside)
  writeFileSync(join(outside, 'Secret.cs'), 'secret')

  try {
    try {
      symlinkSync(outside, join(sourceRoot, 'linked'), 'junction')
    } catch (error) {
      if (error?.code === 'EPERM') {
        context.skip('当前 Windows 环境不允许创建测试符号链接')
        return
      }
      throw error
    }

    assert.throws(
      () => collectCSharpSourceFiles(sourceRoot),
      /不得包含符号链接/,
    )
  } finally {
    rmSync(fixture, { recursive: true, force: true })
  }
})

test('后端源码扫描在目录项预算耗尽时硬失败', () => {
  const fixture = mkdtempSync(join(tmpdir(), 'backend-i18n-budget-'))
  const sourceRoot = join(fixture, 'src')
  mkdirSync(sourceRoot)
  writeFileSync(join(sourceRoot, 'One.cs'), 'class One {}')
  writeFileSync(join(sourceRoot, 'Two.cs'), 'class Two {}')

  try {
    assert.throws(
      () => collectCSharpSourceFiles(sourceRoot, {
        maxFiles: 10,
        maxDirectories: 10,
        maxDirectoryEntries: 1,
        maxFileBytes: 1024,
        maxTotalBytes: 2048,
      }),
      /目录项超过上限 1/,
    )
  } finally {
    rmSync(fixture, { recursive: true, force: true })
  }
})

test('后端源码实际读取使用批次剩余额度作为下一文件上限', () => {
  const fixture = mkdtempSync(join(tmpdir(), 'backend-i18n-read-budget-'))
  const first = join(fixture, 'First.cs')
  const second = join(fixture, 'Second.cs')
  writeFileSync(first, '123')
  writeFileSync(second, '456')
  const budget = { totalBytes: 0 }
  const limits = { maxFileBytes: 10, maxTotalBytes: 5 }
  try {
    assert.equal(readCSharpSource(first, budget, limits), '123')
    assert.throws(
      () => readCSharpSource(second, budget, limits),
      /实际读取总量超过上限 5 字节/,
    )
  } finally {
    rmSync(fixture, { recursive: true, force: true })
  }
})

test('后端源码在总预算恰好耗尽后仍允许空文件', () => {
  const fixture = mkdtempSync(join(tmpdir(), 'backend-i18n-empty-read-budget-'))
  const full = join(fixture, 'Full.cs')
  const empty = join(fixture, 'Empty.cs')
  writeFileSync(full, '1')
  writeFileSync(empty, '')
  const budget = { totalBytes: 0 }
  const limits = { maxFileBytes: 1, maxTotalBytes: 1 }
  try {
    assert.equal(readCSharpSource(full, budget, limits), '1')
    assert.equal(readCSharpSource(empty, budget, limits), '')
    assert.equal(budget.totalBytes, 1)
  } finally {
    rmSync(fixture, { recursive: true, force: true })
  }
})

test('后端输入门禁允许可再生语言漂移但验证简中事实源契约', () => {
  const fixture = createBackendFixture()
  try {
    const result = runBackendFixture(fixture.root)
    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
    assert.match(result.stdout, /不可生成输入契约有效/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('完整验收允许后台英文繁中缺键，简中事实源与错误码仍完整', () => {
  const fixture = createBackendFixture()
  try {
    const result = runBackendFixture(fixture.root, [])
    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
    assert.match(result.stdout, /1 个简中资源键/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('完整验收仍拒绝后台繁中资源的无效结构', () => {
  const fixture = createBackendFixture()
  writeJson(join(fixture.resourceRoot, 'zh-Hant.json'), { culture: 'zh-Hant', texts: [] })
  try {
    const result = runBackendFixture(fixture.root, [])
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /zh-Hant.json 必须使用小写 culture\/texts/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('后端输入门禁拒绝生成器无法修复的简中文化错误', () => {
  const fixture = createBackendFixture()
  writeJson(join(fixture.resourceRoot, 'zh-Hans.json'), {
    culture: 'en',
    texts: { 'SwapMid:Fixture': '夹具错误' },
  })
  try {
    const result = runBackendFixture(fixture.root)
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /culture 必须等于 zh-Hans/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('后端输入门禁拒绝生成器不会清理的多余资源文件', () => {
  const fixture = createBackendFixture()
  writeJson(join(fixture.resourceRoot, 'extra.json'), { culture: 'extra', texts: {} })
  try {
    const result = runBackendFixture(fixture.root)
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /后端本地化资源只允许/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('后端输入门禁拒绝数组形态 texts', () => {
  const fixture = createBackendFixture()
  writeJson(join(fixture.resourceRoot, 'zh-Hans.json'), {
    culture: 'zh-Hans',
    texts: ['SwapMid:Fixture'],
  })
  try {
    const result = runBackendFixture(fixture.root)
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /必须使用小写 culture\/texts/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('后端输入门禁在第 5001 个资源键物化索引前失败', () => {
  const fixture = createBackendFixture()
  const texts = Object.fromEntries(
    Array.from({ length: 5_001 }, (_, index) => [`SwapMid:Fixture${index}`, '夹具错误']),
  )
  writeJson(join(fixture.resourceRoot, 'zh-Hans.json'), { culture: 'zh-Hans', texts })
  try {
    const result = runBackendFixture(fixture.root)
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /本地化资源键不得超过 5000/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})

test('跨文件第 5001 个 UserFriendlyException 仍按全局额度失败', () => {
  const fixture = createBackendFixture()
  const featureRoot = join(fixture.root, 'src', 'Feature')
  mkdirSync(featureRoot, { recursive: true })
  const source = (start, count) => Array.from(
    { length: count },
    (_, index) => `new UserFriendlyException("fixture-${start + index}");`,
  ).join('\n')
  writeFileSync(join(featureRoot, 'First.cs'), source(0, 2_500))
  writeFileSync(join(featureRoot, 'Second.cs'), source(2_500, 2_501))
  try {
    const result = runBackendFixture(fixture.root)
    assert.notEqual(result.status, 0)
    assert.match(result.stderr, /UserFriendlyException 总数超过上限 5000/)
  } finally {
    rmSync(fixture.root, { recursive: true, force: true })
  }
})
