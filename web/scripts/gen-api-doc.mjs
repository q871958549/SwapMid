// 从 OpenAPI 契约快照（docs/api/openapi.generated.json）生成「开发者文档 · API 参考」页的数据。
//
// 为什么存在：接口文档的事实源是后端运行时 Swagger（见 ADR-0002）。此前文档页是手写硬编码，
// 只覆盖 5 个模块且会随后端漂移。本脚本把全部 /api/v1 业务接口（当前 280 个 / 52 个 controller）
// 自动解析、按业务域分组，输出为页面直接消费的数据文件——全覆盖、不漂移。
//
// 用法：
//   pnpm gen:api-doc           # 仅重生成文档数据（要求快照已存在）
//   pnpm sync:api              # 契约快照 + 类型 + 文档数据 + 门户 一键全刷
//
// 输入：../docs/api/openapi.generated.json
// 输出：src/app/(dashboard)/docs/api-reference/api-doc-data.generated.ts
//
// 设计取舍：ABP 反射出的 swagger 字段无中文名/说明（除非后端写了 DTO XML 注释 → 进 description）。
// 故自动文档以「字段名 + 类型 + 必填 + 枚举值」为骨架，中文随后端注释渐进丰满；
// 接口 summary 缺失时仍按 HTTP 方法 + 路径给出「软推断」文案，但同时标 summaryDocumented=false，
// 页面显示「待补」——禁止把兜底文案伪装成真实标题（与表字典「待补」范式对齐）。
// 业务「域名 / controller 中文名」在下方人工维护。
import { readFileSync, writeFileSync } from 'node:fs'
import { apiResponseFieldSchema, shouldDocumentApiField } from './api-doc-field-policy.mjs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..') // web/
const SPEC = resolve(ROOT, '../docs/api/openapi.generated.json')
const OUT = resolve(ROOT, 'src/app/(dashboard)/docs/api-reference/api-doc-data.generated.ts')

// ── 业务域定义（对齐根 CLAUDE.md《业务模块地图》，有序）。新增 controller 时在对应域 tags 补一行 ──
const DOMAINS = [
  { id: 'data-integration', title: '数据集成', description: '多源关系型/REST/文件连接管理、流式 ETL 管道、多源与 Push Webhook CDC、Cron 调度、目标回执对账与执行恢复。', tags: ['DataSource', 'DataPipeline', 'DataPipelineSchemaPreflight', 'PipelineExecution', 'CdcStream', 'CdcWebhookIngress'] },
  { id: 'message-bus', title: '消息总线', description: '主题发布、订阅过滤与 HTTP Webhook / RabbitMQ / 通用 Bot Webhook 可靠投递与重放。', tags: ['MessageTopic', 'MessageSubscription', 'MessageRecord'] },
  { id: 'api-management', title: 'API 网关与管理', description: '对外 API 分组、路由转发、在线调试台与 OpenIddict 客户端凭据。', tags: ['ApiGroup', 'ApiRoute', 'ThirdPartyClient'] },
  { id: 'enterprise', title: '组织主数据', description: '企业、部门、岗位与员工基础主数据。', tags: ['Enterprise', 'CompanyDepartment', 'Position', 'Employee', 'EmployeeLookup'] },
  { id: 'account', title: '账号与权限', description: 'ABP Identity 账号、角色、细粒度权限矩阵、密码策略、外部 OIDC 联邦与受控代登录。', tags: ['AccountUser', 'AuditUserLookup', 'AccountRole', 'AccountPermission', 'AccountSettings', 'CurrentUserPermission', 'PermissionGuide', 'AuthProvider'] },
  { id: 'system', title: '系统运维', description: '编码规则、两级数据字典、定时任务调度、审计日志与系统健康检查。', tags: ['CodeRule', 'DataDictionary', 'JobScheduling', 'ScheduledJobExecution', 'AuditLog', 'SystemHealth'] },
  { id: 'notification', title: '通知中心', description: '通用 Bot Webhook 与 SMTP 邮件渠道、分级接收人、通知日志及 Prometheus Alertmanager 告警接入。', tags: ['NotificationSettings', 'NotificationRecipient', 'NotificationLog', 'AlertWebhook'] },
  { id: 'developer-docs', title: '开发者文档', description: 'EF Core 模型与外部库实时反射表字典。', tags: ['DbSchema'] },
  { id: 'layout', title: '个人与界面偏好', description: '导航排序、多页签、主题字号语言偏好、表格列视图与头像。', tags: ['NavPreference', 'NavUsage', 'UserPreference', 'TablePreference', 'Avatar'] },
]

