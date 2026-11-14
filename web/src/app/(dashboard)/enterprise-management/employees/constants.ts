/**
 * 员工交互列表页的每页档位上限。必须与后端 `EnterpriseConsts.EmployeeListMaxPageSize` 一致。
 * 只约束本页 DataTable / URL，不代表 `/employees` 接口上限——该接口同时给下拉用。
 */
export const EMPLOYEE_LIST_MAX_PAGE_SIZE = 200

/**
 * 批量开通账号的分片大小。Identity 建号比普通批删重得多，不能套用 2000 的共享批删片。
 * 每片一次 HTTP，弹窗才能按片更新进度。
 */
export const ACCOUNT_PROVISION_CHUNK_SIZE = 10
