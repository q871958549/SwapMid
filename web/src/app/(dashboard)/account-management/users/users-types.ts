// 账号管理（ABP Identity 用户）相关类型与表单默认值。
// 由 client / users-columns / users-dialogs 共享，避免循环依赖（纯类型/常量，无 'use client'）。

/** 账号安全设置（settings/security）：初始密码 + 首登强制改密开关 + 密码复杂度规则。 */
export interface SecuritySettings {
  defaultPassword: string
  forceChangePasswordOnFirstLogin: boolean
  passwordRequiredLength: number
  passwordRequireDigit: boolean
  passwordRequireLowercase: boolean
  passwordRequireUppercase: boolean
  passwordRequireNonAlphanumeric: boolean
}

export interface AbpUser {
  id: string
  userName: string
  name: string
  surname: string | null
  email: string
  phoneNumber: string | null
  isActive: boolean
  roleNames?: string[]
  boundEmployeeId?: string | null
  boundEmployeeNo?: string | null
  boundEmployeeName?: string | null
  boundEnterpriseName?: string | null
  boundDepartmentName?: string | null
  creationTime: string
}

export interface AbpRole {
  id: string
  name: string
  isDefault: boolean
  isStatic: boolean
  isPublic: boolean
}

export interface EmployeeForLink {
  id: string
  userId: string | null
  employeeNo: string
  name: string
  enterpriseId: string
  enterpriseName: string | null
  departmentId: string | null
  departmentName?: string | null
  email?: string | null
  phone?: string | null
  position?: string | null
  status?: number
}

export interface DepartmentForFilter {
  id: string
  name: string
  enterpriseName?: string | null
  code?: string | null
}

export interface FormState {
  userName: string
  name: string
  email: string
  phoneNumber: string
  password: string
  isActive: boolean
  employeeId: string
  roleNames: string[]
}

export const defaultForm: FormState = {
  userName: '', name: '', email: '',
  phoneNumber: '', password: '', isActive: true, employeeId: '', roleNames: [],
}
