/**
 * 账号列表单页上限。必须与后端 `AccountManagementConsts.Users.ListMaxPageSize` 一致，
 * 否则下拉里会留着一个点了也只返回 200 行的更大档——那是明摆着的谎。
 */
export const ACCOUNT_USER_LIST_MAX_PAGE_SIZE = 200
