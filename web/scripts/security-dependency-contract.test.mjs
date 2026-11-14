import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { dirname, join, parse } from 'node:path'
import test from 'node:test'

const rootRequire = createRequire(import.meta.url)
const defaultExpansionMaxLength = 4_000_000

async function readPackageForEntry(entryPath) {
  const root = parse(entryPath).root
  for (let directory = dirname(entryPath); directory !== root; directory = dirname(directory)) {
    try {
      const packageJson = JSON.parse(await readFile(join(directory, 'package.json'), 'utf8'))
      if (packageJson.name && packageJson.version) return packageJson
    } catch (error) {
      if (error?.code !== 'ENOENT') throw error
    }
  }
  throw new Error(`找不到 ${entryPath} 对应的 package.json`)
}

function resolveMinimatch3ThroughExcelExport() {
  const excelEntry = rootRequire.resolve('exceljs')
  const excelRequire = createRequire(excelEntry)
  const archiverEntry = excelRequire.resolve('archiver')
  const archiverRequire = createRequire(archiverEntry)
  const archiverUtilsEntry = archiverRequire.resolve('archiver-utils')
  const archiverUtilsRequire = createRequire(archiverUtilsEntry)
  const globEntry = archiverUtilsRequire.resolve('glob')
  const globRequire = createRequire(globEntry)
  const minimatchEntry = globRequire.resolve('minimatch')
  const minimatchRequire = createRequire(minimatchEntry)
  const braceExpansionEntry = minimatchRequire.resolve('brace-expansion')

  return {
    braceExpansion: minimatchRequire(braceExpansionEntry),
    braceExpansionEntry,
    minimatch: minimatchRequire(minimatchEntry),
    minimatchEntry,
  }
}

function expandWithDefaultOptionsInBoundedChild(braceExpansionEntry) {
  const childSource = `
    const braceExpansion = require(process.argv[1])
    const input = '{a,b}'.repeat(100)
    const expanded = braceExpansion(input)
    const totalLength = expanded.reduce((sum, value) => sum + value.length, 0)

    process.stdout.write(JSON.stringify({
      count: expanded.length,
      totalLength,
      allResultsHaveExpectedWidth: expanded.every((value) => value.length === 100),
    }))
  `

  return JSON.parse(execFileSync(
    process.execPath,
    [
      '--max-old-space-size=96',
      '--input-type=commonjs',
      '--eval',
      childSource,
      braceExpansionEntry,
    ],
    {
      encoding: 'utf8',
      maxBuffer: 1024 * 1024,
      timeout: 10_000,
      windowsHide: true,
    },
  ))
}

