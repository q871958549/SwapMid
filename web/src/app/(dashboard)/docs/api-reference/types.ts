// API 参考页的数据类型。手写的设计规范（client.tsx）与自动生成的接口数据
// （api-doc-data.generated.ts，由 scripts/gen-api-doc.mjs 产出）共用这些类型。
//
// 文档分两栏（见 client.tsx 顶部切换）：
//   ① 系统接口参考 = 自动生成的接口字段表（ApiModule/ApiSection/ApiEndpoint）
//      + 手写覆盖层（DomainOverview，来自 api-doc-overrides.ts，渲染在接口列表之上）。
//   ② 集成指南     = 面向外部集成方的场景化文档（IntegrationGuide，来自 integration-guides.ts）。

export interface ApiField {
  name: string
  /** 中文名：自动生成的接口为空，手写内容可填 */
  cnName: string
  type: string
  /** 请求 / Query 字段：调用时是否必传 */
  required?: boolean
  /** 响应字段：返回值是否可能为 null */
  nullable?: boolean
  description: string
  example?: string
}

export interface ApiEndpoint {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  path: string
  summary: string
  /**
   * 是否来自 OpenAPI / 方法 XML summary。
   * false = 仅脚本按方法+路径的软推断，页面应标「待补」，不可当真实标题。
   * 缺省按 true 兼容旧生成物 / 手写覆盖。
   */
  summaryDocumented?: boolean
  description?: string
  queryParams?: ApiField[]
  requestBody?: ApiField[]
  responseFields?: ApiField[]
  note?: string
}

export interface ApiSection {
  id: string
  title: string
  endpoints: ApiEndpoint[]
}

export interface ApiModule {
  id: string
  title: string
  description: string
  baseUrl: string
  sections: ApiSection[]
  /**
   * 手写覆盖层（api-doc-overrides.ts 按域 id 注入）：领域概述 + 主线编排 + 关键约束，
   * 渲染在自动生成的接口列表之上。把「零件清单」升级为「装配说明」的关键，
   * gen:api-doc 永不覆盖本字段。
   */
  overview?: DomainOverview
}

// ─── 手写覆盖层（系统参考增强）──────────────────────────────────────────────

/** 域级概述：让读者在看字段表之前先理解这个域是干什么的、接口怎么编排。 */
export interface DomainOverview {
  /** 领域定位（一段，比 description 详细）。 */
  summary: string
  /** 主线编排流程（有序步骤），如「企业→部门→岗位→员工」的建立顺序。 */
  workflow?: WorkflowStep[]
  /** 关键约束 / 易错点提示。 */
  notes?: string[]
}

export interface WorkflowStep {
  /** 步骤标题，如「1. 建立企业主体」。 */
  title: string
  /** 步骤说明。 */
  detail: string
  /** 关联接口，形如 "POST /api/v1/enterprises"（用于对照，可多个）。 */
  endpoints?: string[]
}

// ─── 集成指南（对外场景化文档）─────────────────────────────────────────────

/** 一个对外集成场景（如「员工树洞匿名申诉」）。按场景而非按 controller 组织。 */
export interface IntegrationGuide {
  id: string
  /** 场景标题。 */
  title: string
  /** 一句话说明这个场景是什么、解决什么问题。 */
  intro: string
  /** 调用方 / 受众（如「微信小程序 / 手机浏览器匿名访客」）。 */
  audience: string
  /** 鉴权方式（如「匿名」「一次性 token」「Bearer token」）。 */
  auth: string
  /** 基础路径前缀（如 /out/appeal）。 */
  baseUrl: string
  /** 按序调用步骤——集成指南的核心，回答「我该按什么顺序调哪几个接口」。 */
  steps: IntegrationStep[]
  /** 通用注意事项（返回格式约定、强依赖、坑）。 */
  notes?: string[]
}

export interface IntegrationStep {
  /** 步骤标题，如「1. 校验工号」。 */
  title: string
  method: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  path: string
  /** 这一步做什么。 */
  description: string
  /** 鉴权（仅当与场景默认不同时填，如管理端步骤需 token）。 */
  auth?: string
  /** 请求示例（curl 或 JSON 文本，原样展示）。 */
  request?: string
  /** 响应示例（JSON 文本，原样展示）。 */
  response?: string
  /** 这一步的业务规则 / 易错点。 */
  note?: string
}
