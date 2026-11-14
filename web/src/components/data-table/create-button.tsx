'use client'

import { Button } from '@/components/ui/button'
import { Plus } from 'lucide-react'

interface CreateButtonProps {
  label: string
  onClick?: () => void
  className?: string
}

export function CreateButton({ label, onClick, className }: CreateButtonProps) {
  return (
    <Button size="sm" className={className} onClick={onClick}>
      <Plus className="size-4" />
      {label}
    </Button>
  )
}
