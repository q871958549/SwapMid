'use client'

import { useTranslations } from 'next-intl'
import { CrudFormDialog } from '@/components/data-table/crud-form-dialog'
import { Combobox } from '@/components/ui/combobox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { CompanyDepartmentDto, EmployeeDto, EnterpriseDto, PositionDto } from '@/lib/api/types'
import { missingLabels } from '@/lib/crud/missing-fields'
import type { useCrudForm } from '@/lib/crud/use-crud-form'

export type Employee = EmployeeDto
export type EnterpriseOption = EnterpriseDto
export type DepartmentOption = CompanyDepartmentDto
export type PositionOption = PositionDto

export interface EmployeeFormState {
  enterpriseId: string
  departmentId: string
  positionId: string
  employeeNo: string
  name: string
  email: string
  phone: string
  status: string
}

export const defaultEmployeeForm: EmployeeFormState = {
  enterpriseId: '',
  departmentId: '',
  positionId: '',
  employeeNo: '',
  name: '',
  email: '',
  phone: '',
  status: '0',
}

export function EmployeeFormDialog({
  crud,
  enterprises,
  departments,
  positions,
}: {
  crud: ReturnType<typeof useCrudForm<EmployeeFormState, Employee>>
  enterprises: EnterpriseOption[]
  departments: DepartmentOption[]
  positions: PositionOption[]
}) {
  const t = useTranslations('Enterprise')
  const tc = useTranslations('Common')
  const entityName = t('common.employeeEntity')

  const statusOptions = [
    { value: 0, label: t('employee.statusActive') },
    { value: 1, label: t('employee.statusSuspended') },
    { value: 2, label: t('employee.statusResigned') },
  ]

  const filteredDepts = departments.filter(d => d.enterpriseId === crud.form.enterpriseId)
  const filteredPositions = positions.filter(p =>
    p.enterpriseId === crud.form.enterpriseId &&
    (!crud.form.departmentId || p.departmentId === crud.form.departmentId),
  )

  const enterpriseOptions = enterprises.map(e => ({ value: e.id, label: e.name }))
  const deptOptions = filteredDepts.map(d => ({
    value: d.id,
    label: d.fullPath ?? d.name,
    description: d.level ? `L${d.level} · ${d.code}` : d.code,
  }))
  const positionOptions = filteredPositions.map(p => ({
    value: p.id,
    label: p.name,
    description: p.level ? `${p.code} · ${p.level}` : p.code,
  }))

  const missingFields = missingLabels({
    [t('common.belongEnterpriseLabel')]: !!crud.form.enterpriseId,
    [t('employee.formEmployeeNoLabel')]: !!crud.form.employeeNo,
    [t('employee.formNameLabel')]: !!crud.form.name,
  })

  return (
    <CrudFormDialog
      crud={crud}
      title={{ create: t('common.createEntityTitle', { entityName }), edit: t('common.editEntityTitle', { entityName }) }}
      contentClassName="sm:max-w-[560px]"
      canSubmit={missingFields.length === 0}
      missingFields={missingFields}
    >
      <div className="space-y-1.5">
        <Label>{t('common.belongEnterpriseLabel')}</Label>
        <Combobox
          options={enterpriseOptions}
          value={crud.form.enterpriseId}
          onChange={v => crud.set({ enterpriseId: v, departmentId: '', positionId: '' })}
          placeholder={t('common.selectEnterprise')}
          searchPlaceholder={t('common.searchEnterprise')}
          emptyText={t('common.enterpriseNotFound')}
        />
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('employee.formBelongDepartmentLabel')}</Label>
          <Combobox
            options={deptOptions}
            value={crud.form.departmentId}
            onChange={v => crud.set({ departmentId: v, positionId: '' })}
            placeholder={t('common.selectDepartment')}
            searchPlaceholder={t('common.searchDepartment')}
            emptyText={t('common.selectEnterpriseFirst')}
            disabled={!crud.form.enterpriseId}
            clearable
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('common.positionEntity')}</Label>
          <Combobox
            options={positionOptions}
            value={crud.form.positionId}
            onChange={v => crud.set({ positionId: v })}
            placeholder={t('common.selectPosition')}
            searchPlaceholder={t('common.searchPosition')}
            emptyText={t('common.noAvailablePosition')}
            disabled={!crud.form.enterpriseId}
            clearable
          />
        </div>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{t('employee.formEmployeeNoLabel')}</Label>
          <Input
            value={crud.form.employeeNo}
            onChange={e => crud.set({ employeeNo: e.target.value })}
            placeholder={t('employee.employeeNoPlaceholder')}
            className="font-mono"
          />
        </div>
        <div className="space-y-1.5">
          <Label>{t('employee.formNameLabel')}</Label>
          <Input value={crud.form.name} onChange={e => crud.set({ name: e.target.value })} placeholder={t('employee.namePlaceholder')} />
        </div>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label>{tc('email')}</Label>
          <Input
            type="email"
            value={crud.form.email}
            onChange={e => crud.set({ email: e.target.value })}
            placeholder={t('employee.formEmailPlaceholder')}
          />
        </div>
        <div className="space-y-1.5">
          <Label>{tc('phone')}</Label>
          <Input value={crud.form.phone} onChange={e => crud.set({ phone: e.target.value })} placeholder={t('employee.formPhonePlaceholder')} />
        </div>
      </div>
      <div className="space-y-1.5">
        <Label>{t('common.statusColumnHeader')}</Label>
        <Select value={crud.form.status} onValueChange={v => crud.set({ status: v })}>
          <SelectTrigger className="h-9">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {statusOptions.map(s => (
              <SelectItem key={s.value} value={String(s.value)}>{s.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </CrudFormDialog>
  )
}
