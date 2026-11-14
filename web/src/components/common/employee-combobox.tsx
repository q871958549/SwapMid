'use client'

import { useCallback, useEffect, useState } from 'react'
import { useTranslations } from 'next-intl'
import { RemoteCombobox, type RemoteComboboxOption } from '@/components/ui/remote-combobox'

/** 选人目录瘦 DTO，不含邮箱/手机。契约见 GET /api/v1/employee-lookup。 */
type EmployeeLookupItem = {
  id: string
  name?: string
  employeeNo?: string
  departmentName?: string | null
  status?: number
}

interface EmployeeComboboxProps {
  /** 选中的员工 Id（'' 表示未选） */
  value?: string
  /** 已选员工姓名（远程搜索选项易失，由外部持有保证回显） */
  selectedName?: string
  /** 选中回调：员工 Id + 姓名（清空时两者均为 ''） */
  onChange: (employeeId: string, employeeName: string) => void
  placeholder?: string
  disabled?: boolean
  /** 允许清空（默认 true——负责人可清空退回未关联） */
  clearable?: boolean
  /** 选项标注在职状态（默认 false）：离职/停薪留职员工在 description 尾部加标注。绩效继承等离职交接场景开启，防选错源员工。 */
  showStatus?: boolean
  /**
   * 限定部门：只搜该部门及下级部门的员工（走 employee-lookup?departmentId=&includeChildDepartments=true）。
   * 部门切换后由调用方清空已选员工——本组件不窥探旧值是否仍属于新部门。
   */
  departmentId?: string | null
  /** 为 true 时未选部门则禁用，避免先选出「全公司」再改部门造成错配。 */
  requireDepartment?: boolean
  /** 显示已选员工的工号/部门；重新打开表单时按姓名有界查询并精确匹配 Id。 */
  showSelectedDetails?: boolean
  /** 业务自行说明搜索范围时关闭通用提示，默认保持原行为。 */
  showDepartmentScopeHint?: boolean
  className?: string
}

/**
 * 员工远程搜索选择器（全平台共享）：打登录即可用的选人目录，不要求员工管理权限。
 * 选中得到员工 Id + 权威姓名。项目负责人、样品责任人、流转接收人等
 * 「人要关联主数据而非自由文本」的场景一律用它（个人台账/责任模型依赖员工 Id 归集）。
 */
export function EmployeeCombobox({
  value,
  selectedName,
  onChange,
  placeholder,
  disabled = false,
  clearable = true,
  showStatus = false,
  departmentId,
  requireDepartment = false,
  showSelectedDetails = false,
  showDepartmentScopeHint = true,
  className,
}: EmployeeComboboxProps) {
  const t = useTranslations('Common')
  // 搜索失败原因（403 权限不足最常见）：不能与「真没结果」混为一谈静默吞掉，
  // 否则无权限账号看到的是"未找到匹配员工"，被当成数据问题排查
  const [fetchError, setFetchError] = useState<string | null>(null)
  const [selectedDetail, setSelectedDetail] = useState<{ id: string; description?: string } | null>(null)
  const scopedDepartmentId = departmentId?.trim() || ''
  const waitingForDepartment = requireDepartment && !scopedDepartmentId

  const describeEmployee = useCallback((employee: EmployeeLookupItem) => {
    const statusSuffix: Record<number, string> = { 1: t('employeeCombobox.statusOnLeave'), 2: t('employeeCombobox.statusResigned') }
    return [employee.employeeNo, employee.departmentName,
      showStatus && employee.status != null ? statusSuffix[employee.status] : undefined,
    ].filter(Boolean).join(' · ')
  }, [showStatus, t])

  useEffect(() => {
    if (!showSelectedDetails || !value || !selectedName || selectedDetail?.id === value) return
    const controller = new AbortController()
    // 已选人可能在当前搜索部门外；补展示不受搜索范围限制，也绝不据此清空已选 Id。
    const qs = new URLSearchParams({ maxResultCount: '20', filter: selectedName })
    void fetch(`/api/proxy/employee-lookup?${qs}`, { signal: controller.signal })
      .then(async res => {
        if (!res.ok) return
        const data = await res.json() as { items?: EmployeeLookupItem[] }
        if (controller.signal.aborted) return
        const employee = data.items?.find(item => item.id === value)
        if (employee) setSelectedDetail({ id: employee.id, description: describeEmployee(employee) })
      })
      .catch(() => { /* 补充展示失败仍保留权威的员工 Id 和原姓名。 */ })
    return () => controller.abort()
  }, [showSelectedDetails, value, selectedName, selectedDetail?.id, describeEmployee])

  const fetchOptions = useCallback(async (query: string, signal: AbortSignal): Promise<RemoteComboboxOption[]> => {
    if (requireDepartment && !scopedDepartmentId) return []
    const qs = new URLSearchParams({ maxResultCount: '20' })
    if (query.trim()) qs.set('filter', query.trim())
    if (scopedDepartmentId) {
      qs.set('departmentId', scopedDepartmentId)
      qs.set('includeChildDepartments', 'true')
    }
    const res = await fetch(`/api/proxy/employee-lookup?${qs.toString()}`, { signal })
    signal.throwIfAborted()
    if (!res.ok) {
      setFetchError(res.status === 403
        ? t('employeeCombobox.noPermission')
        : t('employeeCombobox.searchFailed', { status: res.status }))
      return []
    }
    const data = (await res.json()) as { items?: EmployeeLookupItem[] }
    signal.throwIfAborted()
    setFetchError(null)
    return (data.items ?? []).map(e => ({
      value: e.id,
      label: e.name ?? '',
      description: describeEmployee(e),
    }))
  }, [describeEmployee, t, scopedDepartmentId, requireDepartment])

  return (
    <div className="space-y-1.5">
      <RemoteCombobox
        value={value}
        selectedLabel={selectedName}
        onChange={(v, option) => {
          setSelectedDetail(v ? { id: v, description: option?.description } : null)
          onChange(v, option?.label ?? '')
        }}
        fetchOptions={fetchOptions}
        scopeKey={scopedDepartmentId}
        placeholder={waitingForDepartment
          ? t('employeeCombobox.selectDepartmentFirst')
          : (placeholder ?? t('employeeCombobox.placeholder'))}
        searchPlaceholder={t('employeeCombobox.searchPlaceholder')}
        emptyText={fetchError ?? t('employeeCombobox.emptyText')}
        disabled={disabled || waitingForDepartment}
        clearable={clearable}
        className={className}
      />
      {showSelectedDetails && value && selectedDetail?.id === value && selectedDetail.description && (
        <p className="text-xs text-muted-foreground">{selectedDetail.description}</p>
      )}
      {showDepartmentScopeHint && !!scopedDepartmentId && (
        <p className="text-xs text-muted-foreground">{t('employeeCombobox.departmentScopeHint')}</p>
      )}
    </div>
  )
}