const TAG_CN = {
  DataSource: '数据源', DataPipeline: '数据管道', DataPipelineSchemaPreflight: '管道结构预检与连接器能力', PipelineExecution: '管道执行记录', CdcStream: '多源 CDC 实时流', CdcWebhookIngress: 'Push Webhook CDC 事件接入',
  MessageTopic: '消息主题', MessageSubscription: '消息订阅', MessageRecord: '消息投递记录',
  ApiGroup: 'API 分组', ApiRoute: 'API 路由', ThirdPartyClient: '第三方客户端凭据',
  Enterprise: '企业', CompanyDepartment: '部门', Position: '岗位', Employee: '员工', EmployeeLookup: '员工检索目录',
  AccountUser: '账号', AuditUserLookup: '审计用户检索', AccountRole: '角色', AccountPermission: '权限',
  AccountSettings: '账号安全设置', CurrentUserPermission: '当前用户权限', PermissionGuide: '权限说明', AuthProvider: '身份认证提供商',
  CodeRule: '编码规则', DataDictionary: '数据字典', JobScheduling: '定时任务', ScheduledJobExecution: '定时任务执行历史',
  AuditLog: '审计日志', SystemHealth: '系统体检',
  NotificationSettings: '通知渠道设置', NotificationRecipient: '通知接收人', NotificationLog: '通知发送日志', AlertWebhook: 'Alertmanager 告警接入',
  DbSchema: '数据库表字典',
  NavPreference: '导航偏好', NavUsage: '导航使用统计', UserPreference: '界面偏好', TablePreference: '列表视图偏好', Avatar: '个人头像',
}

// 自定义动作后缀 → 中文（用于无 summary 时的兜底，如 POST /xxx/{id}/publish）
const ACTIONS = {
  'set-as-main': '设为主体', publish: '发布', trigger: '触发', replay: '重放', execute: '执行',
  cancel: '取消', enable: '启用', disable: '停用', 'check-out': '退宿', provision: '开通账号',
  sync: '同步', export: '导出', import: '导入', batch: '批量操作', main: '主体企业',
  preflight: '前置预检', activate: '激活', pause: '暂停', capture: '执行捕获', checkpoint: '重置位点',
}
// 全局通用字段中文说明（字段无后端 XML 注释时套用）。通用字段在此补一行；业务特有字段靠后端 DTO XML 注释
const FIELD_DICT = {
  // 分页 / 排序 / 时间范围（全平台统一 Query 约定）
  filter: '关键字过滤（按名称/编码模糊搜索）',
  sorting: '排序表达式，如 creationTime desc、name asc',
  skipCount: '跳过条数（分页偏移），默认 0',
  maxResultCount: '每页条数，默认 10、最大 1000',
  startTime: '时间范围开始，yyyy-MM-dd HH:mm:ss',
  endTime: '时间范围结束，yyyy-MM-dd HH:mm:ss',
  timeType: '时间筛选针对的字段',
  // 主键 / 编码 / 名称 / 通用属性
  id: '唯一标识',
  code: '业务编码',
  name: '名称',
  shortName: '简称',
  description: '描述',
  remark: '备注',
  order: '排序号（越小越靠前）',
  isEnabled: '是否启用',
  isMain: '是否主体',
  status: '状态',
  type: '类型',
  email: '邮箱',
  phone: '手机号',
  // 组织关联（企业 / 部门 / 岗位 / 员工）
  enterpriseId: '企业 ID', enterpriseCode: '企业编码', enterpriseName: '企业名称',
  departmentId: '部门 ID', departmentCode: '部门编码', departmentName: '部门名称',
  parentId: '上级 ID', parentCode: '上级编码', parentName: '上级名称',
  positionId: '岗位 ID', positionCode: '岗位编码', positionName: '岗位名称',
  employeeId: '员工 ID', employeeNo: '工号', employeeName: '姓名', userId: '关联账号 ID',
  // 审计
  creationTime: '创建时间',
  creatorId: '创建人 ID',
  lastModificationTime: '最后修改时间',
  lastModifierId: '最后修改人 ID',
}
// 基类字段过滤见 api-doc-field-policy.mjs；必填请求字段与业务版本字段必须展示。
// 审计字段：保留但排到字段表末尾，业务字段优先
const AUDIT_FIELDS = ['creationTime', 'creatorId', 'lastModificationTime', 'lastModifierId']
const METHOD_ORDER = { GET: 0, POST: 1, PUT: 2, PATCH: 3, DELETE: 4 }

