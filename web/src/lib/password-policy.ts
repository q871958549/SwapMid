export const PASSWORD_POLICY_MIN_LENGTH = 6
export const PASSWORD_POLICY_MAX_LENGTH = 128

export type PasswordPolicy = {
  requiredLength: number
  requireDigit: boolean
  requireLowercase: boolean
  requireUppercase: boolean
  requireNonAlphanumeric: boolean
}

export type PasswordPolicyRuleId = 'length' | 'digit' | 'lowercase' | 'uppercase' | 'special'

export type PasswordPolicyRuleResult = {
  id: PasswordPolicyRuleId
  ok: boolean
}

const HAS_DIGIT = /\p{Nd}/u
const HAS_LOWER = /\p{Ll}/u
const HAS_UPPER = /\p{Lu}/u
const HAS_SPECIAL = /[^\p{L}\p{Nd}]/u

export function parsePasswordPolicy(input: unknown): PasswordPolicy | null {
  if (!input || typeof input !== 'object') return null
  const value = input as Record<string, unknown>
  const requiredLength = Number(value.requiredLength)
  if (
    !Number.isInteger(requiredLength)
    || requiredLength < PASSWORD_POLICY_MIN_LENGTH
    || requiredLength > PASSWORD_POLICY_MAX_LENGTH
  ) {
    return null
  }
  if (
    typeof value.requireDigit !== 'boolean'
    || typeof value.requireLowercase !== 'boolean'
    || typeof value.requireUppercase !== 'boolean'
    || typeof value.requireNonAlphanumeric !== 'boolean'
  ) {
    return null
  }
  return {
    requiredLength,
    requireDigit: value.requireDigit,
    requireLowercase: value.requireLowercase,
    requireUppercase: value.requireUppercase,
    requireNonAlphanumeric: value.requireNonAlphanumeric,
  }
}

export function listPasswordPolicyRules(policy: PasswordPolicy): PasswordPolicyRuleId[] {
  const rules: PasswordPolicyRuleId[] = ['length']
  if (policy.requireDigit) rules.push('digit')
  if (policy.requireLowercase) rules.push('lowercase')
  if (policy.requireUppercase) rules.push('uppercase')
  if (policy.requireNonAlphanumeric) rules.push('special')
  return rules
}

function rulePasses(password: string, id: PasswordPolicyRuleId, policy: PasswordPolicy): boolean {
  switch (id) {
    case 'length':
      return password.length >= policy.requiredLength
    case 'digit':
      return HAS_DIGIT.test(password)
    case 'lowercase':
      return HAS_LOWER.test(password)
    case 'uppercase':
      return HAS_UPPER.test(password)
    case 'special':
      return HAS_SPECIAL.test(password)
  }
}

export function evaluatePasswordPolicy(
  password: string,
  policy: PasswordPolicy,
): { ok: boolean; rules: PasswordPolicyRuleResult[] } {
  const rules = listPasswordPolicyRules(policy).map((id) => ({
    id,
    ok: rulePasses(password, id, policy),
  }))
  return { ok: rules.every((rule) => rule.ok), rules }
}

export function isPasswordComplexityFailure(message: string): boolean {
  const text = message.toLowerCase()
  if (!text) return false
  // 中文匹配用转义，避免 i18n 裸中文扫描把源码判成待翻译 UI。
  const mentionsPassword = text.includes('password') || text.includes('\u5bc6\u7801')
  if (!mentionsPassword) return false
  return (
    text.includes('require')
    || text.includes('length')
    || text.includes('digit')
    || text.includes('upper')
    || text.includes('lower')
    || text.includes('non alphanumeric')
    || text.includes('non-alphanumeric')
    || text.includes('complexity')
    || text.includes('\u4e0d\u7b26\u5408')
    || text.includes('\u81f3\u5c11')
  )
}
