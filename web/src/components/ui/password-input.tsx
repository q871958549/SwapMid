'use client'

import { useState, type ComponentProps, type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import { Eye, EyeOff } from 'lucide-react'
import { cn } from '@/lib/utils'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'

interface PasswordInputProps extends Omit<ComponentProps<'input'>, 'type'> {
  rightSlot?: ReactNode
}

function PasswordInput({ className, rightSlot, ...props }: PasswordInputProps) {
  const t = useTranslations('Common')
  const [visible, setVisible] = useState(false)

  return (
    <div className="relative">
      <Input
        {...props}
        type={visible ? 'text' : 'password'}
        className={cn(rightSlot ? 'pr-28' : 'pr-10', className)}
      />
      {rightSlot && (
        <div className="absolute inset-y-0 right-9 flex items-center">
          {rightSlot}
        </div>
      )}
      <IconButton
        type="button"
        className="absolute right-1 top-1/2 size-7 -translate-y-1/2 text-muted-foreground"
        onClick={() => setVisible(value => !value)}
        label={visible ? t('hidePassword') : t('showPassword')}
      >
        {visible ? <EyeOff className="size-3.5" /> : <Eye className="size-3.5" />}
      </IconButton>
    </div>
  )
}

export { PasswordInput }