function collectBraceExpansionConsumers(lockfile, version) {
  const lines = lockfile.split(/\r?\n/)
  const snapshotsIndex = lines.findIndex((line) => line === 'snapshots:')
  assert.notEqual(snapshotsIndex, -1, 'pnpm-lock.yaml 缺少 snapshots 段')

  const consumers = []
  let currentSnapshot = null

  for (const line of lines.slice(snapshotsIndex + 1)) {
    const snapshotMatch = /^  (\S.*):$/.exec(line)
    if (snapshotMatch) {
      currentSnapshot = snapshotMatch[1].replace(/^(['"])(.*)\1$/, '$2')
      continue
    }

    const dependencyMatch = /^      brace-expansion: (.+)$/.exec(line)
    if (!dependencyMatch || dependencyMatch[1].replace(/^(['"])(.*)\1$/, '$2') !== version) {
      continue
    }

    assert.ok(currentSnapshot, `brace-expansion@${version} 缺少父 snapshot`)
    const minimatchMatch = /^minimatch@(\d+)\./.exec(currentSnapshot)
    consumers.push(minimatchMatch ? `minimatch@${minimatchMatch[1]}.x` : currentSnapshot)
  }

  return consumers.sort()
}

test('Excel 导出链的 minimatch@3 实际解析到同主版本安全回移版', async () => {
  const {
    braceExpansion,
    braceExpansionEntry,
    minimatch,
    minimatchEntry,
  } = resolveMinimatch3ThroughExcelExport()

  const minimatchPackage = await readPackageForEntry(minimatchEntry)
  const braceExpansionPackage = await readPackageForEntry(braceExpansionEntry)

  assert.equal(minimatchPackage.name, 'minimatch')
  assert.match(minimatchPackage.version, /^3\./)
  assert.match(minimatchPackage.dependencies?.['brace-expansion'] ?? '', /^\^1\./)
  assert.equal(typeof minimatch, 'function')

  assert.equal(braceExpansionPackage.name, 'brace-expansion')
  assert.equal(braceExpansionPackage.version, '1.1.21')
  assert.equal(typeof braceExpansion, 'function')
})

test('minimatch@3 保持项目黄金 glob 的展开顺序和匹配语义', () => {
  const { minimatch } = resolveMinimatch3ThroughExcelExport()

  assert.deepEqual(
    minimatch.braceExpand('src/**/*.{ts,tsx}'),
    ['src/**/*.ts', 'src/**/*.tsx'],
  )
  assert.deepEqual(
    minimatch.braceExpand('src/**/*.{test,spec}.{ts,tsx}'),
    [
      'src/**/*.test.ts',
      'src/**/*.test.tsx',
      'src/**/*.spec.ts',
      'src/**/*.spec.tsx',
    ],
  )

  assert.equal(minimatch('src/reporting/daily.tsx', 'src/**/*.{ts,tsx}'), true)
  assert.equal(minimatch('src/reporting/daily.test.tsx', 'src/**/*.{test,spec}.{ts,tsx}'), true)
  assert.equal(minimatch('src/reporting/daily.tsx', 'src/**/*.{test,spec}.{ts,tsx}'), false)
  assert.equal(minimatch('src/reporting/daily.test.js', 'src/**/*.{test,spec}.{ts,tsx}'), false)
})

test('brace-expansion@1.1.21 的 maxLength 边界会截断组合爆炸', () => {
  const { braceExpansion } = resolveMinimatch3ThroughExcelExport()
  const maxLength = 512
  const expanded = braceExpansion('{a,b}'.repeat(20), {
    max: 100_000,
    maxLength,
  })
  const totalLength = expanded.reduce((sum, value) => sum + value.length, 0)

  assert.ok(expanded.length > 0)
  assert.ok(totalLength <= maxLength, `展开总长度 ${totalLength} 超过 ${maxLength}`)
  assert.ok(expanded.every((value) => /^[ab]{20}$/.test(value)))
})

test('brace-expansion@1.1.21 默认边界在受限子进程中阻止组合爆炸', () => {
  const { braceExpansionEntry } = resolveMinimatch3ThroughExcelExport()
  const result = expandWithDefaultOptionsInBoundedChild(braceExpansionEntry)

  assert.ok(result.count > 0)
  assert.ok(result.count < 100_000, '默认 maxLength 未在 max 数量上限前截断展开')
  assert.ok(
    result.totalLength <= defaultExpansionMaxLength,
    `默认展开总长度 ${result.totalLength} 超过 ${defaultExpansionMaxLength}`,
  )
  assert.equal(result.allResultsHaveExpectedWidth, true)
})

test('各 brace-expansion 安全版本仅供已审阅的 minimatch 主版本使用', async () => {
  const lockfile = await readFile(new URL('../pnpm-lock.yaml', import.meta.url), 'utf8')

  assert.deepEqual(
    collectBraceExpansionConsumers(lockfile, '1.1.21'),
    ['minimatch@3.x'],
  )
  assert.deepEqual(
    collectBraceExpansionConsumers(lockfile, '2.1.8'),
    ['minimatch@5.x'],
  )
  assert.deepEqual(
    collectBraceExpansionConsumers(lockfile, '5.0.13'),
    ['minimatch@11.x'],
  )
})

test('锁文件仅包含已审阅的 brace-expansion 安全版本', async () => {
  const lockfile = await readFile(new URL('../pnpm-lock.yaml', import.meta.url), 'utf8')
  const versions = [
    ...new Set(
      [...lockfile.matchAll(/^ {2}brace-expansion@([^:]+):$/gm)]
        .map((match) => match[1]),
    ),
  ].sort()

  assert.deepEqual(versions, ['1.1.21', '2.1.8', '5.0.13'])
})

test('保留 24 小时供应链观察窗及锁文件校验', async () => {
  const workspace = await readFile(new URL('../pnpm-workspace.yaml', import.meta.url), 'utf8')
  assert.match(workspace, /^minimumReleaseAge: 1440$/m)
  assert.doesNotMatch(workspace, /^minimumReleaseAgeExclude:/m)
  assert.doesNotMatch(workspace, /^trustLockfile:\s*true$/m)
})

test('Next 的 TypeScript ESLint 链使用安全 minimatch 并保留匹配语义', async () => {
  const configRequire = createRequire(rootRequire.resolve('eslint-config-next'))
  const parserRequire = createRequire(configRequire.resolve('@typescript-eslint/parser'))
  const estreeRequire = createRequire(parserRequire.resolve('@typescript-eslint/typescript-estree'))
  const entry = estreeRequire.resolve('minimatch')
  const matcher = estreeRequire(entry)
  assert.equal((await readPackageForEntry(entry)).version, '11.0.3')
  assert.deepEqual(matcher.braceExpand('src/**/*.{ts,tsx}'), ['src/**/*.ts', 'src/**/*.tsx'])
  assert.equal(matcher.minimatch('src/lib/example.ts', 'src/**/*.{ts,tsx}'), true)
  assert.equal(matcher.minimatch('src/lib/example.js', 'src/**/*.{ts,tsx}'), false)
})
