export const MESSAGE_CATALOG_LIMITS = Object.freeze({
  depth: 20,
  nodes: 50_000,
  leaves: 20_000,
  keyCharacters: 512,
  valueCharacters: 20_000,
  totalValueCharacters: 1_000_000,
})

/**
 * 把 next-intl 嵌套消息对象展开为「完整键 → 叶子值」。
 * 输入可能来自 PR JSON，使用显式栈和硬预算，避免深层递归或海量叶子拖垮可信预检进程。
 */
export function flattenMessages(
  value,
  prefix = '',
  result = new Map(),
  limits = MESSAGE_CATALOG_LIMITS,
) {
  const stack = [{ value, prefix, depth: 0 }]
  const visited = new WeakSet()
  let nodes = 0
  let leaves = result.size
  let totalValueCharacters = 0

  while (stack.length > 0) {
    const current = stack.pop()
    nodes += 1
    if (nodes > limits.nodes) throw new Error(`i18n 消息目录节点数超过上限 ${limits.nodes}`)
    if (current.depth > limits.depth) throw new Error(`i18n 消息目录深度超过上限 ${limits.depth}`)

    if (current.value && typeof current.value === 'object' && !Array.isArray(current.value)) {
      if (visited.has(current.value)) throw new Error('i18n 消息目录不得包含循环引用')
      visited.add(current.value)
      const entries = Object.entries(current.value)
      for (let index = entries.length - 1; index >= 0; index -= 1) {
        const [key, child] = entries[index]
        if (!key || key.includes('.') || /[\u0000-\u001f\u007f]/.test(key)) {
          throw new Error(`i18n 消息目录键段不合法：${JSON.stringify(key)}`)
        }
        const fullKey = current.prefix ? `${current.prefix}.${key}` : key
        if (fullKey.length > limits.keyCharacters) {
          throw new Error(`i18n 消息键超过上限 ${limits.keyCharacters} 字符`)
        }
        stack.push({ value: child, prefix: fullKey, depth: current.depth + 1 })
      }
      continue
    }

    if (!current.prefix) continue
    leaves += 1
    if (leaves > limits.leaves) throw new Error(`i18n 消息叶子数超过上限 ${limits.leaves}`)
    if (typeof current.value === 'string') {
      if (current.value.length > limits.valueCharacters) {
        throw new Error(`i18n 消息 ${current.prefix} 超过单条上限 ${limits.valueCharacters} 字符`)
      }
      totalValueCharacters += current.value.length
      if (totalValueCharacters > limits.totalValueCharacters) {
        throw new Error(`i18n 消息文本总量超过上限 ${limits.totalValueCharacters} 字符`)
      }
    }
    result.set(current.prefix, current.value)
  }
  return result
}

function isArgumentName(value) {
  return value.length > 0 && !/[\s{},]/.test(value)
}

/**
 * 提取 ICU message 中的参数名，理解 plural/select 的分支子消息和 ICU apostrophe 转义。
 * 这里只比较参数契约，不负责替代 next-intl/FormatJS 的完整语法校验。
 */
export function extractIcuArgumentNames(message) {
  if (typeof message !== 'string') return new Set()

  const names = new Set()
  let index = 0

  function skipWhitespace() {
    while (index < message.length && /\s/.test(message[index])) index += 1
  }

  function skipQuotedText() {
    if (message[index] !== "'") return false
    if (message[index + 1] === "'") {
      index += 2
      return true
    }

    const next = message[index + 1]
    if (next !== '{' && next !== '}' && next !== '#') {
      index += 1
      return true
    }

    index += 1
    while (index < message.length) {
      if (message[index] !== "'") {
        index += 1
        continue
      }
      if (message[index + 1] === "'") {
        index += 2
        continue
      }
      index += 1
      break
    }
    return true
  }

  function skipStyleUntilArgumentEnd() {
    while (index < message.length) {
      if (message[index] === "'") {
        skipQuotedText()
      } else if (message[index] === '}') {
        index += 1
        return
      } else {
        index += 1
      }
    }
  }

  function parseMessage(stopAtClosingBrace) {
    while (index < message.length) {
      if (message[index] === "'") {
        skipQuotedText()
      } else if (message[index] === '{') {
        parseArgument()
      } else if (stopAtClosingBrace && message[index] === '}') {
        index += 1
        return
      } else {
        index += 1
      }
    }
  }

  function parseOptions() {
    while (index < message.length) {
      skipWhitespace()
      if (message[index] === '}') {
        index += 1
        return
      }

      // plural/selectordinal 可带 offset:1；它不是消息参数。
      if (message.startsWith('offset:', index)) {
        index += 'offset:'.length
        while (index < message.length && !/\s|}/.test(message[index])) index += 1
        continue
      }

      // 跳过 selector（one / other / =0 / male 等），其后的 {...} 是子消息而非参数声明。
      while (index < message.length && message[index] !== '{' && message[index] !== '}') index += 1
      if (message[index] === '{') {
        index += 1
        parseMessage(true)
      } else if (message[index] === '}') {
        index += 1
        return
      }
    }
  }

  function parseArgument() {
    index += 1
    skipWhitespace()
    const nameStart = index
    while (index < message.length && message[index] !== ',' && message[index] !== '}') index += 1
    const name = message.slice(nameStart, index).trim()
    if (isArgumentName(name)) names.add(name)

    if (message[index] === '}') {
      index += 1
      return
    }
    if (message[index] !== ',') return

    index += 1
    skipWhitespace()
    const typeStart = index
    while (index < message.length && message[index] !== ',' && message[index] !== '}') index += 1
    const type = message.slice(typeStart, index).trim()
    if (message[index] === '}') {
      index += 1
      return
    }
    if (message[index] !== ',') return

    index += 1
    if (type === 'plural' || type === 'selectordinal' || type === 'select') {
      parseOptions()
    } else {
      skipStyleUntilArgumentEnd()
    }
  }

  parseMessage(false)
  return names
}

export function findIcuArgumentDrift(localeMessages, baselineLocale) {
  const baseline = localeMessages.get(baselineLocale)
  const drift = []

  for (const [key, baselineValue] of baseline) {
    const expected = [...extractIcuArgumentNames(baselineValue)].sort()
    for (const [locale, messages] of localeMessages) {
      if (locale === baselineLocale || !messages.has(key)) continue
      const actual = [...extractIcuArgumentNames(messages.get(key))].sort()
      if (expected.join('\u0000') !== actual.join('\u0000')) {
        drift.push({ key, locale, expected, actual })
      }
    }
  }

  return drift
}
