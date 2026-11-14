'use client'

import { DatePicker } from '@/components/ui/date-picker'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { MonthPicker } from '@/components/ui/month-picker'
import type { JobParameterSchema } from './columns'

/**
 * 按后端声明的参数 schema 动态渲染表单字段（F7：替代此前硬编码的「月份」字段——
 * 每日产出重刷等任务的真实参数是 date 而非 month，硬编码字段与 handler 实际读取的 key 对不上）。
 * 类型 0=文本/1=某天/2=某月，与后端 JobParameterType 枚举同序。
 */
export function ParamFields({
  parameters,
  values,
  onChange,
}: {
  parameters: JobParameterSchema[]
  values: Record<string, string>
  onChange: (name: string, value: string) => void
}) {
  if (parameters.length === 0) return null

  return (
    <div className="space-y-3">
      {parameters.map(p => (
        <div key={p.name} className="space-y-1.5">
          <Label>{p.label}{p.required && ' *'}</Label>
          {p.type === 1 ? (
            <DatePicker
              value={values[p.name] ?? ''}
              onChange={v => onChange(p.name, v)}
              placeholder={p.defaultHint ?? undefined}
            />
          ) : p.type === 2 ? (
            <MonthPicker
              value={values[p.name] ?? ''}
              onChange={v => onChange(p.name, v)}
              placeholder={p.defaultHint ?? undefined}
            />
          ) : (
            <Input
              value={values[p.name] ?? ''}
              onChange={e => onChange(p.name, e.target.value)}
              placeholder={p.defaultHint ?? undefined}
            />
          )}
        </div>
      ))}
    </div>
  )
}

/** 把参数字段值拼成 ArgsJson；空值字段不写入（对应后端「留空走 handler 默认值」的约定）。全空时返回 null。 */
export function buildArgsJson(parameters: JobParameterSchema[], values: Record<string, string>): string | null {
  const entries = parameters
    .map(p => [p.name, (values[p.name] ?? '').trim()] as const)
    .filter(([, v]) => v !== '')
  if (entries.length === 0) return null
  return JSON.stringify(Object.fromEntries(entries))
}

/** 必填字段是否都已填写（用于禁用提交按钮）。 */
export function isParamsValid(parameters: JobParameterSchema[], values: Record<string, string>): boolean {
  return parameters.every(p => !p.required || (values[p.name] ?? '').trim() !== '')
}