const spec = JSON.parse(readFileSync(SPEC, 'utf8'))
const schemas = spec.components?.schemas ?? {}

function schemaByRef(ref) {
  if (!ref) return null
  return schemas[ref.split('/').pop()] ?? null
}
function deref(s) {
  if (!s) return null
  return s.$ref ? schemaByRef(s.$ref) : s
}
function shortName(full) {
  return String(full).split('.').pop().split('`')[0]
}
function lcFirst(s) {
  return s ? s[0].toLowerCase() + s.slice(1) : s
}
function typeOf(s) {
  if (!s) return 'object'
  if (s.$ref) return shortName(s.$ref.split('/').pop())
  if (s.type === 'array') return typeOf(s.items) + '[]'
  if (s.type === 'string' && s.format === 'uuid') return 'Guid'
  if (s.type === 'string' && s.format === 'date-time') return 'DateTime'
  if (s.type === 'integer') return 'integer'
  return s.type ?? 'object'
}
function enumHint(s) {
  const t = s?.$ref ? schemaByRef(s.$ref) : null
  if (!t?.enum) return ''
  // EnumSchemaFilter 把成员表追加为 description 末行（前面可能有枚举类型级 XML summary）；取末行即成员表
  if (t.description) return '取值：' + t.description.split('\n').pop().trim()
  return '枚举值：' + t.enum.join(' / ')
}
// 字段说明：后端 XML 注释 > 全局词典；再附加枚举取值。
// enum 字段特殊处理：Swashbuckle 会把 enum schema 的 description（filter 注入的取值表）
// 复制到引用处的 description，直接用作 base 会与 en 重复，故 enum 字段只取词典名 + 枚举取值。
function composeDesc(name, ownDesc, enumSchema) {
  const en = enumHint(enumSchema)
  const base = en ? (FIELD_DICT[name] ?? '') : (ownDesc ?? FIELD_DICT[name] ?? '')
  return [base, en].filter(Boolean).join(base && en ? '；' : '')
}
function fieldsFrom(schema, source, prefix = '', depth = 0) {
  const s = deref(schema)
  if (!s?.properties) return []
  const req = new Set(s.required ?? [])
  const fields = []
  for (const [n, p] of Object.entries(s.properties)) {
    if (!shouldDocumentApiField(n, p, source, req.has(n))) continue
    const name = prefix + n
    const f = { name, cnName: '', type: typeOf(p), description: composeDesc(n, p.description, p) }
    if (source === 'request') f.required = req.has(n)
    else f.nullable = p.nullable === true
    fields.push(f)
    // 嵌套对象 / 对象数组展开一层，避免 CreateXxxDto 里的子对象只显示类型名
    if (depth >= 1) continue
    const nested = deref(p)
    const items = nested?.type === 'array' ? deref(nested.items) : nested
    if (items?.properties && !items.enum) {
      const childPrefix = nested?.type === 'array' ? `${name}[].` : `${name}.`
      fields.push(...fieldsFrom(items, source, childPrefix, depth + 1))
    }
  }
  const lastSeg = (f) => f.name.split('.').pop()
  return [
    ...fields.filter((f) => !AUDIT_FIELDS.includes(lastSeg(f))),
    ...fields.filter((f) => AUDIT_FIELDS.includes(lastSeg(f))),
  ]
}
function responseFields(schema) {
  const s = apiResponseFieldSchema(deref(schema))
  return s ? fieldsFrom(s, 'response') : []
}
function paramFields(op) {
  return (op.parameters ?? [])
    .filter((p) => p.in === 'query' || p.in === 'path')
    .map((p) => ({
      name: lcFirst(p.name),
      cnName: '',
      type: typeOf(p.schema ?? {}),
      required: !!p.required || p.in === 'path',
      description: composeDesc(lcFirst(p.name), p.description, p.schema ?? {}),
    }))
}
/** @returns {{ summary: string, summaryDocumented: boolean }} */
function summaryFor(method, path, op) {
  if (typeof op.summary === 'string' && op.summary.trim()) {
    return { summary: op.summary.trim(), summaryDocumented: true }
  }
  const m = method.toLowerCase()
  const segs = path.split('/').filter(Boolean)
  const last = segs[segs.length - 1]
  const isId = /^\{.+\}$/.test(last)
  const hasId = segs.some((s) => /^\{.+\}$/.test(s))
  let soft = m.toUpperCase()
  if (!isId && ACTIONS[last]) soft = ACTIONS[last]
  else if (!isId && hasId) soft = last // 资源上的自定义动作，原样给出
  else if (m === 'get') soft = isId ? '查询详情' : '查询列表'
  else if (m === 'post') soft = isId ? '操作' : '新建'
  else if (m === 'put') soft = '更新'
  else if (m === 'patch') soft = '部分更新'
  else if (m === 'delete') soft = '删除'
  return { summary: soft, summaryDocumented: false }
}
function bodySchema(op) {
  const c = op.requestBody?.content
  return c?.['application/json']?.schema ?? null
}
function okSchema(op) {
  const c = op.responses?.['200']?.content ?? op.responses?.['201']?.content
  return c?.['application/json']?.schema ?? null
}
function resourcePath(paths) {
  return paths
    .map((p) => p.replace(/\/\{[^}]+\}.*$/, '').replace(/^\/api\/v[1-9]\d*/, ''))
    .sort((a, b) => a.length - b.length)[0] || ''
}

