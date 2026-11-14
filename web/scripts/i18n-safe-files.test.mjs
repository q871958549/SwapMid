import assert from 'node:assert/strict'
import {
  lstatSync,
  linkSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, join } from 'node:path'
import test from 'node:test'

import {
  readRegularFileBounded,
  readRegularFileBoundedSync,
  writeFileAtomic,
} from './i18n-safe-files.mjs'

test('所有读取 PR i18n 数据的可信脚本只能复用有界读取内核', () => {
  const consumers = [
    'i18n-pr-input-boundary.mjs',
    'i18n-audit-registry.mjs',
    'scan-hardcoded-cn.mjs',
    'check-backend-i18n.mjs',
    'check-i18n-keys.mjs',
    'sync-i18n.mjs',
    'sync-backend-i18n.mjs',
  ]
  for (const consumer of consumers) {
    const source = readFileSync(new URL(`./${consumer}`, import.meta.url), 'utf8')
    assert.doesNotMatch(
      source,
      /\breadFileSync\s*\(|\bhandle\.readFile\s*\(|\breadFile\s*\(/,
      `${consumer} 不得绕过 limit+1 读取内核`,
    )
  }
})

test('所有 i18n JSON 写入器复用有界序列化与原子替换', () => {
  const writers = [
    'check-i18n-keys.mjs',
    'sync-i18n.mjs',
    'sync-backend-i18n.mjs',
    'scan-hardcoded-cn.mjs',
    'check-backend-i18n.mjs',
  ]
  for (const writer of writers) {
    const source = readFileSync(new URL(`./${writer}`, import.meta.url), 'utf8')
    assert.match(source, /\bserializeJson\s*\(/, `${writer} 必须限制 pretty JSON 实际字节数`)
    assert.match(source, /\bwriteFileAtomic\s*\(/, `${writer} 必须原子替换目标文件`)
    assert.doesNotMatch(
      source,
      /\bwriteFile(?:Sync)?\s*\(/,
      `${writer} 不得绕过有界序列化与原子替换直接写入`,
    )
  }
})

test('同步有界读取允许恰好上限并在 limit+1 前失败', () => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-safe-read-sync-'))
  try {
    const target = join(root, 'catalog.json')
    writeFileSync(target, '1234', 'utf8')
    assert.deepEqual(
      readRegularFileBoundedSync(target, { maxBytes: 4, label: '测试目录' }),
      { buffer: Buffer.from('1234'), bytes: 4, text: '1234' },
    )

    writeFileSync(target, '12345', 'utf8')
    assert.throws(
      () => readRegularFileBoundedSync(target, { maxBytes: 4, label: '测试目录' }),
      /测试目录不得超过 4 字节/,
    )
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('零字节预算只允许稳定空文件', () => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-safe-read-zero-'))
  try {
    const target = join(root, 'empty.ts')
    writeFileSync(target, '', 'utf8')
    assert.deepEqual(
      readRegularFileBoundedSync(target, { maxBytes: 0, label: '零预算文件' }),
      { buffer: Buffer.alloc(0), bytes: 0, text: '' },
    )

    writeFileSync(target, '1', 'utf8')
    assert.throws(
      () => readRegularFileBoundedSync(target, { maxBytes: 0, label: '零预算文件' }),
      /零预算文件不得超过 0 字节/,
    )
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('异步有界读取与同步路径使用相同 limit+1 契约', async () => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-safe-read-async-'))
  try {
    const target = join(root, 'catalog.json')
    writeFileSync(target, '1234', 'utf8')
    const result = await readRegularFileBounded(target, { maxBytes: 4, label: '异步目录' })
    assert.equal(result.text, '1234')
    assert.equal(result.bytes, 4)

    writeFileSync(target, '12345', 'utf8')
    await assert.rejects(
      readRegularFileBounded(target, { maxBytes: 4, label: '异步目录' }),
      /异步目录不得超过 4 字节/,
    )
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('有界读取拒绝符号链接与非法 UTF-8', (context) => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-safe-read-invalid-'))
  try {
    const target = join(root, 'catalog.json')
    const link = join(root, 'linked.json')
    writeFileSync(target, Buffer.from([0xc3, 0x28]))
    assert.throws(
      () => readRegularFileBoundedSync(target, { maxBytes: 4, label: '非法文本' }),
      /非法文本必须是合法 UTF-8 文本/,
    )

    writeFileSync(target, '{}\n', 'utf8')
    try {
      symlinkSync(target, link, 'file')
    } catch (error) {
      if (error.code === 'EPERM') {
        context.diagnostic('当前 Windows 策略不允许创建符号链接，跳过链接分支')
        return
      }
      throw error
    }
    assert.throws(
      () => readRegularFileBoundedSync(link, { maxBytes: 4, label: '链接文本' }),
      /链接文本必须是普通文件且不得为符号链接/,
    )
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('原子写入使用随机独占临时文件并替换目标', () => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-safe-write-'))
  try {
    const target = join(root, 'catalog.json')
    writeFileSync(target, 'old', 'utf8')

    writeFileAtomic(target, 'new')

    assert.equal(readFileSync(target, 'utf8'), 'new')
    assert.equal(lstatSync(target).isFile(), true)
    assert.equal(lstatSync(target).isSymbolicLink(), false)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})

test('独占创建拒绝攻击者预置的临时链接', () => {
  const root = mkdtempSync(join(tmpdir(), 'swapmid-i18n-safe-link-'))
  try {
    const target = join(root, 'catalog.json')
    const outside = join(root, 'outside.json')
    const nonce = 'attacker-controlled'
    const temporary = join(
      root,
      `.${basename(target)}.${process.pid}.${nonce}.tmp`,
    )
    writeFileSync(target, 'old', 'utf8')
    writeFileSync(outside, 'guard', 'utf8')
    try {
      symlinkSync(outside, temporary, 'file')
    } catch (error) {
      if (error.code === 'EPERM') {
        linkSync(outside, temporary)
      } else {
        throw error
      }
    }

    assert.throws(
      () => writeFileAtomic(target, 'attacker data', { nonce }),
      /EEXIST/,
    )
    assert.equal(readFileSync(target, 'utf8'), 'old')
    assert.equal(readFileSync(outside, 'utf8'), 'guard')
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
})
