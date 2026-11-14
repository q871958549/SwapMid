'use client'

import { useTranslations } from 'next-intl'
import { Check, Circle } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  evaluatePasswordPolicy,
  type PasswordPolicy,
  type PasswordPolicyRuleId,
} from '@/lib/password-policy'

function ruleLabel(
  id: PasswordPolicyRuleId,
  policy: PasswordPolicy,
  t: ReturnType<typeof useTranslations>,
): string {
  switch (id) {
    case 'length':
      return t('ruleMinLength', { n: policy.requiredLength })
    case 'digit':
      return t('ruleDigit')
    case 'lowercase':
      return t('ruleLowercase')
    case 'uppercase':
      return t('ruleUppercase')
    case 'special':
      return t('ruleSpecial')
  }
}

export function PasswordPolicyHints({
  policy,
  password = '',
  className,
}: {
  policy: PasswordPolicy | null
  password?: string
  className?: string
}) {
  const t = useTranslations('PasswordPolicy')

  if (!policy) {
    return (
      <p className={cn('text-muted-foreground text-xs leading-relaxed', className)}>
        {t('unavailableHint')}
      </p>
    )
  }

  const { rules } = evaluatePasswordPolicy(password, policy)

  return (
    <div className={cn('text-muted-foreground space-y-1.5 text-xs leading-relaxed', className)}>
      <p>{t('title')}</p>
      <ul className="space-y-1">
        {rules.map((rule) => (
          <li
            key={rule.id}
            className={cn(
              'flex items-center gap-1.5',
              rule.ok ? 'text-success' : 'text-muted-foreground',
            )}
          >
            {rule.ok ? <Check className="size-3.5 shrink-0" aria-hidden /> : <Circle className="size-3.5 shrink-0" aria-hidden />}
            <span>{ruleLabel(rule.id, policy, t)}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
