import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'
import { pinyin } from 'pinyin-pro'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

// 与后端 EmployeeEmailHelper 一致：OA 邮箱缺失时回退「姓名全拼 + @example.com」。
export function defaultEmailFromName(name: string | null | undefined): string {
  const full = pinyin(name ?? '', { toneType: 'none', type: 'string' })
    .replace(/[^a-zA-Z0-9]/g, '')
    .toLowerCase()
  return `${full || 'user'}@example.com`
}

export function displayEmail(email: string | null | undefined, name: string | null | undefined): string {
  return email && email.trim() ? email.trim() : defaultEmailFromName(name)
}
