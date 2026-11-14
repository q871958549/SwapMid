import { describe, expect, it } from 'vitest'
import {
  evaluatePasswordPolicy,
  isPasswordComplexityFailure,
  listPasswordPolicyRules,
  parsePasswordPolicy,
  type PasswordPolicy,
} from './password-policy'

const lengthOnly: PasswordPolicy = {
  requiredLength: 6,
  requireDigit: false,
  requireLowercase: false,
  requireUppercase: false,
  requireNonAlphanumeric: false,
}

const allClasses: PasswordPolicy = {
  requiredLength: 8,
  requireDigit: true,
  requireLowercase: true,
  requireUppercase: true,
  requireNonAlphanumeric: true,
}

const mixed: PasswordPolicy = {
  requiredLength: 10,
  requireDigit: true,
  requireLowercase: false,
  requireUppercase: true,
  requireNonAlphanumeric: false,
}

describe('parsePasswordPolicy', () => {
  it('接受完整规则对象', () => {
    expect(parsePasswordPolicy(allClasses)).toEqual(allClasses)
  })

  it('拒绝缺字段或越界长度，避免回退到写死口径', () => {
    expect(parsePasswordPolicy(null)).toBeNull()
    expect(parsePasswordPolicy({ ...allClasses, requiredLength: 5 })).toBeNull()
    expect(parsePasswordPolicy({ ...allClasses, requireDigit: 'true' })).toBeNull()
  })
})

describe('listPasswordPolicyRules', () => {
  it('仅长度', () => {
    expect(listPasswordPolicyRules(lengthOnly)).toEqual(['length'])
  })

  it('四类全开', () => {
    expect(listPasswordPolicyRules(allClasses)).toEqual([
      'length',
      'digit',
      'lowercase',
      'uppercase',
      'special',
    ])
  })

  it('中间态只列出开启的规则', () => {
    expect(listPasswordPolicyRules(mixed)).toEqual(['length', 'digit', 'uppercase'])
  })
})

describe('evaluatePasswordPolicy', () => {
  it('仅长度：6 位即通过', () => {
    expect(evaluatePasswordPolicy('abcdef', lengthOnly).ok).toBe(true)
    expect(evaluatePasswordPolicy('abcde', lengthOnly).ok).toBe(false)
  })

  it('四类全开：缺一类即失败', () => {
    expect(evaluatePasswordPolicy('Abcdef1!', allClasses).ok).toBe(true)
    expect(evaluatePasswordPolicy('Abcdefg!', allClasses).ok).toBe(false)
    expect(evaluatePasswordPolicy('abcdef1!', allClasses).ok).toBe(false)
  })

  it('中间态：不要求的类别不进结果', () => {
    const result = evaluatePasswordPolicy('ABCDEFGH12', mixed)
    expect(result.ok).toBe(true)
    expect(result.rules.map((rule) => rule.id)).toEqual(['length', 'digit', 'uppercase'])
  })

  it('用 Unicode 属性类对齐 CLR char.IsDigit/IsLower/IsUpper', () => {
    expect(evaluatePasswordPolicy('Abcdef１', { ...allClasses, requireNonAlphanumeric: false, requiredLength: 7 }).ok)
      .toBe(true)
    expect(evaluatePasswordPolicy('Ébcdef1', { ...allClasses, requireNonAlphanumeric: false, requiredLength: 7 }).ok)
      .toBe(true)
  })
})

describe('isPasswordComplexityFailure', () => {
  it('识别 Identity 复杂度英文与中文错误', () => {
    expect(isPasswordComplexityFailure('Passwords must have at least one digit.')).toBe(true)
    expect(isPasswordComplexityFailure('新密码不符合当前规则')).toBe(true)
    expect(isPasswordComplexityFailure('Invalid username or password.')).toBe(false)
  })
})
