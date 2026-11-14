'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { Check, ChevronsUpDown } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList,
} from '@/components/ui/command'
import { cn } from '@/lib/utils'
import { type AbpRole } from './users-types'

/** 列表里「所属角色」单元格：最多展示 2 个角色，溢出收进 +N 气泡。 */
export function RoleBadges({ roles }: { roles: string[] }) {
  const t = useTranslations('Account')
  if (roles.length === 0) {
    return <span className="text-xs text-muted-foreground">{t('user.noRolesLabel')}</span>
  }

  const visibleRoles = roles.slice(0, 2)
  const extraCount = roles.length - visibleRoles.length

  return (
    <div className="flex max-w-[12rem] flex-wrap gap-1">
      {visibleRoles.map(role => (
        <Badge key={role} variant="secondary" className="px-1.5 py-0.5 text-[11px]">
          {role}
        </Badge>
      ))}
      {extraCount > 0 && (
        <Popover>
          <PopoverTrigger asChild>
            <button type="button" className="inline-flex">
              <Badge variant="outline" className="cursor-pointer bg-muted/50 px-1.5 py-0.5 text-[11px]">
                +{extraCount}
              </Badge>
            </button>
          </PopoverTrigger>
          <PopoverContent align="start" className="flex w-56 flex-wrap gap-1 p-2">
            {roles.map(role => (
              <Badge key={role} variant="secondary" className="text-[11px]">
                {role}
              </Badge>
            ))}
          </PopoverContent>
        </Popover>
      )}
    </div>
  )
}

/** 多选角色控件（新建/编辑表单 + 批量分配 + 列表筛选共用）：Popover + Command 搜索勾选。 */
export function RoleMultiSelect({
  options,
  value,
  onChange,
  by = 'name',
  placeholder,
  className,
}: {
  options: AbpRole[]
  value: string[]
  onChange: (value: string[]) => void
  /** name：表单按角色名提交；id：列表筛选按角色 Id 记到 URL。 */
  by?: 'name' | 'id'
  placeholder?: string
  className?: string
}) {
  const t = useTranslations('Account')
  const [open, setOpen] = useState(false)
  const keyOf = (role: AbpRole) => (by === 'id' ? role.id : role.name)
  const selected = options.filter(role => value.includes(keyOf(role)))

  function toggle(role: AbpRole) {
    const key = keyOf(role)
    onChange(value.includes(key)
      ? value.filter(item => item !== key)
      : [...value, key])
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          className={cn('h-auto min-h-9 w-full justify-between px-3 py-1.5 font-normal', selected.length === 0 && 'text-muted-foreground', className)}
          title={by === 'id' ? t('user.roleFilterMatchHint') : undefined}
        >
          <span className="flex min-w-0 flex-1 flex-wrap gap-1">
            {selected.length === 0 ? (
              <span className="truncate">{placeholder ?? t('user.selectRolesPlaceholder')}</span>
            ) : (
              selected.slice(0, 3).map(role => (
                <Badge key={role.id} variant="secondary" className="max-w-[8rem] truncate text-[11px]">
                  {role.name}
                </Badge>
              ))
            )}
            {selected.length > 3 && <Badge variant="outline" className="text-[11px]">+{selected.length - 3}</Badge>}
          </span>
          <ChevronsUpDown className="ml-2 size-3.5 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-[var(--radix-popover-trigger-width)] overflow-hidden p-0"
        align="start"
        onWheel={(event) => event.stopPropagation()}
      >
        <Command>
          <CommandInput placeholder={t('user.searchRolePlaceholder')} />
          <CommandList
            onWheel={(event) => {
              const el = event.currentTarget
              if (el.scrollHeight > el.clientHeight) event.stopPropagation()
            }}
          >
            <CommandEmpty>{t('user.noAssignableRoles')}</CommandEmpty>
            <CommandGroup>
              {options.map(role => {
                const checked = value.includes(keyOf(role))
                return (
                  <CommandItem key={role.id} value={role.name} onSelect={() => toggle(role)}>
                    <Check className={cn('size-4 shrink-0', checked ? 'opacity-100' : 'opacity-0')} />
                    <span className="flex min-w-0 flex-1 flex-col">
                      <span className="truncate">{role.name}</span>
                      {(role.isStatic || role.isDefault) && (
                        <span className="text-xs text-muted-foreground">
                          {[role.isStatic ? t('common.systemRoleLabel') : '', role.isDefault ? t('common.defaultRoleLabel') : ''].filter(Boolean).join(' · ')}
                        </span>
                      )}
                    </span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
