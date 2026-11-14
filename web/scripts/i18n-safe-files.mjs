import { randomUUID } from 'node:crypto'
import {
  closeSync,
  constants,
  fstatSync,
  lstatSync,
  openSync,
  readSync,
  renameSync,
  rmSync,
  writeFileSync,
} from 'node:fs'
import { lstat, open } from 'node:fs/promises'
import { basename, dirname, join } from 'node:path'

const UTF8_DECODER = new TextDecoder('utf-8', { fatal: true })

export const I18N_JSON_FILE_MAX_BYTES = 4 * 1024 * 1024

function assertReadLimit(maxBytes) {
  if (!Number.isSafeInteger(maxBytes) || maxBytes < 0 || maxBytes >= 2 ** 31) {
    throw new Error('i18n 文件读取上限必须是小于 2 GiB 的非负安全整数')
  }
}

function assertRegularFile(stat, label) {
  if (!stat.isFile() || stat.isSymbolicLink?.()) {
    throw new Error(`${label}必须是普通文件且不得为符号链接`)
  }
}

function sameIdentity(left, right) {
  return left.dev === right.dev && left.ino === right.ino
}

function sameSnapshot(left, right) {
  return sameIdentity(left, right)
    && left.size === right.size
    && left.mtimeNs === right.mtimeNs
    && left.ctimeNs === right.ctimeNs
}

function assertOpenFileMatchesPath(pathStat, descriptorStat, label) {
  assertRegularFile(pathStat, label)
  assertRegularFile(descriptorStat, label)
  if (!sameIdentity(pathStat, descriptorStat)) {
    throw new Error(`${label}在打开期间发生替换`)
  }
}

function decodeUtf8(buffer, label) {
  try {
    return UTF8_DECODER.decode(buffer)
  } catch {
    throw new Error(`${label}必须是合法 UTF-8 文本`)
  }
}

function readCapacity(size, maxBytes, label) {
  if (size > BigInt(maxBytes)) throw new Error(`${label}不得超过 ${maxBytes} 字节`)
  return Number(size) + 1
}

function assertStableRead(before, after, bytesRead, maxBytes, label) {
  if (bytesRead > maxBytes) throw new Error(`${label}不得超过 ${maxBytes} 字节`)
  if (!sameSnapshot(before, after) || BigInt(bytesRead) !== before.size) {
    throw new Error(`${label}在读取期间发生变化`)
  }
}

/**
 * 从可信代码给定的固定路径读取最多 maxBytes 字节；0 只允许稳定的空文件。
 *
 * 不能用 stat + readFile：文件在两者之间增长时，readFile 会先分配超额内存，事后校验已经太晚。
 * 本函数只分配「打开时大小 + 1」且不超过「maxBytes + 1」，第 limit+1 字节会立即失败；同时
 * 复核路径/descriptor 身份和读取前后元数据。它提供确定的资源上界，但不替代共享 Runner 的 OS 隔离。
 */
export function readRegularFileBoundedSync(path, { maxBytes, label = path } = {}) {
  assertReadLimit(maxBytes)
  const pathBefore = lstatSync(path, { bigint: true })
  assertRegularFile(pathBefore, label)
  const flags = constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0)
  const descriptor = openSync(path, flags)

  try {
    const descriptorBefore = fstatSync(descriptor, { bigint: true })
    assertOpenFileMatchesPath(pathBefore, descriptorBefore, label)
    const capacity = readCapacity(descriptorBefore.size, maxBytes, label)
    const buffer = Buffer.allocUnsafe(capacity)
    let bytesRead = 0
    while (bytesRead < capacity) {
      const count = readSync(
        descriptor,
        buffer,
        bytesRead,
        capacity - bytesRead,
        bytesRead,
      )
      if (count === 0) break
      bytesRead += count
    }

    const descriptorAfter = fstatSync(descriptor, { bigint: true })
    assertStableRead(descriptorBefore, descriptorAfter, bytesRead, maxBytes, label)
    const pathAfter = lstatSync(path, { bigint: true })
    assertOpenFileMatchesPath(pathAfter, descriptorAfter, label)
    if (!sameSnapshot(pathBefore, pathAfter)) {
      throw new Error(`${label}在读取期间发生变化`)
    }
    const content = buffer.subarray(0, bytesRead)
    return Object.freeze({
      buffer: content,
      bytes: bytesRead,
      text: decodeUtf8(content, label),
    })
  } finally {
    closeSync(descriptor)
  }
}

export async function readRegularFileBounded(path, { maxBytes, label = path } = {}) {
  assertReadLimit(maxBytes)
  const pathBefore = await lstat(path, { bigint: true })
  assertRegularFile(pathBefore, label)
  const flags = constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0)
  const handle = await open(path, flags)

  try {
    const descriptorBefore = await handle.stat({ bigint: true })
    assertOpenFileMatchesPath(pathBefore, descriptorBefore, label)
    const capacity = readCapacity(descriptorBefore.size, maxBytes, label)
    const buffer = Buffer.allocUnsafe(capacity)
    let bytesRead = 0
    while (bytesRead < capacity) {
      const { bytesRead: count } = await handle.read(
        buffer,
        bytesRead,
        capacity - bytesRead,
        bytesRead,
      )
      if (count === 0) break
      bytesRead += count
    }

    const descriptorAfter = await handle.stat({ bigint: true })
    assertStableRead(descriptorBefore, descriptorAfter, bytesRead, maxBytes, label)
    const pathAfter = await lstat(path, { bigint: true })
    assertOpenFileMatchesPath(pathAfter, descriptorAfter, label)
    if (!sameSnapshot(pathBefore, pathAfter)) {
      throw new Error(`${label}在读取期间发生变化`)
    }
    const content = buffer.subarray(0, bytesRead)
    return Object.freeze({
      buffer: content,
      bytes: bytesRead,
      text: decodeUtf8(content, label),
    })
  } finally {
    await handle.close()
  }
}

export function writeFileAtomic(path, content, options = {}) {
  const nonce = options.nonce ?? randomUUID()
  const temporary = join(dirname(path), `.${basename(path)}.${process.pid}.${nonce}.tmp`)
  const flags =
    constants.O_CREAT |
    constants.O_EXCL |
    constants.O_WRONLY |
    (constants.O_NOFOLLOW ?? 0)
  let descriptor
  let created = false

  try {
    descriptor = openSync(temporary, flags, 0o600)
    created = true
    writeFileSync(descriptor, content, 'utf8')
    closeSync(descriptor)
    descriptor = undefined
    renameSync(temporary, path)
  } catch (error) {
    if (descriptor !== undefined) closeSync(descriptor)
    if (created) rmSync(temporary, { force: true })
    throw error
  }
}
