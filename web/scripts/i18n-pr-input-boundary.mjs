import {
  lstatSync,
  opendirSync,
  realpathSync,
} from 'node:fs'
import {
  basename,
  dirname,
  isAbsolute,
  join,
  relative,
  resolve,
  sep,
} from 'node:path'
import { fileURLToPath } from 'node:url'

import {
  I18N_JSON_FILE_MAX_BYTES,
  readRegularFileBoundedSync,
} from './i18n-safe-files.mjs'

export const I18N_PR_INPUT_PATHS = Object.freeze([
  'web/messages/zh-Hans.json',
  'web/messages/zh-Hant.json',
  'web/messages/en.json',
  'web/i18n/generation-config.json',
  'web/i18n/reference-audit-registry.json',
  'web/i18n/translation-state.json',
  'web/i18n/hardcoded-cn-baseline.json',
  'src/SwapMid.Domain.Shared/Localization/SwapMid/zh-Hans.json',
  'src/SwapMid.Domain.Shared/Localization/SwapMid/zh-Hant.json',
  'src/SwapMid.Domain.Shared/Localization/SwapMid/en.json',
  'src/SwapMid.Domain.Shared/I18n/translation-state.json',
  'src/SwapMid.Domain.Shared/I18n/user-friendly-exception-baseline.json',
])

const SOURCE_ROOTS = Object.freeze([
  'web/src',
  'src',
])

const FIXED_INPUT_DIRECTORIES = Object.freeze([
  'web/messages',
  'web/i18n',
  'src/SwapMid.Domain.Shared/Localization/SwapMid',
  'src/SwapMid.Domain.Shared/I18n',
])

export const I18N_PR_BOUNDARY_LIMITS = Object.freeze({
  maxJsonBytes: I18N_JSON_FILE_MAX_BYTES,
  maxDirectories: 5_000,
  maxEntries: 20_000,
})

function normalizedLimits(overrides = {}) {
  const limits = { ...I18N_PR_BOUNDARY_LIMITS, ...overrides }
  for (const [name, value] of Object.entries(limits)) {
    if (!Number.isSafeInteger(value) || value <= 0) {
      throw new Error(`i18n PR 边界参数 ${name} 必须是正安全整数`)
    }
  }
  return Object.freeze(limits)
}

function sameIdentity(left, right) {
  return left.dev === right.dev && left.ino === right.ino
}

function sameDirectorySnapshot(left, right) {
  return sameIdentity(left, right)
    && left.size === right.size
    && left.mtimeNs === right.mtimeNs
    && left.ctimeNs === right.ctimeNs
}

function assertDirectory(stat, label) {
  if (!stat.isDirectory() || stat.isSymbolicLink()) {
    throw new Error(`${label}必须是普通目录且不得为符号链接`)
  }
}

function assertInside(root, candidate, label, { allowRoot = false } = {}) {
  const pathFromRoot = relative(root, candidate)
  if (
    isAbsolute(pathFromRoot)
    || pathFromRoot === '..'
    || pathFromRoot.startsWith(`..${sep}`)
    || (!allowRoot && pathFromRoot === '')
  ) {
    throw new Error(`${label}越出 PR checkout`)
  }
}

function assertNoLinkedPath(root, target, label) {
  assertInside(root, target, label, { allowRoot: true })
  const segments = relative(root, target).split(sep).filter(Boolean)
  let current = root

  for (const [index, segment] of segments.entries()) {
    current = join(current, segment)
    const stat = lstatSync(current, { bigint: true })
    if (stat.isSymbolicLink()) {
      throw new Error(`${label}的路径祖先不得为符号链接：${relative(root, current)}`)
    }
    if (index < segments.length - 1 && !stat.isDirectory()) {
      throw new Error(`${label}的路径祖先必须是目录：${relative(root, current)}`)
    }
  }
}

function canonicalInside(rootCanonical, target, label, { allowRoot = false } = {}) {
  const canonical = realpathSync.native(target)
  assertInside(rootCanonical, canonical, label, { allowRoot })
  return canonical
}

function reserveDirectory(budget, limits, label) {
  budget.directories += 1
  if (budget.directories > limits.maxDirectories) {
    throw new Error(`${label}目录总数不得超过 ${limits.maxDirectories}`)
  }
}

function reserveEntry(budget, limits, label) {
  budget.entries += 1
  if (budget.entries > limits.maxEntries) {
    throw new Error(`${label}目录项总数不得超过 ${limits.maxEntries}`)
  }
}

function scanDirectory(root, {
  budget,
  limits,
  prCanonical,
  recursive,
  rejectTemporary,
  label,
}) {
  const pending = [root]
  reserveDirectory(budget, limits, label)

  while (pending.length > 0) {
    const current = pending.pop()
    const before = lstatSync(current, { bigint: true })
    assertDirectory(before, label)
    const canonicalBefore = canonicalInside(prCanonical, current, label, { allowRoot: true })
    const directory = opendirSync(current)

    try {
      let entry
      while ((entry = directory.readSync()) !== null) {
        reserveEntry(budget, limits, label)
        const child = join(current, entry.name)
        const childStat = lstatSync(child, { bigint: true })
        if (childStat.isSymbolicLink()) {
          throw new Error(`${label}不得包含符号链接：${relative(root, child)}`)
        }
        if (rejectTemporary && entry.name.endsWith('.tmp')) {
          throw new Error(`${label}不得预置 i18n 临时文件：${relative(root, child)}`)
        }
        if (childStat.isDirectory()) {
          if (recursive) {
            reserveDirectory(budget, limits, label)
            pending.push(child)
          }
        } else if (!childStat.isFile()) {
          throw new Error(`${label}不得包含设备、管道或其他特殊文件：${relative(root, child)}`)
        }
      }
    } finally {
      directory.closeSync()
    }

    const after = lstatSync(current, { bigint: true })
    assertDirectory(after, label)
    const canonicalAfter = canonicalInside(prCanonical, current, label, { allowRoot: true })
    if (!sameDirectorySnapshot(before, after) || canonicalBefore !== canonicalAfter) {
      throw new Error(`${label}在扫描期间发生变化：${relative(root, current) || '.'}`)
    }
  }
}

