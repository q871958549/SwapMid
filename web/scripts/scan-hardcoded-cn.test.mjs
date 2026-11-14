import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import test from 'node:test'

import { collectSourceFiles, findCjkLiterals } from './scan-hardcoded-cn.mjs'

function sourceFixture(t) {
  const temporary = mkdtempSync(join(tmpdir(), 'hardcoded-cn-scan-'))
  const sourceRoot = join(temporary, 'src')
  mkdirSync(sourceRoot)
  t.after(() => rmSync(temporary, { force: true, recursive: true }))
  return { temporary, sourceRoot }
}

test('裸中文扫描忽略注释和 console，但保留字符串与 JSX 文本', () => {
  const source = `
    // 这是一段注释
    const value = '需要翻译'
    console.info('开发日志')
    /* 块注释中文 */
    export function View() {
      return <span>页面文案</span>
    }
  `

  assert.deepEqual(findCjkLiterals(source), ['需要翻译', '页面文案'])
})

test('同一字面量的重复出现会保留次数，供增量基线阻止回归', () => {
  assert.deepEqual(findCjkLiterals(`const a = '保存'; const b = '保存'`), ['保存', '保存'])
})

test('源码扫描拒绝指向根目录外的符号链接', (t) => {
  const { temporary, sourceRoot } = sourceFixture(t)
  const target = join(temporary, 'outside.ts')
  writeFileSync(target, "export const value = 'outside'\n")
  try {
    symlinkSync(target, join(sourceRoot, 'linked.ts'), 'file')
  } catch (error) {
    if (error?.code === 'EPERM') {
      t.skip('当前 Windows 环境不允许创建符号链接')
      return
    }
    throw error
  }

  assert.throws(() => collectSourceFiles(sourceRoot), /不得包含符号链接/)
})

test('源码扫描在读取前拒绝超过单文件预算的输入', (t) => {
  const { sourceRoot } = sourceFixture(t)
  writeFileSync(join(sourceRoot, 'oversized.ts'), '12345')

  assert.throws(() => collectSourceFiles(sourceRoot, { maxFileBytes: 4 }), /超过单文件上限 4 字节/)
})

test('源码扫描跳过 generated 文件，即使超过单文件上限', (t) => {
  const { sourceRoot } = sourceFixture(t)
  writeFileSync(join(sourceRoot, 'ok.ts'), 'ok')
  writeFileSync(join(sourceRoot, 'huge.generated.ts'), '12345')

  const files = collectSourceFiles(sourceRoot, { maxFileBytes: 4 })
  assert.deepEqual(
    files.map(({ rel, text }) => ({ rel: rel.replaceAll('\\', '/'), text })),
    [{ rel: 'ok.ts', text: 'ok' }],
  )
})

test('源码扫描拒绝目录项数量超预算的输入树', (t) => {
  const { sourceRoot } = sourceFixture(t)
  writeFileSync(join(sourceRoot, 'a.ts'), 'a')
  writeFileSync(join(sourceRoot, 'b.ts'), 'b')

  assert.throws(() => collectSourceFiles(sourceRoot, { maxDirectoryEntries: 1 }), /目录项超过上限 1/)
})

test('源码扫描按全部普通文件的实际大小执行总字节预算', (t) => {
  const { sourceRoot } = sourceFixture(t)
  writeFileSync(join(sourceRoot, 'a.ts'), '123')
  writeFileSync(join(sourceRoot, 'ignored.txt'), '456')

  assert.throws(() => collectSourceFiles(sourceRoot, { maxTotalBytes: 5 }), /文件总量超过上限 5 字节/)
})

test('源码扫描在总预算恰好耗尽后仍允许空 TypeScript 文件', (t) => {
  const { sourceRoot } = sourceFixture(t)
  writeFileSync(join(sourceRoot, 'a.ts'), '1')
  writeFileSync(join(sourceRoot, 'z.ts'), '')

  const files = collectSourceFiles(sourceRoot, { maxFileBytes: 1, maxTotalBytes: 1 })
  assert.deepEqual(
    files.map(({ rel, text }) => ({ rel, text })),
    [
      { rel: 'a.ts', text: '1' },
      { rel: 'z.ts', text: '' },
    ],
  )
})

test('源码扫描的文件与目录预算可独立收紧', async (t) => {
  await t.test('文件数预算', (nested) => {
    const { sourceRoot } = sourceFixture(nested)
    writeFileSync(join(sourceRoot, 'a.ts'), 'a')
    writeFileSync(join(sourceRoot, 'b.txt'), 'b')
    assert.throws(() => collectSourceFiles(sourceRoot, { maxFiles: 1 }), /文件数超过上限 1/)
  })

  await t.test('目录数预算包含源码根目录', (nested) => {
    const { sourceRoot } = sourceFixture(nested)
    mkdirSync(join(sourceRoot, 'nested'))
    assert.throws(() => collectSourceFiles(sourceRoot, { maxDirectories: 1 }), /目录数超过上限 1/)
  })
})

test('源码扫描结果按相对路径确定排序，并仅返回 TypeScript 文本', (t) => {
  const { sourceRoot } = sourceFixture(t)
  mkdirSync(join(sourceRoot, 'nested'))
  writeFileSync(join(sourceRoot, 'z.tsx'), 'z')
  writeFileSync(join(sourceRoot, 'nested', 'a.ts'), 'a')
  writeFileSync(join(sourceRoot, 'ignored.txt'), 'ignored')

  assert.deepEqual(
    collectSourceFiles(sourceRoot).map(({ rel, text }) => ({ rel: rel.replaceAll('\\', '/'), text })),
    [
      { rel: 'nested/a.ts', text: 'a' },
      { rel: 'z.tsx', text: 'z' },
    ],
  )
})
