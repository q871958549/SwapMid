import assert from 'node:assert/strict'
import {
  mkdirSync,
  mkdtempSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'

import {
  I18N_PR_INPUT_PATHS,
  validatePrInputBoundary,
} from './i18n-pr-input-boundary.mjs'

function writeJson(path, value = {}) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, `${JSON.stringify(value)}\n`, 'utf8')
}

function createFixture() {
  const workspaceRoot = mkdtempSync(join(tmpdir(), 'swapmid-i18n-pr-boundary-'))
  const trustedRepositoryRoot = join(workspaceRoot, 'trusted')
  const prRoot = join(workspaceRoot, 'pr')
  mkdirSync(trustedRepositoryRoot)
  for (const relativePath of I18N_PR_INPUT_PATHS) {
    writeJson(join(prRoot, relativePath))
  }
  mkdirSync(join(prRoot, 'web', 'src', 'feature'), { recursive: true })
  writeFileSync(join(prRoot, 'web', 'src', 'feature', 'client.ts'), 'export {}\n')
  mkdirSync(join(prRoot, 'src', 'Fixture'), { recursive: true })
  writeFileSync(join(prRoot, 'src', 'Fixture', 'Example.cs'), 'namespace Fixture;\n')
  return { workspaceRoot, trustedRepositoryRoot, prRoot }
}

function validate(fixture, overrides = {}) {
  return validatePrInputBoundary({
    workspaceRoot: fixture.workspaceRoot,
    trustedRepositoryRoot: fixture.trustedRepositoryRoot,
    prRoot: fixture.prRoot,
    ...overrides,
  })
}

test('PR 输入边界接受固定对象 JSON 与普通源码树', () => {
  const fixture = createFixture()
  try {
    const result = validate(fixture)
    assert.equal(result.jsonFiles, I18N_PR_INPUT_PATHS.length)
    assert.ok(result.directories > 0)
    assert.ok(result.entries > I18N_PR_INPUT_PATHS.length)
  } finally {
    rmSync(fixture.workspaceRoot, { recursive: true, force: true })
  }
})

test('PR 输入边界在读取前拒绝超过 limit 的固定 JSON', () => {
  const fixture = createFixture()
  try {
    writeFileSync(join(fixture.prRoot, I18N_PR_INPUT_PATHS[0]), '12345')
    assert.throws(
      () => validate(fixture, { limits: { maxJsonBytes: 4 } }),
      /不得超过 4 字节/,
    )
  } finally {
    rmSync(fixture.workspaceRoot, { recursive: true, force: true })
  }
})

test('PR 输入边界拒绝非对象 JSON 根节点', () => {
  const fixture = createFixture()
  try {
    writeJson(join(fixture.prRoot, I18N_PR_INPUT_PATHS[0]), [])
    assert.throws(() => validate(fixture), /JSON 根节点必须是对象/)
  } finally {
    rmSync(fixture.workspaceRoot, { recursive: true, force: true })
  }
})

test('PR 输入边界拒绝源码树符号链接', (context) => {
  const fixture = createFixture()
  try {
    const target = join(fixture.workspaceRoot, 'outside.ts')
    const link = join(fixture.prRoot, 'web', 'src', 'feature', 'linked.ts')
    writeFileSync(target, 'export const outside = true\n')
    try {
      symlinkSync(target, link, 'file')
    } catch (error) {
      if (error.code === 'EPERM') {
        context.skip('当前 Windows 策略不允许创建符号链接')
        return
      }
      throw error
    }
    assert.throws(() => validate(fixture), /源码树.*不得包含符号链接/)
  } finally {
    rmSync(fixture.workspaceRoot, { recursive: true, force: true })
  }
})

test('PR 输入边界在收集目录项前执行全局预算', () => {
  const fixture = createFixture()
  try {
    assert.throws(
      () => validate(fixture, { limits: { maxEntries: 1 } }),
      /目录项总数不得超过 1/,
    )
  } finally {
    rmSync(fixture.workspaceRoot, { recursive: true, force: true })
  }
})

test('PR 输入边界拒绝固定 i18n 目录中的预置临时文件', () => {
  const fixture = createFixture()
  try {
    writeFileSync(join(fixture.prRoot, 'web', 'i18n', 'attacker.tmp'), 'guard')
    assert.throws(() => validate(fixture), /不得预置 i18n 临时文件/)
  } finally {
    rmSync(fixture.workspaceRoot, { recursive: true, force: true })
  }
})