// ── 收集：tag → { paths:Set, endpoints:[] } ──
const byTag = {}
for (const [path, ops] of Object.entries(spec.paths)) {
  if (!/^\/api\/v[1-9]\d*\//.test(path)) continue
  for (const [method, op] of Object.entries(ops)) {
    if (!METHOD_ORDER.hasOwnProperty(method.toUpperCase())) continue
    const tag = op.tags?.[0] ?? 'Uncategorized'
    const bucket = (byTag[tag] ??= { paths: new Set(), endpoints: [] })
    bucket.paths.add(path)
    const { summary, summaryDocumented } = summaryFor(method, path, op)
    const ep = { method: method.toUpperCase(), path, summary, summaryDocumented }
    if (summaryDocumented && op.description && op.description !== op.summary) ep.description = op.description
    const q = paramFields(op)
    if (q.length) ep.queryParams = q
    const body = fieldsFrom(bodySchema(op), 'request')
    if (body.length) ep.requestBody = body
    const resp = responseFields(okSchema(op))
    if (resp.length) ep.responseFields = resp
    bucket.endpoints.push(ep)
  }
}
for (const b of Object.values(byTag)) {
  b.endpoints.sort((a, z) => (a.path === z.path ? METHOD_ORDER[a.method] - METHOD_ORDER[z.method] : a.path.localeCompare(z.path)))
}

// ── 组装模块 ──
function sectionFor(tag) {
  const b = byTag[tag]
  return { id: tag, title: `${TAG_CN[tag] ?? tag} ${resourcePath([...b.paths])}`.trim(), endpoints: b.endpoints }
}
function baseUrlFor(sections) {
  const versions = new Set(
    sections.flatMap((section) => section.endpoints
      .map((endpoint) => endpoint.path.match(/^\/api\/(v[1-9]\d*)\//)?.[1])
      .filter(Boolean)),
  )
  return [...versions].sort().map((version) => `/api/${version}`).join(' · ')
}
const modules = []
for (const d of DOMAINS) {
  const sections = d.tags.filter((t) => byTag[t]).map(sectionFor)
  if (sections.length) modules.push({ id: d.id, title: d.title, description: d.description, baseUrl: baseUrlFor(sections), sections })
}
// 兜底：未归入任何域的 tag，集中到「其他」域，确保零遗漏
const known = new Set(DOMAINS.flatMap((d) => d.tags))
const others = Object.keys(byTag).filter((t) => !known.has(t)).sort()
if (others.length) {
  const sections = others.map(sectionFor)
  modules.push({ id: 'others', title: '其他', description: '尚未归入业务域的接口（请在 gen-api-doc.mjs 的 DOMAINS 中补充归类）。', baseUrl: baseUrlFor(sections), sections })
  console.warn(`[gen:api-doc] ⚠ ${others.length} 个 tag 未归类，已落入「其他」域：${others.join(', ')}`)
}

const epCount = Object.values(byTag).reduce((n, b) => n + b.endpoints.length, 0)
const header = `/* eslint-disable */
// ⚠ 本文件由 web/scripts/gen-api-doc.mjs 从 docs/api/openapi.generated.json 自动生成，请勿手改。
// 刷新：pnpm gen:api-doc（或 pnpm sync:api 一并刷新 契约快照 / 类型 / 文档数据 / 门户）。
import type { ApiModule } from './types'

export const API_MODULES: ApiModule[] = `
writeFileSync(OUT, header + JSON.stringify(modules, null, 2) + '\n', 'utf8')
console.log(`[gen:api-doc] 已生成 ${epCount} 个接口 / ${modules.length} 个业务域 → src/app/(dashboard)/docs/api-reference/api-doc-data.generated.ts`)
