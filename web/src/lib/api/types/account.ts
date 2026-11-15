import type { Require } from './_require'

// ===== 账号权限（AccountManagement 域）=====
// 账号页点击绑定员工标签查看基本信息（GET /account-users/{id}/bound-employee）；status 复用 EmployeeStatus（0=在职/1=停薪留职/2=离职）。
export type BoundEmployeeInfoDto = Require<
  import('../generated').SwapMidAccountManagementBoundEmployeeInfoDto,
  'employeeId' | 'employeeNo' | 'name' | 'status'
>
// 通用审计列批量姓名解析：后端只暴露 Id/DisplayName/UserName，不扩成联系方式或组织目录。
export type AuditUserLookupDto = Require<
  import('../generated').SwapMidAccountManagementAuditUserLookupDto,
  'id' | 'displayName' | 'userName'
>
// 角色列表/CRUD（AccountRoleAppService，GET/POST/PUT/DELETE /api/v1/account-roles + 复制 POST {id}/copy）：
// 薄代理 ABP IIdentityRoleAppService + Description(ExtraProperties) + MemberCount/PermissionCount 聚合。
// concurrencyStamp 视为必有（持久化角色恒带并发戳，PUT 回传用）；description 保持可空（业务上确可为空）。
export type AccountRoleDto = Require<
  import('../generated').SwapMidAccountManagementAccountRoleDto,
  'id' | 'name' | 'isDefault' | 'isStatic' | 'isPublic' | 'creationTime' | 'memberCount' | 'permissionCount' | 'concurrencyStamp'
>
export type AccountRoleCreateUpdateDto = import('../generated').SwapMidAccountManagementAccountRoleCreateUpdateDto
export type AccountRoleCopyDto = Require<
  import('../generated').SwapMidAccountManagementAccountRoleCopyDto,
  'name'
>

// P1：权限项/分组（角色权限视图 GET /account-roles/{roleName}/permissions 与用户有效权限视图
// GET /account-users/{userId}/effective-permissions 共用同一形状）。parentName 用 Omit+交叉类型
// 单独声明为 `string | null`（而非塞进 Require 列表——Require 会把 null 一并剥掉，
// 但根权限的 parentName 语义上就是合法 null，不是"未初始化"）。
export type AccountPermissionProviderDto = import('../generated').SwapMidAccountManagementAccountPermissionProviderDto
export type AccountPermissionUpdateDto = import('../generated').SwapMidAccountManagementAccountPermissionUpdateDto
export type AccountPermissionDto = Omit<
  Require<
    import('../generated').SwapMidAccountManagementAccountPermissionDto,
    'name' | 'displayName' | 'isGranted'
  >,
  'parentName'
> & { parentName: string | null }
export type AccountPermissionGroupDto = Omit<
  Require<
    import('../generated').SwapMidAccountManagementAccountPermissionGroupDto,
    'name' | 'displayName'
  >,
  'permissions'
> & { permissions: AccountPermissionDto[] }
// 角色权限视图（P1-3 角色对比复用现有 GET /account-roles/{roleName}/permissions）。
export type AccountRolePermissionDto = Omit<
  Require<
    import('../generated').SwapMidAccountManagementAccountRolePermissionDto,
    'roleName'
  >,
  'groups'
> & { groups: AccountPermissionGroupDto[] }
export type PermissionGuideKind = 0 | 1 | 2 | 3
export interface PermissionGuideDto {
  name: string
  kind: PermissionGuideKind
  impact: string
  suggested?: string | null
  confuseWith?: string | null
}
// 用户有效权限视图（P1-4，每项权限的 grantedProviders 标注授予来源：R+角色名 / U+直授）。
export type AccountUserPermissionDto = Omit<
  Require<
    import('../generated').SwapMidAccountManagementAccountUserPermissionDto,
    'userId'
  >,
  'groups'
> & { groups: AccountPermissionGroupDto[] }
// 角色权限变更历史（P1-2，从 AuditLog.Comments 语义化 diff 提取；changes 可能为空数组）。
export type RolePermissionChangeDto = Require<
  import('../generated').SwapMidAccountManagementRolePermissionChangeDto,
  'executionTime' | 'changes'
>
export interface PasswordPolicyDto {
  requiredLength: number
  requireDigit: boolean
  requireLowercase: boolean
  requireUppercase: boolean
  requireNonAlphanumeric: boolean
}
export type ExternalAuthProvidersDto = import('../generated').SwapMidAccountManagementExternalAuthProvidersDto
