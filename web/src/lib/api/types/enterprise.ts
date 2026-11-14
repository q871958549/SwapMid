import type { Require } from './_require'

export type EnterpriseDto = Require<
  import('../generated').SwapMidEnterpriseManagementEnterpriseDto,
  'id' | 'code' | 'name' | 'isMain' | 'status' | 'isEnabled' | 'creationTime'
>
export type CompanyDepartmentDto = Require<
  import('../generated').SwapMidEnterpriseManagementCompanyDepartmentDto,
  'id' | 'enterpriseId' | 'code' | 'name' | 'level' | 'order' | 'isEnabled' | 'creationTime'
>
export type PositionDto = Require<
  import('../generated').SwapMidEnterpriseManagementPositionDto,
  'id' | 'enterpriseId' | 'departmentId' | 'code' | 'name' | 'order' | 'isEnabled' | 'creationTime'
>
export type EmployeeDto = Require<
  import('../generated').SwapMidEnterpriseManagementEmployeeDto,
  'id' | 'enterpriseId' | 'employeeNo' | 'name' | 'status' | 'creationTime'
>
export interface EmployeeLookupDto {
  id: string
  name: string
  employeeNo: string
  departmentName?: string | null
  status: number
}
