// P1-2 角色权限变更历史：纯函数（审计评论行解析 + 时间格式化），与 UI 解耦，供 role-detail-sheet.tsx 使用。
// 后端 AccountRoleAppService.ExtractChangeLines 已从 AuditLog.Comments 剥离固定前缀
// `[RolePermissionChange] `，`RolePermissionChangeDto.changes` 里每行剩余形如
// `{roleName} +[a,b] -[c]`（+/- 段均可能缺省；实测：roleName 是否也被剥离与该 DTO 的
// XML 注释描述不一致——注释写"已剥离固定前缀与角色名"，但实现只 Slice 掉了固定前缀，
// roleName 仍留在行首）。用括号锚点正则提取，不依赖 roleName 是否存在于行中，对两种
// 形态都健壮，不受该文档/实现差异影响。

export interface ParsedPermissionChange {
  /** 本次操作新增授予的权限名（全名，未剥前缀）。 */
  added: string[]
  /** 本次操作移除授予的权限名（全名，未剥前缀）。 */
  removed: string[]
}

const ADDED_RE = /\+\[([^\]]*)\]/
const REMOVED_RE = /-\[([^\]]*)\]/

/** 解析单行变更摘要，提取 +[...]/-[...] 段内的权限名列表；缺省段或空列表返回空数组。 */
export function parsePermissionChangeLine(line: string): ParsedPermissionChange {
  const addedRaw = line.match(ADDED_RE)?.[1] ?? ''
  const removedRaw = line.match(REMOVED_RE)?.[1] ?? ''
  return {
    added: addedRaw ? addedRaw.split(',').filter(Boolean) : [],
    removed: removedRaw ? removedRaw.split(',').filter(Boolean) : [],
  }
}

/** 统一日期时间格式化（yyyy-MM-dd HH:mm:ss）；审计时间戳精确到秒消歧，空值/无法解析原样兜底。 */
export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return value
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}
