import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

const badgeVariants = cva(
  'inline-flex items-center rounded-md border px-2.5 py-0.5 text-xs font-semibold transition-colors outline-none focus-visible:border-ring focus-visible:ring-(--focus-ring-color) focus-visible:ring-(length:--focus-ring-width) select-none',
  {
    variants: {
      variant: {
        default: 'border-transparent bg-primary text-primary-foreground shadow hover:bg-primary/80',
        secondary: 'border-transparent bg-secondary text-secondary-foreground hover:bg-secondary/80',
        destructive: 'border-transparent bg-destructive text-white shadow hover:bg-destructive/80',
        'destructive-soft': 'border-destructive/25 bg-destructive-soft text-destructive',
        outline: 'text-foreground',
        success: 'border-success/25 bg-success-soft text-success',
        warning: 'border-warning/30 bg-warning-soft text-warning',
        info: 'border-info/25 bg-info-soft text-info',
      },
    },
    defaultVariants: { variant: 'default' },
  }
)

export type BadgeVariant = NonNullable<VariantProps<typeof badgeVariants>['variant']>

function Badge({ className, variant, ...props }: React.ComponentProps<'div'> & VariantProps<typeof badgeVariants>) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />
}

export { Badge, badgeVariants }
