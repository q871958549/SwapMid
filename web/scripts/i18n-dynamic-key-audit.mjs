export function dynamicReferenceFingerprint(item) {
  return JSON.stringify([
    item.file.replaceAll('\\', '/'),
    item.functionName ?? null,
    item.variable,
    item.method,
    item.namespace,
    item.expression,
  ])
}

/** 校验动态键审计清单：拒绝未登记、重复、空理由、不存在的键以及已失效条目。 */
export function auditDynamicKeyAllowlist(references, allowlist, knownKeys) {
  const maxReportedErrors = 100
  const errors = []
  const entries = new Map()

  for (const entry of allowlist) {
    const fingerprint = dynamicReferenceFingerprint(entry)
    if (entries.has(fingerprint)) {
      errors.push(`审计清单重复：${entry.file} ${entry.namespace}.${entry.expression}`)
      continue
    }
    entries.set(fingerprint, entry)

    if (typeof entry.reason !== 'string' || entry.reason.trim().length < 10) {
      errors.push(`审计清单理由过短：${entry.file} ${entry.expression}`)
    }
    if (!Number.isInteger(entry.expectedOccurrences) || entry.expectedOccurrences < 1) {
      errors.push(`审计清单 expectedOccurrences 必须是正整数：${entry.file} ${entry.expression}`)
    }
    if (!Array.isArray(entry.fullKeys) || entry.fullKeys.length === 0) {
      errors.push(`审计清单必须枚举 fullKeys：${entry.file} ${entry.expression}`)
      continue
    }
    for (const key of entry.fullKeys) {
      if (!knownKeys.has(key)) errors.push(`审计清单引用不存在的键：${entry.file} ${key}`)
      if (entry.namespace && !key.startsWith(`${entry.namespace}.`)) {
        errors.push(`审计清单键不属于命名空间 ${entry.namespace}：${entry.file} ${key}`)
      }
    }
  }

  const occurrences = new Map()
  for (const reference of references) {
    if (reference.namespace === null) {
      errors.push(
        `动态 namespace 禁止使用：${reference.file}:${reference.line} `
        + `${reference.variable}(${reference.namespaceExpression}).${reference.expression}`,
      )
      continue
    }
    const fingerprint = dynamicReferenceFingerprint(reference)
    const entry = entries.get(fingerprint)
    if (!entry) {
      errors.push(
        `未登记的动态翻译键：${reference.file}:${reference.line} `
        + `${reference.variable}(${reference.namespace}).${reference.expression}`,
      )
      continue
    }
    occurrences.set(fingerprint, (occurrences.get(fingerprint) ?? 0) + 1)
  }

  for (const [fingerprint, entry] of entries) {
    const actual = occurrences.get(fingerprint) ?? 0
    if (actual === 0) {
      errors.push(`失效的动态键审计项：${entry.file} ${entry.namespace}.${entry.expression}`)
    } else if (actual !== entry.expectedOccurrences) {
      errors.push(
        `动态键调用次数漂移：${entry.file} ${entry.namespace}.${entry.expression} `
        + `expected=${entry.expectedOccurrences} actual=${actual}`,
      )
    }
  }

  if (errors.length <= maxReportedErrors) return errors
  return [
    ...errors.slice(0, maxReportedErrors),
    `其余 ${errors.length - maxReportedErrors} 条动态键审计错误已省略`,
  ]
}