function validateJsonObject(path, relativePath, prRoot, prCanonical, maxBytes) {
  assertNoLinkedPath(prRoot, path, relativePath)
  const canonicalBefore = canonicalInside(prCanonical, path, relativePath)
  const { text } = readRegularFileBoundedSync(path, {
    maxBytes,
    label: `PR i18n JSON ${relativePath}`,
  })
  const canonicalAfter = canonicalInside(prCanonical, path, relativePath)
  assertNoLinkedPath(prRoot, path, relativePath)
  if (canonicalBefore !== canonicalAfter) {
    throw new Error(`${relativePath}在读取期间发生替换`)
  }

  let value
  try {
    value = JSON.parse(text)
  } catch (error) {
    throw new Error(`${relativePath}必须是合法 JSON：${error.message}`)
  }
  if (value === null || Array.isArray(value) || typeof value !== 'object') {
    throw new Error(`${relativePath}的 JSON 根节点必须是对象`)
  }
}

function defaultLayout() {
  const modulePath = fileURLToPath(import.meta.url)
  const trustedRepositoryRoot = resolve(dirname(modulePath), '..', '..')
  return {
    trustedRepositoryRoot,
    workspaceRoot: dirname(trustedRepositoryRoot),
  }
}

/**
 * 只把 PR checkout 当数据读取：固定 JSON 用 limit+1 内核，源码树用流式目录预算。
 * 该门禁降低单次输入造成的内存/CPU/路径逃逸风险，但不替代 Runner 的一次性 OS 隔离。
 */
export function validatePrInputBoundary(options = {}) {
  const defaults = defaultLayout()
  const workspaceRoot = resolve(options.workspaceRoot ?? defaults.workspaceRoot)
  const trustedRepositoryRoot = resolve(
    options.trustedRepositoryRoot ?? defaults.trustedRepositoryRoot,
  )
  const prRoot = resolve(options.prRoot ?? join(workspaceRoot, 'pr'))
  const limits = normalizedLimits(options.limits)

  if (
    dirname(trustedRepositoryRoot) !== workspaceRoot
    || basename(trustedRepositoryRoot) !== 'trusted'
  ) {
    throw new Error('可信 checkout 必须是工作区的直接子目录 trusted')
  }
  if (dirname(prRoot) !== workspaceRoot || basename(prRoot) !== 'pr') {
    throw new Error('PR checkout 必须是工作区的直接子目录 pr')
  }

  const trustedBefore = lstatSync(trustedRepositoryRoot, { bigint: true })
  const prBefore = lstatSync(prRoot, { bigint: true })
  assertDirectory(trustedBefore, '可信 checkout ')
  assertDirectory(prBefore, 'PR checkout ')
  const prCanonical = realpathSync.native(prRoot)

  for (const relativePath of I18N_PR_INPUT_PATHS) {
    validateJsonObject(
      join(prRoot, relativePath),
      relativePath,
      prRoot,
      prCanonical,
      limits.maxJsonBytes,
    )
  }

  const budget = { directories: 0, entries: 0 }
  for (const sourceRoot of SOURCE_ROOTS) {
    const absoluteRoot = join(prRoot, sourceRoot)
    assertNoLinkedPath(prRoot, absoluteRoot, `PR 源码树 ${sourceRoot}`)
    scanDirectory(absoluteRoot, {
      budget,
      limits,
      prCanonical,
      recursive: true,
      rejectTemporary: false,
      label: `PR 源码树 ${sourceRoot}`,
    })
  }
  for (const inputDirectory of FIXED_INPUT_DIRECTORIES) {
    const absoluteRoot = join(prRoot, inputDirectory)
    assertNoLinkedPath(prRoot, absoluteRoot, `PR i18n 目录 ${inputDirectory}`)
    scanDirectory(absoluteRoot, {
      budget,
      limits,
      prCanonical,
      recursive: false,
      rejectTemporary: true,
      label: `PR i18n 目录 ${inputDirectory}`,
    })
  }

  const trustedAfter = lstatSync(trustedRepositoryRoot, { bigint: true })
  const prAfter = lstatSync(prRoot, { bigint: true })
  if (!sameDirectorySnapshot(trustedBefore, trustedAfter)) {
    throw new Error('可信 checkout 在边界校验期间发生变化')
  }
  if (!sameDirectorySnapshot(prBefore, prAfter)) {
    throw new Error('PR checkout 在边界校验期间发生变化')
  }

  return Object.freeze({
    directories: budget.directories,
    entries: budget.entries,
    jsonFiles: I18N_PR_INPUT_PATHS.length,
  })
}

const isMain = process.argv[1]
  && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url))

if (isMain) {
  try {
    const result = validatePrInputBoundary()
    console.log(
      `PR i18n 输入边界有效：${result.jsonFiles} 个 JSON，`
      + `${result.directories} 个目录，${result.entries} 个目录项。`,
    )
  } catch (error) {
    console.error(error.message)
    process.exitCode = 1
  }
}
