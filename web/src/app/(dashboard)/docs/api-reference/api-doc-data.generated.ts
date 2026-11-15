/* eslint-disable */
// ⚠ 本文件由 web/scripts/gen-api-doc.mjs 从 docs/api/openapi.generated.json 自动生成，请勿手改。
// 刷新：pnpm gen:api-doc（或 pnpm sync:api 一并刷新 契约快照 / 类型 / 文档数据 / 门户）。
import type { ApiModule } from './types'

export const API_MODULES: ApiModule[] = [
  {
    "id": "data-integration",
    "title": "数据集成",
    "description": "多源关系型/REST/文件连接管理、流式 ETL 管道、Cron 调度、目标回执对账与执行恢复。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "DataSource",
        "title": "数据源 /data-sources",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/data-sources",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "required": false,
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "数据源名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "数据源描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "nullable": false
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "可公开回显的配置 JSON。关系型只包含\r\nprovider/host/port/database/username/targetResourceGroup/encrypt/trustServerCertificate 白名单字段；\r\nREST API 与文件类型固定返回空对象，绝不透传存储原文。",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否存在未回显的密码、连接串、未知非空字段或不透明配置",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-sources",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "数据源名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "数据源描述",
                "required": false
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "required": true
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "完整连接配置 JSON。关系型更新仅在连接目标与 TLS 姿态不变时允许省略 password；\r\nREST API/文件类型的存量配置不可回显，更新时必须重新提交完整非空对象。",
                "required": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "数据源名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "数据源描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "nullable": false
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "可公开回显的配置 JSON。关系型只包含\r\nprovider/host/port/database/username/targetResourceGroup/encrypt/trustServerCertificate 白名单字段；\r\nREST API 与文件类型固定返回空对象，绝不透传存储原文。",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否存在未回显的密码、连接串、未知非空字段或不透明配置",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/data-sources/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "数据源名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "数据源描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "nullable": false
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "可公开回显的配置 JSON。关系型只包含\r\nprovider/host/port/database/username/targetResourceGroup/encrypt/trustServerCertificate 白名单字段；\r\nREST API 与文件类型固定返回空对象，绝不透传存储原文。",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否存在未回显的密码、连接串、未知非空字段或不透明配置",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/data-sources/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "数据源名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "数据源描述",
                "required": false
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "required": true
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "完整连接配置 JSON。关系型更新仅在连接目标与 TLS 姿态不变时允许省略 password；\r\nREST API/文件类型的存量配置不可回显，更新时必须重新提交完整非空对象。",
                "required": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "数据源名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "数据源描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "nullable": false
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "可公开回显的配置 JSON。关系型只包含\r\nprovider/host/port/database/username/targetResourceGroup/encrypt/trustServerCertificate 白名单字段；\r\nREST API 与文件类型固定返回空对象，绝不透传存储原文。",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否存在未回显的密码、连接串、未知非空字段或不透明配置",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/data-sources/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-sources/{id}/test-connection",
            "summary": "test-connection",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "required": true
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "待测试的完整连接配置 JSON；按已保存数据源测试时，只有连接目标与 TLS 姿态不变\r\n才允许省略关系型 password。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "success",
                "cnName": "",
                "type": "boolean",
                "description": "是否连接成功",
                "nullable": false
              },
              {
                "name": "message",
                "cnName": "",
                "type": "string",
                "description": "结果消息(失败时为错误原因)",
                "nullable": true
              },
              {
                "name": "elapsedMs",
                "cnName": "",
                "type": "integer",
                "description": "耗时(毫秒)",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-sources/test-connection",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "type",
                "cnName": "",
                "type": "DataSourceType",
                "description": "类型；取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "required": true
              },
              {
                "name": "config",
                "cnName": "",
                "type": "string",
                "description": "待测试的完整连接配置 JSON；按已保存数据源测试时，只有连接目标与 TLS 姿态不变\r\n才允许省略关系型 password。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "success",
                "cnName": "",
                "type": "boolean",
                "description": "是否连接成功",
                "nullable": false
              },
              {
                "name": "message",
                "cnName": "",
                "type": "string",
                "description": "结果消息(失败时为错误原因)",
                "nullable": true
              },
              {
                "name": "elapsedMs",
                "cnName": "",
                "type": "integer",
                "description": "耗时(毫秒)",
                "nullable": false
              }
            ]
          }
        ]
      },
      {
        "id": "DataPipeline",
        "title": "数据管道 /data-pipelines",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/data-pipelines",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按源数据源 ID 过滤"
              },
              {
                "name": "status",
                "cnName": "",
                "type": "PipelineStatus",
                "required": false,
                "description": "状态；取值：0=Draft（草稿）；1=Active（启用）；2=Paused（暂停）；3=Archived（归档）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "管道名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "管道描述",
                "nullable": true
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID",
                "nullable": false
              },
              {
                "name": "sourceName",
                "cnName": "",
                "type": "string",
                "description": "源数据源名称",
                "nullable": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID",
                "nullable": true
              },
              {
                "name": "targetName",
                "cnName": "",
                "type": "string",
                "description": "目标数据源名称",
                "nullable": true
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "数据转换配置(JSON)",
                "nullable": true
              },
              {
                "name": "scheduleCron",
                "cnName": "",
                "type": "string",
                "description": "调度 Cron 表达式",
                "nullable": true
              },
              {
                "name": "scheduleEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用定时调度",
                "nullable": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "PipelineStatus",
                "description": "状态；取值：0=Draft（草稿）；1=Active（启用）；2=Paused（暂停）；3=Archived（归档）",
                "nullable": false
              },
              {
                "name": "lastRunAt",
                "cnName": "",
                "type": "DateTime",
                "description": "最近一次运行时间",
                "nullable": true
              },
              {
                "name": "lastRunStatus",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-pipelines",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "管道名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "管道描述",
                "required": false
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID",
                "required": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID",
                "required": false
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "数据转换配置(JSON)",
                "required": false
              },
              {
                "name": "scheduleCron",
                "cnName": "",
                "type": "string",
                "description": "调度 Cron 表达式",
                "required": false
              },
              {
                "name": "scheduleEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用定时调度",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "管道名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "管道描述",
                "nullable": true
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID",
                "nullable": false
              },
              {
                "name": "sourceName",
                "cnName": "",
                "type": "string",
                "description": "源数据源名称",
                "nullable": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID",
                "nullable": true
              },
              {
                "name": "targetName",
                "cnName": "",
                "type": "string",
                "description": "目标数据源名称",
                "nullable": true
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "数据转换配置(JSON)",
                "nullable": true
              },
              {
                "name": "scheduleCron",
                "cnName": "",
                "type": "string",
                "description": "调度 Cron 表达式",
                "nullable": true
              },
              {
                "name": "scheduleEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用定时调度",
                "nullable": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "PipelineStatus",
                "description": "状态；取值：0=Draft（草稿）；1=Active（启用）；2=Paused（暂停）；3=Archived（归档）",
                "nullable": false
              },
              {
                "name": "lastRunAt",
                "cnName": "",
                "type": "DateTime",
                "description": "最近一次运行时间",
                "nullable": true
              },
              {
                "name": "lastRunStatus",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/data-pipelines/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "管道名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "管道描述",
                "nullable": true
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID",
                "nullable": false
              },
              {
                "name": "sourceName",
                "cnName": "",
                "type": "string",
                "description": "源数据源名称",
                "nullable": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID",
                "nullable": true
              },
              {
                "name": "targetName",
                "cnName": "",
                "type": "string",
                "description": "目标数据源名称",
                "nullable": true
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "数据转换配置(JSON)",
                "nullable": true
              },
              {
                "name": "scheduleCron",
                "cnName": "",
                "type": "string",
                "description": "调度 Cron 表达式",
                "nullable": true
              },
              {
                "name": "scheduleEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用定时调度",
                "nullable": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "PipelineStatus",
                "description": "状态；取值：0=Draft（草稿）；1=Active（启用）；2=Paused（暂停）；3=Archived（归档）",
                "nullable": false
              },
              {
                "name": "lastRunAt",
                "cnName": "",
                "type": "DateTime",
                "description": "最近一次运行时间",
                "nullable": true
              },
              {
                "name": "lastRunStatus",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/data-pipelines/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "管道名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "管道描述",
                "required": false
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID",
                "required": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID",
                "required": false
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "数据转换配置(JSON)",
                "required": false
              },
              {
                "name": "scheduleCron",
                "cnName": "",
                "type": "string",
                "description": "调度 Cron 表达式",
                "required": false
              },
              {
                "name": "scheduleEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用定时调度",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "管道名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "管道描述",
                "nullable": true
              },
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID",
                "nullable": false
              },
              {
                "name": "sourceName",
                "cnName": "",
                "type": "string",
                "description": "源数据源名称",
                "nullable": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID",
                "nullable": true
              },
              {
                "name": "targetName",
                "cnName": "",
                "type": "string",
                "description": "目标数据源名称",
                "nullable": true
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "数据转换配置(JSON)",
                "nullable": true
              },
              {
                "name": "scheduleCron",
                "cnName": "",
                "type": "string",
                "description": "调度 Cron 表达式",
                "nullable": true
              },
              {
                "name": "scheduleEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用定时调度",
                "nullable": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "PipelineStatus",
                "description": "状态；取值：0=Draft（草稿）；1=Active（启用）；2=Paused（暂停）；3=Archived（归档）",
                "nullable": false
              },
              {
                "name": "lastRunAt",
                "cnName": "",
                "type": "DateTime",
                "description": "最近一次运行时间",
                "nullable": true
              },
              {
                "name": "lastRunStatus",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/data-pipelines/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/data-pipelines/{id}/activate",
            "summary": "activate",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-pipelines/{id}/execute",
            "summary": "执行",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              },
              {
                "name": "runId",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "乐观并发戳；执行恢复等状态变更必须回传最新值。",
                "nullable": true
              },
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "description": "所属管道 ID",
                "nullable": false
              },
              {
                "name": "pipelineName",
                "cnName": "",
                "type": "string",
                "description": "所属管道名称",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "triggerType",
                "cnName": "",
                "type": "TriggerType",
                "description": "取值：0=Manual（手动）；1=Scheduled（定时）",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行开始时间",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行结束时间",
                "nullable": true
              },
              {
                "name": "durationSeconds",
                "cnName": "",
                "type": "number",
                "description": "耗时(秒)",
                "nullable": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "处理行数",
                "nullable": true
              },
              {
                "name": "errorCode",
                "cnName": "",
                "type": "string",
                "description": "稳定错误码（仅失败时返回）",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "本地化安全错误信息；不回显底层异常与历史原始错误",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "请求关联号（用于管理员定位日志）",
                "nullable": true
              },
              {
                "name": "exceptionType",
                "cnName": "",
                "type": "string",
                "description": "异常类型全名（不含消息与堆栈）",
                "nullable": true
              },
              {
                "name": "targetDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时目标数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "targetDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源名称快照",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库 provider 快照",
                "nullable": true
              },
              {
                "name": "targetEndpoint",
                "cnName": "",
                "type": "string",
                "description": "执行时目标端点快照（不含账号与凭据）",
                "nullable": true
              },
              {
                "name": "targetDatabase",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库名快照",
                "nullable": true
              },
              {
                "name": "targetTable",
                "cnName": "",
                "type": "string",
                "description": "执行时规范目标表快照",
                "nullable": true
              },
              {
                "name": "targetConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "targetResourceGroup",
                "cnName": "",
                "type": "string",
                "description": "执行时管理员维护的目标物理资源组",
                "nullable": true
              },
              {
                "name": "sourceDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时源数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "sourceDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源名称快照",
                "nullable": true
              },
              {
                "name": "sourceConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "执行时写入模式（append / truncate）",
                "nullable": true
              },
              {
                "name": "sourceQuerySha256",
                "cnName": "",
                "type": "string",
                "description": "执行时规范化源查询的 SHA-256；不回显查询正文",
                "nullable": true
              },
              {
                "name": "reconciliationStrategy",
                "cnName": "",
                "type": "string",
                "description": "执行使用的对账策略（manual-v0 / target-receipt-v1）。",
                "nullable": true
              },
              {
                "name": "receiptVerificationEligible",
                "cnName": "",
                "type": "boolean",
                "description": "当前执行是否具备机器核验目标回执的完整稳定身份。",
                "nullable": false
              },
              {
                "name": "receiptVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "目标回执完成机器核验的时间。",
                "nullable": true
              },
              {
                "name": "receiptVerificationSource",
                "cnName": "",
                "type": "string",
                "description": "机器核验来源；当前仅 target-receipt-v1。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/data-pipelines/{id}/pause",
            "summary": "pause",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          }
        ]
      },
      {
        "id": "DataPipelineSchemaPreflight",
        "title": "管道结构预检与连接器能力 /data-pipelines/capabilities",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/data-pipelines/capabilities",
            "summary": "获取平台内置数据源连接器的读写、原子 Upsert 与机器回执能力清单。",
            "summaryDocumented": true,
            "responseFields": [
              {
                "name": "provider",
                "cnName": "",
                "type": "string",
                "description": "标准化连接器标识（postgresql / sqlserver / mysql / rest-api / file）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "连接器显示名称。",
                "nullable": true
              },
              {
                "name": "dataSourceType",
                "cnName": "",
                "type": "DataSourceType",
                "description": "取值：0=RelationalDatabase（关系型数据库）；1=RestApi（REST API）；2=File（文件）",
                "nullable": false
              },
              {
                "name": "supportsSourceRead",
                "cnName": "",
                "type": "boolean",
                "description": "是否支持作为流式 ETL 源读取。",
                "nullable": false
              },
              {
                "name": "supportsTargetWrite",
                "cnName": "",
                "type": "boolean",
                "description": "是否支持作为关系型目标写入。",
                "nullable": false
              },
              {
                "name": "supportsAtomicUpsert",
                "cnName": "",
                "type": "boolean",
                "description": "是否支持原子增量合并（Upsert）。",
                "nullable": false
              },
              {
                "name": "supportsTargetReceiptV1",
                "cnName": "",
                "type": "boolean",
                "description": "是否支持目标库机器回执 v1（__swapmid_etl_receipt_v1）。",
                "nullable": false
              },
              {
                "name": "requiresStrictTlsForReceipt",
                "cnName": "",
                "type": "boolean",
                "description": "机器回执模式是否强制要求严格 TLS（encrypt=true 且 trustServerCertificate=false）。",
                "nullable": false
              },
              {
                "name": "supportedWriteModes",
                "cnName": "",
                "type": "string[]",
                "description": "支持的写入模式列表。",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-pipelines/preflight-schema",
            "summary": "静态探测源查询结果列与目标表字段、非空及唯一键约束的兼容性（不写入任何业务数据）。",
            "summaryDocumented": true,
            "requestBody": [
              {
                "name": "sourceId",
                "cnName": "",
                "type": "Guid",
                "description": "源数据源 ID。",
                "required": true
              },
              {
                "name": "targetId",
                "cnName": "",
                "type": "Guid",
                "description": "目标数据源 ID（可选）。",
                "required": false
              },
              {
                "name": "transformConfig",
                "cnName": "",
                "type": "string",
                "description": "ETL 转换规则 JSON（含 sourceQuery / targetTable / mode / keyColumns / watermarkColumn）。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "compatible",
                "cnName": "",
                "type": "boolean",
                "description": "是否存在零阻断错误（severity=error 为 0 时为 true）。",
                "nullable": false
              },
              {
                "name": "sourceProvider",
                "cnName": "",
                "type": "string",
                "description": "源连接器标识。",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "目标连接器标识（未选目标源时为 null）。",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "解析后的写入模式。",
                "nullable": true
              },
              {
                "name": "sourceColumns",
                "cnName": "",
                "type": "PreflightColumnDto[]",
                "description": "源查询零行 Schema 探测列清单。",
                "nullable": true
              },
              {
                "name": "sourceColumns[].name",
                "cnName": "",
                "type": "string",
                "description": "列名。",
                "nullable": true
              },
              {
                "name": "sourceColumns[].dataType",
                "cnName": "",
                "type": "string",
                "description": "数据库类型或 CLR 类型描述。",
                "nullable": true
              },
              {
                "name": "sourceColumns[].isNullable",
                "cnName": "",
                "type": "boolean",
                "description": "是否允许空值。",
                "nullable": false
              },
              {
                "name": "sourceColumns[].isPrimaryKey",
                "cnName": "",
                "type": "boolean",
                "description": "是否为主键列。",
                "nullable": false
              },
              {
                "name": "sourceColumns[].isUniqueIndexed",
                "cnName": "",
                "type": "boolean",
                "description": "是否受非过滤唯一索引或主键覆盖。",
                "nullable": false
              },
              {
                "name": "sourceColumns[].hasDefaultOrIdentity",
                "cnName": "",
                "type": "boolean",
                "description": "目标列是否定义了默认值或自增计算。",
                "nullable": false
              },
              {
                "name": "targetColumns",
                "cnName": "",
                "type": "PreflightColumnDto[]",
                "description": "目标表结构列清单。",
                "nullable": true
              },
              {
                "name": "targetColumns[].name",
                "cnName": "",
                "type": "string",
                "description": "列名。",
                "nullable": true
              },
              {
                "name": "targetColumns[].dataType",
                "cnName": "",
                "type": "string",
                "description": "数据库类型或 CLR 类型描述。",
                "nullable": true
              },
              {
                "name": "targetColumns[].isNullable",
                "cnName": "",
                "type": "boolean",
                "description": "是否允许空值。",
                "nullable": false
              },
              {
                "name": "targetColumns[].isPrimaryKey",
                "cnName": "",
                "type": "boolean",
                "description": "是否为主键列。",
                "nullable": false
              },
              {
                "name": "targetColumns[].isUniqueIndexed",
                "cnName": "",
                "type": "boolean",
                "description": "是否受非过滤唯一索引或主键覆盖。",
                "nullable": false
              },
              {
                "name": "targetColumns[].hasDefaultOrIdentity",
                "cnName": "",
                "type": "boolean",
                "description": "目标列是否定义了默认值或自增计算。",
                "nullable": false
              },
              {
                "name": "issues",
                "cnName": "",
                "type": "PreflightSchemaIssueDto[]",
                "description": "兼容性诊断问题列表。",
                "nullable": true
              },
              {
                "name": "issues[].severity",
                "cnName": "",
                "type": "string",
                "description": "严重级别：error 或 warning。",
                "nullable": true
              },
              {
                "name": "issues[].code",
                "cnName": "",
                "type": "string",
                "description": "稳定诊断代码。",
                "nullable": true
              },
              {
                "name": "issues[].message",
                "cnName": "",
                "type": "string",
                "description": "面向操作员的诊断说明（不含敏感连接串或驱动原文）。",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "PipelineExecution",
        "title": "管道执行记录 /pipeline-executions",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/pipeline-executions",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按所属管道 ID 过滤"
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "required": false,
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）"
              },
              {
                "name": "startedFrom",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "执行开始时间(起)"
              },
              {
                "name": "startedTo",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "执行开始时间(止)"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "乐观并发戳；执行恢复等状态变更必须回传最新值。",
                "nullable": true
              },
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "description": "所属管道 ID",
                "nullable": false
              },
              {
                "name": "pipelineName",
                "cnName": "",
                "type": "string",
                "description": "所属管道名称",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "triggerType",
                "cnName": "",
                "type": "TriggerType",
                "description": "取值：0=Manual（手动）；1=Scheduled（定时）",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行开始时间",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行结束时间",
                "nullable": true
              },
              {
                "name": "durationSeconds",
                "cnName": "",
                "type": "number",
                "description": "耗时(秒)",
                "nullable": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "处理行数",
                "nullable": true
              },
              {
                "name": "errorCode",
                "cnName": "",
                "type": "string",
                "description": "稳定错误码（仅失败时返回）",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "本地化安全错误信息；不回显底层异常与历史原始错误",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "请求关联号（用于管理员定位日志）",
                "nullable": true
              },
              {
                "name": "exceptionType",
                "cnName": "",
                "type": "string",
                "description": "异常类型全名（不含消息与堆栈）",
                "nullable": true
              },
              {
                "name": "targetDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时目标数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "targetDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源名称快照",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库 provider 快照",
                "nullable": true
              },
              {
                "name": "targetEndpoint",
                "cnName": "",
                "type": "string",
                "description": "执行时目标端点快照（不含账号与凭据）",
                "nullable": true
              },
              {
                "name": "targetDatabase",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库名快照",
                "nullable": true
              },
              {
                "name": "targetTable",
                "cnName": "",
                "type": "string",
                "description": "执行时规范目标表快照",
                "nullable": true
              },
              {
                "name": "targetConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "targetResourceGroup",
                "cnName": "",
                "type": "string",
                "description": "执行时管理员维护的目标物理资源组",
                "nullable": true
              },
              {
                "name": "sourceDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时源数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "sourceDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源名称快照",
                "nullable": true
              },
              {
                "name": "sourceConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "执行时写入模式（append / truncate）",
                "nullable": true
              },
              {
                "name": "sourceQuerySha256",
                "cnName": "",
                "type": "string",
                "description": "执行时规范化源查询的 SHA-256；不回显查询正文",
                "nullable": true
              },
              {
                "name": "reconciliationStrategy",
                "cnName": "",
                "type": "string",
                "description": "执行使用的对账策略（manual-v0 / target-receipt-v1）。",
                "nullable": true
              },
              {
                "name": "receiptVerificationEligible",
                "cnName": "",
                "type": "boolean",
                "description": "当前执行是否具备机器核验目标回执的完整稳定身份。",
                "nullable": false
              },
              {
                "name": "receiptVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "目标回执完成机器核验的时间。",
                "nullable": true
              },
              {
                "name": "receiptVerificationSource",
                "cnName": "",
                "type": "string",
                "description": "机器核验来源；当前仅 target-receipt-v1。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/pipeline-executions/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "乐观并发戳；执行恢复等状态变更必须回传最新值。",
                "nullable": true
              },
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "description": "所属管道 ID",
                "nullable": false
              },
              {
                "name": "pipelineName",
                "cnName": "",
                "type": "string",
                "description": "所属管道名称",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "triggerType",
                "cnName": "",
                "type": "TriggerType",
                "description": "取值：0=Manual（手动）；1=Scheduled（定时）",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行开始时间",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行结束时间",
                "nullable": true
              },
              {
                "name": "durationSeconds",
                "cnName": "",
                "type": "number",
                "description": "耗时(秒)",
                "nullable": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "处理行数",
                "nullable": true
              },
              {
                "name": "errorCode",
                "cnName": "",
                "type": "string",
                "description": "稳定错误码（仅失败时返回）",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "本地化安全错误信息；不回显底层异常与历史原始错误",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "请求关联号（用于管理员定位日志）",
                "nullable": true
              },
              {
                "name": "exceptionType",
                "cnName": "",
                "type": "string",
                "description": "异常类型全名（不含消息与堆栈）",
                "nullable": true
              },
              {
                "name": "targetDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时目标数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "targetDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源名称快照",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库 provider 快照",
                "nullable": true
              },
              {
                "name": "targetEndpoint",
                "cnName": "",
                "type": "string",
                "description": "执行时目标端点快照（不含账号与凭据）",
                "nullable": true
              },
              {
                "name": "targetDatabase",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库名快照",
                "nullable": true
              },
              {
                "name": "targetTable",
                "cnName": "",
                "type": "string",
                "description": "执行时规范目标表快照",
                "nullable": true
              },
              {
                "name": "targetConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "targetResourceGroup",
                "cnName": "",
                "type": "string",
                "description": "执行时管理员维护的目标物理资源组",
                "nullable": true
              },
              {
                "name": "sourceDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时源数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "sourceDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源名称快照",
                "nullable": true
              },
              {
                "name": "sourceConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "执行时写入模式（append / truncate）",
                "nullable": true
              },
              {
                "name": "sourceQuerySha256",
                "cnName": "",
                "type": "string",
                "description": "执行时规范化源查询的 SHA-256；不回显查询正文",
                "nullable": true
              },
              {
                "name": "reconciliationStrategy",
                "cnName": "",
                "type": "string",
                "description": "执行使用的对账策略（manual-v0 / target-receipt-v1）。",
                "nullable": true
              },
              {
                "name": "receiptVerificationEligible",
                "cnName": "",
                "type": "boolean",
                "description": "当前执行是否具备机器核验目标回执的完整稳定身份。",
                "nullable": false
              },
              {
                "name": "receiptVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "目标回执完成机器核验的时间。",
                "nullable": true
              },
              {
                "name": "receiptVerificationSource",
                "cnName": "",
                "type": "string",
                "description": "机器核验来源；当前仅 target-receipt-v1。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/pipeline-executions/{id}/recover-running",
            "summary": "使用已核验的摘流与目标会话退出证据，受控恢复滞留的运行中执行。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "recoveryRequestId",
                "cnName": "",
                "type": "Guid",
                "description": "恢复请求幂等标识；同一次处置重试必须复用该值。",
                "required": true
              },
              {
                "name": "expectedConcurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "调用方最后读取到的执行记录并发戳。",
                "required": true
              },
              {
                "name": "drainStartedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "全部旧执行实例开始摘流的 UTC 时间。",
                "required": true
              },
              {
                "name": "oldOwnerExitVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "全部具备旧执行 owner 能力的实例已退出的 UTC 确认时间。",
                "required": true
              },
              {
                "name": "targetSessionExitVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "DBA 确认目标活跃会话与未完成事务均已消失的 UTC 时间。",
                "required": true
              },
              {
                "name": "evidenceReference",
                "cnName": "",
                "type": "string",
                "description": "结构化证据引用。仅允许 incident:/change:/work-order:/dba-evidence: 前缀，\r\n后接 8–128 位 ASCII 字母数字、短横线或下划线组成的不透明编号；不得粘贴 URL、SQL、\r\n连接串、令牌或密钥。示例：incident:INC-2026-ETL-0001。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "乐观并发戳；执行恢复等状态变更必须回传最新值。",
                "nullable": true
              },
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "description": "所属管道 ID",
                "nullable": false
              },
              {
                "name": "pipelineName",
                "cnName": "",
                "type": "string",
                "description": "所属管道名称",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "triggerType",
                "cnName": "",
                "type": "TriggerType",
                "description": "取值：0=Manual（手动）；1=Scheduled（定时）",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行开始时间",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行结束时间",
                "nullable": true
              },
              {
                "name": "durationSeconds",
                "cnName": "",
                "type": "number",
                "description": "耗时(秒)",
                "nullable": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "处理行数",
                "nullable": true
              },
              {
                "name": "errorCode",
                "cnName": "",
                "type": "string",
                "description": "稳定错误码（仅失败时返回）",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "本地化安全错误信息；不回显底层异常与历史原始错误",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "请求关联号（用于管理员定位日志）",
                "nullable": true
              },
              {
                "name": "exceptionType",
                "cnName": "",
                "type": "string",
                "description": "异常类型全名（不含消息与堆栈）",
                "nullable": true
              },
              {
                "name": "targetDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时目标数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "targetDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源名称快照",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库 provider 快照",
                "nullable": true
              },
              {
                "name": "targetEndpoint",
                "cnName": "",
                "type": "string",
                "description": "执行时目标端点快照（不含账号与凭据）",
                "nullable": true
              },
              {
                "name": "targetDatabase",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库名快照",
                "nullable": true
              },
              {
                "name": "targetTable",
                "cnName": "",
                "type": "string",
                "description": "执行时规范目标表快照",
                "nullable": true
              },
              {
                "name": "targetConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "targetResourceGroup",
                "cnName": "",
                "type": "string",
                "description": "执行时管理员维护的目标物理资源组",
                "nullable": true
              },
              {
                "name": "sourceDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时源数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "sourceDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源名称快照",
                "nullable": true
              },
              {
                "name": "sourceConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "执行时写入模式（append / truncate）",
                "nullable": true
              },
              {
                "name": "sourceQuerySha256",
                "cnName": "",
                "type": "string",
                "description": "执行时规范化源查询的 SHA-256；不回显查询正文",
                "nullable": true
              },
              {
                "name": "reconciliationStrategy",
                "cnName": "",
                "type": "string",
                "description": "执行使用的对账策略（manual-v0 / target-receipt-v1）。",
                "nullable": true
              },
              {
                "name": "receiptVerificationEligible",
                "cnName": "",
                "type": "boolean",
                "description": "当前执行是否具备机器核验目标回执的完整稳定身份。",
                "nullable": false
              },
              {
                "name": "receiptVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "目标回执完成机器核验的时间。",
                "nullable": true
              },
              {
                "name": "receiptVerificationSource",
                "cnName": "",
                "type": "string",
                "description": "机器核验来源；当前仅 target-receipt-v1。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/pipeline-executions/{id}/resolve-outcome",
            "summary": "resolve-outcome",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "resolution",
                "cnName": "",
                "type": "PipelineExecutionResolution",
                "description": "取值：0=ConfirmedApplied（确认已应用）；1=ConfirmedNotApplied（确认未应用）",
                "required": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "确认已应用时可补录处理行数。",
                "required": false
              },
              {
                "name": "verificationNote",
                "cnName": "",
                "type": "string",
                "description": "新客户端应提交结构化核验证据引用：incident:/change:/work-order:/dba-evidence:\r\n前缀后接 8–128 位不透明编号。为兼容已发布客户端，HTTP 契约仍接收旧 8–512\r\n字符说明；服务端不会保存旧说明正文，而会替换为不可逆的安全占位引用。\r\n不得粘贴 URL、SQL、连接串、令牌或密钥。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "乐观并发戳；执行恢复等状态变更必须回传最新值。",
                "nullable": true
              },
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "description": "所属管道 ID",
                "nullable": false
              },
              {
                "name": "pipelineName",
                "cnName": "",
                "type": "string",
                "description": "所属管道名称",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "triggerType",
                "cnName": "",
                "type": "TriggerType",
                "description": "取值：0=Manual（手动）；1=Scheduled（定时）",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行开始时间",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行结束时间",
                "nullable": true
              },
              {
                "name": "durationSeconds",
                "cnName": "",
                "type": "number",
                "description": "耗时(秒)",
                "nullable": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "处理行数",
                "nullable": true
              },
              {
                "name": "errorCode",
                "cnName": "",
                "type": "string",
                "description": "稳定错误码（仅失败时返回）",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "本地化安全错误信息；不回显底层异常与历史原始错误",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "请求关联号（用于管理员定位日志）",
                "nullable": true
              },
              {
                "name": "exceptionType",
                "cnName": "",
                "type": "string",
                "description": "异常类型全名（不含消息与堆栈）",
                "nullable": true
              },
              {
                "name": "targetDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时目标数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "targetDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源名称快照",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库 provider 快照",
                "nullable": true
              },
              {
                "name": "targetEndpoint",
                "cnName": "",
                "type": "string",
                "description": "执行时目标端点快照（不含账号与凭据）",
                "nullable": true
              },
              {
                "name": "targetDatabase",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库名快照",
                "nullable": true
              },
              {
                "name": "targetTable",
                "cnName": "",
                "type": "string",
                "description": "执行时规范目标表快照",
                "nullable": true
              },
              {
                "name": "targetConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "targetResourceGroup",
                "cnName": "",
                "type": "string",
                "description": "执行时管理员维护的目标物理资源组",
                "nullable": true
              },
              {
                "name": "sourceDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时源数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "sourceDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源名称快照",
                "nullable": true
              },
              {
                "name": "sourceConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "执行时写入模式（append / truncate）",
                "nullable": true
              },
              {
                "name": "sourceQuerySha256",
                "cnName": "",
                "type": "string",
                "description": "执行时规范化源查询的 SHA-256；不回显查询正文",
                "nullable": true
              },
              {
                "name": "reconciliationStrategy",
                "cnName": "",
                "type": "string",
                "description": "执行使用的对账策略（manual-v0 / target-receipt-v1）。",
                "nullable": true
              },
              {
                "name": "receiptVerificationEligible",
                "cnName": "",
                "type": "boolean",
                "description": "当前执行是否具备机器核验目标回执的完整稳定身份。",
                "nullable": false
              },
              {
                "name": "receiptVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "目标回执完成机器核验的时间。",
                "nullable": true
              },
              {
                "name": "receiptVerificationSource",
                "cnName": "",
                "type": "string",
                "description": "机器核验来源；当前仅 target-receipt-v1。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/pipeline-executions/{id}/verify-target-receipt",
            "summary": "只读核验目标库同事务回执；仅完整匹配时收敛为成功。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "乐观并发戳；执行恢复等状态变更必须回传最新值。",
                "nullable": true
              },
              {
                "name": "pipelineId",
                "cnName": "",
                "type": "Guid",
                "description": "所属管道 ID",
                "nullable": false
              },
              {
                "name": "pipelineName",
                "cnName": "",
                "type": "string",
                "description": "所属管道名称",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "ExecutionStatus",
                "description": "状态；取值：0=Pending（待执行）；1=Running（执行中）；2=Succeeded（成功）；3=Failed（失败）；4=Cancelled（已取消）；5=OutcomeUnknown（结果待核对）",
                "nullable": false
              },
              {
                "name": "triggerType",
                "cnName": "",
                "type": "TriggerType",
                "description": "取值：0=Manual（手动）；1=Scheduled（定时）",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行开始时间",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "执行结束时间",
                "nullable": true
              },
              {
                "name": "durationSeconds",
                "cnName": "",
                "type": "number",
                "description": "耗时(秒)",
                "nullable": true
              },
              {
                "name": "processedRows",
                "cnName": "",
                "type": "integer",
                "description": "处理行数",
                "nullable": true
              },
              {
                "name": "errorCode",
                "cnName": "",
                "type": "string",
                "description": "稳定错误码（仅失败时返回）",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "本地化安全错误信息；不回显底层异常与历史原始错误",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "请求关联号（用于管理员定位日志）",
                "nullable": true
              },
              {
                "name": "exceptionType",
                "cnName": "",
                "type": "string",
                "description": "异常类型全名（不含消息与堆栈）",
                "nullable": true
              },
              {
                "name": "targetDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时目标数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "targetDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源名称快照",
                "nullable": true
              },
              {
                "name": "targetProvider",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库 provider 快照",
                "nullable": true
              },
              {
                "name": "targetEndpoint",
                "cnName": "",
                "type": "string",
                "description": "执行时目标端点快照（不含账号与凭据）",
                "nullable": true
              },
              {
                "name": "targetDatabase",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据库名快照",
                "nullable": true
              },
              {
                "name": "targetTable",
                "cnName": "",
                "type": "string",
                "description": "执行时规范目标表快照",
                "nullable": true
              },
              {
                "name": "targetConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时目标数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "targetResourceGroup",
                "cnName": "",
                "type": "string",
                "description": "执行时管理员维护的目标物理资源组",
                "nullable": true
              },
              {
                "name": "sourceDataSourceId",
                "cnName": "",
                "type": "Guid",
                "description": "执行时源数据源 ID 快照",
                "nullable": true
              },
              {
                "name": "sourceDataSourceName",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源名称快照",
                "nullable": true
              },
              {
                "name": "sourceConfigVersion",
                "cnName": "",
                "type": "string",
                "description": "执行时源数据源乐观并发版本",
                "nullable": true
              },
              {
                "name": "writeMode",
                "cnName": "",
                "type": "string",
                "description": "执行时写入模式（append / truncate）",
                "nullable": true
              },
              {
                "name": "sourceQuerySha256",
                "cnName": "",
                "type": "string",
                "description": "执行时规范化源查询的 SHA-256；不回显查询正文",
                "nullable": true
              },
              {
                "name": "reconciliationStrategy",
                "cnName": "",
                "type": "string",
                "description": "执行使用的对账策略（manual-v0 / target-receipt-v1）。",
                "nullable": true
              },
              {
                "name": "receiptVerificationEligible",
                "cnName": "",
                "type": "boolean",
                "description": "当前执行是否具备机器核验目标回执的完整稳定身份。",
                "nullable": false
              },
              {
                "name": "receiptVerifiedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "目标回执完成机器核验的时间。",
                "nullable": true
              },
              {
                "name": "receiptVerificationSource",
                "cnName": "",
                "type": "string",
                "description": "机器核验来源；当前仅 target-receipt-v1。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "message-bus",
    "title": "消息总线",
    "description": "主题发布、订阅过滤与 HTTP Webhook / RabbitMQ / 通用 Bot Webhook 可靠投递与重放。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "MessageTopic",
        "title": "消息主题 /message-topics",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/message-topics",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "required": false,
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "主题名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "主题描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "retryIntervalSeconds",
                "cnName": "",
                "type": "integer",
                "description": "重试间隔(秒)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/message-topics",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "主题名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "主题描述",
                "required": false
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）",
                "required": true
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "required": false
              },
              {
                "name": "retryIntervalSeconds",
                "cnName": "",
                "type": "integer",
                "description": "重试间隔(秒)",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "主题名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "主题描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "retryIntervalSeconds",
                "cnName": "",
                "type": "integer",
                "description": "重试间隔(秒)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/message-topics/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "主题名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "主题描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "retryIntervalSeconds",
                "cnName": "",
                "type": "integer",
                "description": "重试间隔(秒)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/message-topics/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "主题名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "主题描述",
                "required": false
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）",
                "required": true
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "required": false
              },
              {
                "name": "retryIntervalSeconds",
                "cnName": "",
                "type": "integer",
                "description": "重试间隔(秒)",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "主题名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "主题描述",
                "nullable": true
              },
              {
                "name": "type",
                "cnName": "",
                "type": "TopicType",
                "description": "类型；取值：0=Queue（队列）；1=Broadcast（广播）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "retryIntervalSeconds",
                "cnName": "",
                "type": "integer",
                "description": "重试间隔(秒)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/message-topics/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          }
        ]
      },
      {
        "id": "MessageSubscription",
        "title": "消息订阅 /message-subscriptions",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/message-subscriptions",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按所属主题 ID 过滤"
              },
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "订阅名称",
                "nullable": true
              },
              {
                "name": "deliveryType",
                "cnName": "",
                "type": "DeliveryType",
                "description": "取值：0=Http（HTTP Webhook）；1=RabbitMq（RabbitMQ）；2=BotWebhook（Bot Webhook）",
                "nullable": false
              },
              {
                "name": "endpointUrl",
                "cnName": "",
                "type": "string",
                "description": "端点 URL(HTTP 投递目标)",
                "nullable": true
              },
              {
                "name": "exchangeName",
                "cnName": "",
                "type": "string",
                "description": "交换机名称(RabbitMq)",
                "nullable": true
              },
              {
                "name": "routingKey",
                "cnName": "",
                "type": "string",
                "description": "路由键(RabbitMq)",
                "nullable": true
              },
              {
                "name": "filterRule",
                "cnName": "",
                "type": "string",
                "description": "过滤规则",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否已配置 HMAC 签名密钥(密钥本身写不可读,不回传)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/message-subscriptions",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "订阅名称",
                "required": true
              },
              {
                "name": "deliveryType",
                "cnName": "",
                "type": "DeliveryType",
                "description": "取值：0=Http（HTTP Webhook）；1=RabbitMq（RabbitMQ）；2=BotWebhook（Bot Webhook）",
                "required": false
              },
              {
                "name": "endpointUrl",
                "cnName": "",
                "type": "string",
                "description": "端点 URL(HTTP 投递目标)",
                "required": false
              },
              {
                "name": "exchangeName",
                "cnName": "",
                "type": "string",
                "description": "交换机名称(RabbitMq)",
                "required": false
              },
              {
                "name": "routingKey",
                "cnName": "",
                "type": "string",
                "description": "路由键(RabbitMq)",
                "required": false
              },
              {
                "name": "filterRule",
                "cnName": "",
                "type": "string",
                "description": "过滤规则",
                "required": false
              },
              {
                "name": "secret",
                "cnName": "",
                "type": "string",
                "description": "HMAC 签名密钥(可空,仅 HTTP 投递用;配了则投递带 X-SwapMid-Signature 头供消费者验真)",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "订阅名称",
                "nullable": true
              },
              {
                "name": "deliveryType",
                "cnName": "",
                "type": "DeliveryType",
                "description": "取值：0=Http（HTTP Webhook）；1=RabbitMq（RabbitMQ）；2=BotWebhook（Bot Webhook）",
                "nullable": false
              },
              {
                "name": "endpointUrl",
                "cnName": "",
                "type": "string",
                "description": "端点 URL(HTTP 投递目标)",
                "nullable": true
              },
              {
                "name": "exchangeName",
                "cnName": "",
                "type": "string",
                "description": "交换机名称(RabbitMq)",
                "nullable": true
              },
              {
                "name": "routingKey",
                "cnName": "",
                "type": "string",
                "description": "路由键(RabbitMq)",
                "nullable": true
              },
              {
                "name": "filterRule",
                "cnName": "",
                "type": "string",
                "description": "过滤规则",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否已配置 HMAC 签名密钥(密钥本身写不可读,不回传)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/message-subscriptions/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "订阅名称",
                "nullable": true
              },
              {
                "name": "deliveryType",
                "cnName": "",
                "type": "DeliveryType",
                "description": "取值：0=Http（HTTP Webhook）；1=RabbitMq（RabbitMQ）；2=BotWebhook（Bot Webhook）",
                "nullable": false
              },
              {
                "name": "endpointUrl",
                "cnName": "",
                "type": "string",
                "description": "端点 URL(HTTP 投递目标)",
                "nullable": true
              },
              {
                "name": "exchangeName",
                "cnName": "",
                "type": "string",
                "description": "交换机名称(RabbitMq)",
                "nullable": true
              },
              {
                "name": "routingKey",
                "cnName": "",
                "type": "string",
                "description": "路由键(RabbitMq)",
                "nullable": true
              },
              {
                "name": "filterRule",
                "cnName": "",
                "type": "string",
                "description": "过滤规则",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否已配置 HMAC 签名密钥(密钥本身写不可读,不回传)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/message-subscriptions/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "订阅名称",
                "required": true
              },
              {
                "name": "deliveryType",
                "cnName": "",
                "type": "DeliveryType",
                "description": "取值：0=Http（HTTP Webhook）；1=RabbitMq（RabbitMQ）；2=BotWebhook（Bot Webhook）",
                "required": false
              },
              {
                "name": "endpointUrl",
                "cnName": "",
                "type": "string",
                "description": "端点 URL(HTTP 投递目标)",
                "required": false
              },
              {
                "name": "exchangeName",
                "cnName": "",
                "type": "string",
                "description": "交换机名称(RabbitMq)",
                "required": false
              },
              {
                "name": "routingKey",
                "cnName": "",
                "type": "string",
                "description": "路由键(RabbitMq)",
                "required": false
              },
              {
                "name": "filterRule",
                "cnName": "",
                "type": "string",
                "description": "过滤规则",
                "required": false
              },
              {
                "name": "secret",
                "cnName": "",
                "type": "string",
                "description": "HMAC 签名密钥(可空,仅 HTTP 投递用;配了则投递带 X-SwapMid-Signature 头供消费者验真)",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "订阅名称",
                "nullable": true
              },
              {
                "name": "deliveryType",
                "cnName": "",
                "type": "DeliveryType",
                "description": "取值：0=Http（HTTP Webhook）；1=RabbitMq（RabbitMQ）；2=BotWebhook（Bot Webhook）",
                "nullable": false
              },
              {
                "name": "endpointUrl",
                "cnName": "",
                "type": "string",
                "description": "端点 URL(HTTP 投递目标)",
                "nullable": true
              },
              {
                "name": "exchangeName",
                "cnName": "",
                "type": "string",
                "description": "交换机名称(RabbitMq)",
                "nullable": true
              },
              {
                "name": "routingKey",
                "cnName": "",
                "type": "string",
                "description": "路由键(RabbitMq)",
                "nullable": true
              },
              {
                "name": "filterRule",
                "cnName": "",
                "type": "string",
                "description": "过滤规则",
                "nullable": true
              },
              {
                "name": "hasSecret",
                "cnName": "",
                "type": "boolean",
                "description": "是否已配置 HMAC 签名密钥(密钥本身写不可读,不回传)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/message-subscriptions/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          }
        ]
      },
      {
        "id": "MessageRecord",
        "title": "消息投递记录 /message-records",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/message-records",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按所属主题 ID 过滤"
              },
              {
                "name": "status",
                "cnName": "",
                "type": "MessageStatus",
                "required": false,
                "description": "状态；取值：0=Pending（待投递）；1=Delivered（已投递）；2=Failed（失败）；3=DeadLettered（死信）"
              },
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "subject",
                "cnName": "",
                "type": "string",
                "description": "消息主题/标题",
                "nullable": true
              },
              {
                "name": "payload",
                "cnName": "",
                "type": "string",
                "description": "消息内容",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "MessageStatus",
                "description": "状态；取值：0=Pending（待投递）；1=Delivered（已投递）；2=Failed（失败）；3=DeadLettered（死信）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "deliveredAt",
                "cnName": "",
                "type": "DateTime",
                "description": "投递成功时间",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "错误信息",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/message-records/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "subject",
                "cnName": "",
                "type": "string",
                "description": "消息主题/标题",
                "nullable": true
              },
              {
                "name": "payload",
                "cnName": "",
                "type": "string",
                "description": "消息内容",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "MessageStatus",
                "description": "状态；取值：0=Pending（待投递）；1=Delivered（已投递）；2=Failed（失败）；3=DeadLettered（死信）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "deliveredAt",
                "cnName": "",
                "type": "DateTime",
                "description": "投递成功时间",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "错误信息",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/message-records/{id}/attempts",
            "summary": "attempts",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "subscriptionId",
                "cnName": "",
                "type": "Guid",
                "description": "目标订阅 Id。",
                "nullable": false
              },
              {
                "name": "subscriptionName",
                "cnName": "",
                "type": "string",
                "description": "订阅名称(投递时快照)。",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "MessageStatus",
                "description": "状态；取值：0=Pending（待投递）；1=Delivered（已投递）；2=Failed（失败）；3=DeadLettered（死信）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "已重试次数。",
                "nullable": false
              },
              {
                "name": "lastError",
                "cnName": "",
                "type": "string",
                "description": "最近一次错误信息(可空)。",
                "nullable": true
              },
              {
                "name": "deliveredAt",
                "cnName": "",
                "type": "DateTime",
                "description": "投递成功时间(可空)。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "首次投递(明细创建)时间。",
                "nullable": false
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/message-records/{id}/replay",
            "summary": "重放",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/message-records/publish",
            "summary": "发布",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "目标主题 ID",
                "required": true
              },
              {
                "name": "subject",
                "cnName": "",
                "type": "string",
                "description": "消息主题/标题",
                "required": true
              },
              {
                "name": "payload",
                "cnName": "",
                "type": "string",
                "description": "消息内容",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "topicId",
                "cnName": "",
                "type": "Guid",
                "description": "所属主题 ID",
                "nullable": false
              },
              {
                "name": "topicName",
                "cnName": "",
                "type": "string",
                "description": "所属主题名称",
                "nullable": true
              },
              {
                "name": "subject",
                "cnName": "",
                "type": "string",
                "description": "消息主题/标题",
                "nullable": true
              },
              {
                "name": "payload",
                "cnName": "",
                "type": "string",
                "description": "消息内容",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "MessageStatus",
                "description": "状态；取值：0=Pending（待投递）；1=Delivered（已投递）；2=Failed（失败）；3=DeadLettered（死信）",
                "nullable": false
              },
              {
                "name": "retryCount",
                "cnName": "",
                "type": "integer",
                "description": "重试次数",
                "nullable": false
              },
              {
                "name": "deliveredAt",
                "cnName": "",
                "type": "DateTime",
                "description": "投递成功时间",
                "nullable": true
              },
              {
                "name": "errorMessage",
                "cnName": "",
                "type": "string",
                "description": "错误信息",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "api-management",
    "title": "API 网关与管理",
    "description": "对外 API 分组、路由转发、在线调试台与 OpenIddict 客户端凭据。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "ApiGroup",
        "title": "API 分组 /api-groups",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/api-groups",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "分组名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "分组描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/api-groups",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "分组名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "分组描述",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "分组名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "分组描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/api-groups/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "分组名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "分组描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/api-groups/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "分组名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "分组描述",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "分组名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "分组描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/api-groups/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          }
        ]
      },
      {
        "id": "ApiRoute",
        "title": "API 路由 /api-routes",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/api-routes",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤"
              },
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按所属分组 ID 过滤"
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "按是否启用过滤"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "description": "所属分组 ID",
                "nullable": true
              },
              {
                "name": "groupName",
                "cnName": "",
                "type": "string",
                "description": "所属分组名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "路由名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "路由描述",
                "nullable": true
              },
              {
                "name": "path",
                "cnName": "",
                "type": "string",
                "description": "路由路径",
                "nullable": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "HttpMethodType",
                "description": "取值：0=GET；1=POST；2=PUT；3=DELETE；4=PATCH；99=ALL",
                "nullable": false
              },
              {
                "name": "upstreamUrl",
                "cnName": "",
                "type": "string",
                "description": "上游地址(转发目标)",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/api-routes",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "description": "所属分组 ID",
                "required": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "路由名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "路由描述",
                "required": false
              },
              {
                "name": "path",
                "cnName": "",
                "type": "string",
                "description": "路由路径",
                "required": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "HttpMethodType",
                "description": "取值：0=GET；1=POST；2=PUT；3=DELETE；4=PATCH；99=ALL",
                "required": true
              },
              {
                "name": "upstreamUrl",
                "cnName": "",
                "type": "string",
                "description": "上游地址(转发目标)",
                "required": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "description": "所属分组 ID",
                "nullable": true
              },
              {
                "name": "groupName",
                "cnName": "",
                "type": "string",
                "description": "所属分组名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "路由名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "路由描述",
                "nullable": true
              },
              {
                "name": "path",
                "cnName": "",
                "type": "string",
                "description": "路由路径",
                "nullable": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "HttpMethodType",
                "description": "取值：0=GET；1=POST；2=PUT；3=DELETE；4=PATCH；99=ALL",
                "nullable": false
              },
              {
                "name": "upstreamUrl",
                "cnName": "",
                "type": "string",
                "description": "上游地址(转发目标)",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/api-routes/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "description": "所属分组 ID",
                "nullable": true
              },
              {
                "name": "groupName",
                "cnName": "",
                "type": "string",
                "description": "所属分组名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "路由名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "路由描述",
                "nullable": true
              },
              {
                "name": "path",
                "cnName": "",
                "type": "string",
                "description": "路由路径",
                "nullable": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "HttpMethodType",
                "description": "取值：0=GET；1=POST；2=PUT；3=DELETE；4=PATCH；99=ALL",
                "nullable": false
              },
              {
                "name": "upstreamUrl",
                "cnName": "",
                "type": "string",
                "description": "上游地址(转发目标)",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/api-routes/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "description": "所属分组 ID",
                "required": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "路由名称",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "路由描述",
                "required": false
              },
              {
                "name": "path",
                "cnName": "",
                "type": "string",
                "description": "路由路径",
                "required": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "HttpMethodType",
                "description": "取值：0=GET；1=POST；2=PUT；3=DELETE；4=PATCH；99=ALL",
                "required": true
              },
              {
                "name": "upstreamUrl",
                "cnName": "",
                "type": "string",
                "description": "上游地址(转发目标)",
                "required": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "groupId",
                "cnName": "",
                "type": "Guid",
                "description": "所属分组 ID",
                "nullable": true
              },
              {
                "name": "groupName",
                "cnName": "",
                "type": "string",
                "description": "所属分组名称",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "路由名称",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "路由描述",
                "nullable": true
              },
              {
                "name": "path",
                "cnName": "",
                "type": "string",
                "description": "路由路径",
                "nullable": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "HttpMethodType",
                "description": "取值：0=GET；1=POST；2=PUT；3=DELETE；4=PATCH；99=ALL",
                "nullable": false
              },
              {
                "name": "upstreamUrl",
                "cnName": "",
                "type": "string",
                "description": "上游地址(转发目标)",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号(越小越靠前)",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/api-routes/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/api-routes/{id}/disable",
            "summary": "停用",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/api-routes/{id}/enable",
            "summary": "启用",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          }
        ]
      },
      {
        "id": "ThirdPartyClient",
        "title": "第三方客户端凭据 /third-party-clients",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/third-party-clients",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "scopes",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              },
              {
                "name": "allowedGroupIds",
                "cnName": "",
                "type": "Guid[]",
                "description": "",
                "nullable": true
              },
              {
                "name": "rateLimitPerMinute",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/third-party-clients",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": true
              },
              {
                "name": "allowedGroupIds",
                "cnName": "",
                "type": "Guid[]",
                "description": "",
                "required": false
              },
              {
                "name": "rateLimitPerMinute",
                "cnName": "",
                "type": "integer",
                "description": "",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "clientSecret",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/third-party-clients/{clientId}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/third-party-clients/{clientId}/policy",
            "summary": "更新第三方客户端网关访问策略（允许访问的 API 路由分组与每分钟限流配额）。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ],
            "requestBody": [
              {
                "name": "allowedGroupIds",
                "cnName": "",
                "type": "Guid[]",
                "description": "",
                "required": false
              },
              {
                "name": "rateLimitPerMinute",
                "cnName": "",
                "type": "integer",
                "description": "",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "scopes",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              },
              {
                "name": "allowedGroupIds",
                "cnName": "",
                "type": "Guid[]",
                "description": "",
                "nullable": true
              },
              {
                "name": "rateLimitPerMinute",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/third-party-clients/{clientId}/reset-secret",
            "summary": "reset-secret",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "clientSecret",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "enterprise",
    "title": "组织主数据",
    "description": "企业、部门、岗位与员工基础主数据。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "Enterprise",
        "title": "企业 /enterprises",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/enterprises",
            "summary": "获取企业列表（分页，支持编码/名称过滤）",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（按名称/编码模糊搜索）"
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "required": false,
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "nullable": true
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "nullable": true
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "isMain",
                "cnName": "",
                "type": "boolean",
                "description": "是否主体企业（每租户唯一）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/enterprises",
            "summary": "新建企业",
            "summaryDocumented": true,
            "requestBody": [
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "required": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "required": false
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "required": false
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "required": false
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "required": false
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "required": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "nullable": true
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "nullable": true
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "isMain",
                "cnName": "",
                "type": "boolean",
                "description": "是否主体企业（每租户唯一）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/enterprises/{id}",
            "summary": "获取单个企业详情",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "nullable": true
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "nullable": true
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "isMain",
                "cnName": "",
                "type": "boolean",
                "description": "是否主体企业（每租户唯一）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/enterprises/{id}",
            "summary": "更新企业信息",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "required": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "required": false
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "required": false
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "required": false
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "required": false
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "required": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "nullable": true
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "nullable": true
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "isMain",
                "cnName": "",
                "type": "boolean",
                "description": "是否主体企业（每租户唯一）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/enterprises/{id}",
            "summary": "删除企业（级联删除部门/岗位/员工）",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/enterprises/{id}/set-as-main",
            "summary": "将指定企业设为主体企业（同一租户仅允许一个主体）",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/enterprises/main",
            "summary": "获取当前租户的主体企业",
            "summaryDocumented": true,
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "shortName",
                "cnName": "",
                "type": "string",
                "description": "企业简称",
                "nullable": true
              },
              {
                "name": "contactPerson",
                "cnName": "",
                "type": "string",
                "description": "联系人",
                "nullable": true
              },
              {
                "name": "contactPhone",
                "cnName": "",
                "type": "string",
                "description": "联系电话",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "address",
                "cnName": "",
                "type": "string",
                "description": "地址",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EnterpriseStatus",
                "description": "状态；取值：0=Active（正常）；1=Suspended（暂停）；2=Archived（归档）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "isMain",
                "cnName": "",
                "type": "boolean",
                "description": "是否主体企业（每租户唯一）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "CompanyDepartment",
        "title": "部门 /departments",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/departments",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "企业 ID"
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "企业编码"
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "上级 ID"
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "上级编码"
              },
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（按名称/编码模糊搜索）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "description": "上级 ID",
                "nullable": true
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "description": "上级编码",
                "nullable": true
              },
              {
                "name": "parentName",
                "cnName": "",
                "type": "string",
                "description": "上级名称",
                "nullable": true
              },
              {
                "name": "parent",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "parent.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "parent.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "parent.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "fullPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployee",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "leaderEmployee.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "leaderEmployee.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "leaderEmployee.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/departments",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "required": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "required": false
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "description": "上级 ID",
                "required": false
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "description": "上级编码",
                "required": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "required": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "integer",
                "description": "",
                "required": false
              },
              {
                "name": "fullPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "leaderEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "",
                "required": false
              },
              {
                "name": "leaderEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "description": "上级 ID",
                "nullable": true
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "description": "上级编码",
                "nullable": true
              },
              {
                "name": "parentName",
                "cnName": "",
                "type": "string",
                "description": "上级名称",
                "nullable": true
              },
              {
                "name": "parent",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "parent.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "parent.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "parent.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "fullPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployee",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "leaderEmployee.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "leaderEmployee.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "leaderEmployee.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/departments/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "description": "上级 ID",
                "nullable": true
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "description": "上级编码",
                "nullable": true
              },
              {
                "name": "parentName",
                "cnName": "",
                "type": "string",
                "description": "上级名称",
                "nullable": true
              },
              {
                "name": "parent",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "parent.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "parent.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "parent.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "fullPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployee",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "leaderEmployee.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "leaderEmployee.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "leaderEmployee.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/departments/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "required": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "required": false
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "description": "上级 ID",
                "required": false
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "description": "上级编码",
                "required": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "required": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "integer",
                "description": "",
                "required": false
              },
              {
                "name": "fullPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "leaderEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "",
                "required": false
              },
              {
                "name": "leaderEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "parentId",
                "cnName": "",
                "type": "Guid",
                "description": "上级 ID",
                "nullable": true
              },
              {
                "name": "parentCode",
                "cnName": "",
                "type": "string",
                "description": "上级编码",
                "nullable": true
              },
              {
                "name": "parentName",
                "cnName": "",
                "type": "string",
                "description": "上级名称",
                "nullable": true
              },
              {
                "name": "parent",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "parent.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "parent.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "parent.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "fullPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "leaderEmployee",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "leaderEmployee.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "leaderEmployee.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "leaderEmployee.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/departments/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/departments/batch-delete",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "实体 Id 列表。上限见 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest——**超限整批拒绝**\r\n（400），不截断：静默只处理前 N 个会让用户以为全做完了，是比报错糟得多的失败形态。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/departments/batch-set-enabled",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "上限同 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest（超限整批 400 拒绝，不截断）。",
                "required": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "Position",
        "title": "岗位 /positions",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/positions",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "企业 ID"
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "企业编码"
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "部门 ID"
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "部门编码"
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "是否启用"
              },
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（按名称/编码模糊搜索）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/positions",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "required": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "required": false
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "required": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "required": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "required": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/positions/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/positions/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "required": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "required": false
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "required": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "required": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "required": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "required": false
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "nullable": true
              },
              {
                "name": "order",
                "cnName": "",
                "type": "integer",
                "description": "排序号（越小越靠前）",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/positions/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/positions/batch-delete",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "实体 Id 列表。上限见 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest——**超限整批拒绝**\r\n（400），不截断：静默只处理前 N 个会让用户以为全做完了，是比报错糟得多的失败形态。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/positions/batch-set-enabled",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "上限同 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest（超限整批 400 拒绝，不截断）。",
                "required": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "Employee",
        "title": "员工 /employees",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/employees",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "企业 ID"
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "企业编码"
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "部门 ID"
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "部门编码"
              },
              {
                "name": "includeChildDepartments",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "按部门查询时是否包含其全部下级部门（默认 false，向后兼容）。"
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "required": false,
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）"
              },
              {
                "name": "hasAccount",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "按是否已生成登录账号筛选：true=已生成账号，false=未生成账号，null=不限。"
              },
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（按名称/编码模糊搜索）"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "所属企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "所属企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "所属企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": true
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "positionId",
                "cnName": "",
                "type": "Guid",
                "description": "岗位 ID",
                "nullable": true
              },
              {
                "name": "positionCode",
                "cnName": "",
                "type": "string",
                "description": "岗位编码",
                "nullable": true
              },
              {
                "name": "positionName",
                "cnName": "",
                "type": "string",
                "description": "岗位名称",
                "nullable": true
              },
              {
                "name": "positionRef",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "positionRef.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "positionRef.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "positionRef.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "关联平台账号 ID",
                "nullable": true
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号",
                "nullable": true
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（文本）",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/employees",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "所属企业 ID",
                "required": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "所属企业编码",
                "required": false
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "required": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "required": false
              },
              {
                "name": "positionId",
                "cnName": "",
                "type": "Guid",
                "description": "岗位 ID",
                "required": false
              },
              {
                "name": "positionCode",
                "cnName": "",
                "type": "string",
                "description": "岗位编码",
                "required": false
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "关联平台账号 ID",
                "required": false
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名",
                "required": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "required": false
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号",
                "required": false
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（文本）",
                "required": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "所属企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "所属企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "所属企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": true
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "positionId",
                "cnName": "",
                "type": "Guid",
                "description": "岗位 ID",
                "nullable": true
              },
              {
                "name": "positionCode",
                "cnName": "",
                "type": "string",
                "description": "岗位编码",
                "nullable": true
              },
              {
                "name": "positionName",
                "cnName": "",
                "type": "string",
                "description": "岗位名称",
                "nullable": true
              },
              {
                "name": "positionRef",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "positionRef.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "positionRef.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "positionRef.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "关联平台账号 ID",
                "nullable": true
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号",
                "nullable": true
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（文本）",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/employees/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "所属企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "所属企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "所属企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": true
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "positionId",
                "cnName": "",
                "type": "Guid",
                "description": "岗位 ID",
                "nullable": true
              },
              {
                "name": "positionCode",
                "cnName": "",
                "type": "string",
                "description": "岗位编码",
                "nullable": true
              },
              {
                "name": "positionName",
                "cnName": "",
                "type": "string",
                "description": "岗位名称",
                "nullable": true
              },
              {
                "name": "positionRef",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "positionRef.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "positionRef.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "positionRef.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "关联平台账号 ID",
                "nullable": true
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号",
                "nullable": true
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（文本）",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/employees/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "所属企业 ID",
                "required": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "所属企业编码",
                "required": false
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "required": false
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "required": false
              },
              {
                "name": "positionId",
                "cnName": "",
                "type": "Guid",
                "description": "岗位 ID",
                "required": false
              },
              {
                "name": "positionCode",
                "cnName": "",
                "type": "string",
                "description": "岗位编码",
                "required": false
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "关联平台账号 ID",
                "required": false
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名",
                "required": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "required": false
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号",
                "required": false
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（文本）",
                "required": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "所属企业 ID",
                "nullable": false
              },
              {
                "name": "enterpriseCode",
                "cnName": "",
                "type": "string",
                "description": "所属企业编码",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "所属企业名称",
                "nullable": true
              },
              {
                "name": "enterprise",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "enterprise.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "enterprise.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "enterprise.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "部门 ID",
                "nullable": true
              },
              {
                "name": "departmentCode",
                "cnName": "",
                "type": "string",
                "description": "部门编码",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "部门名称",
                "nullable": true
              },
              {
                "name": "department",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "department.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "department.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "department.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "positionId",
                "cnName": "",
                "type": "Guid",
                "description": "岗位 ID",
                "nullable": true
              },
              {
                "name": "positionCode",
                "cnName": "",
                "type": "string",
                "description": "岗位编码",
                "nullable": true
              },
              {
                "name": "positionName",
                "cnName": "",
                "type": "string",
                "description": "岗位名称",
                "nullable": true
              },
              {
                "name": "positionRef",
                "cnName": "",
                "type": "EntityReferenceDto",
                "description": "",
                "nullable": false
              },
              {
                "name": "positionRef.id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": true
              },
              {
                "name": "positionRef.code",
                "cnName": "",
                "type": "string",
                "description": "业务编码",
                "nullable": true
              },
              {
                "name": "positionRef.name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "关联平台账号 ID",
                "nullable": true
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号",
                "nullable": true
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（文本）",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/employees/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/employees/batch-delete",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "实体 Id 列表。上限见 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest——**超限整批拒绝**\r\n（400），不截断：静默只处理前 N 个会让用户以为全做完了，是比报错糟得多的失败形态。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/employees/batch-generate-accounts",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "员工 Id 列表（语义同 SwapMid.EnterpriseManagement.BatchIdsInput.Ids）。\r\n            上限同 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest（超限整批 400 拒绝，不截断）。\r\n            要覆盖全员规模请改用 SwapMid.EnterpriseManagement.BulkGenerateAccountsInput——那条按条件在服务端查询，\r\n            不受任何 id 列表长度限制，正是「批量上限」这类约束该留的正门。",
                "required": true
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "批量开通时追加授予的角色名（可选；null/空=沿用现行为，仅授予默认角色）。\r\n            语义与 SwapMid.EnterpriseManagement.BulkGenerateAccountsInput.RoleNames 完全一致：\r\n            追加于默认角色之上（并集，不替换），重复项幂等去重；含不存在的角色名整批前置拒绝（零账号创建）。\r\n            非空时要求调用者另持账号域「分配角色」权限（AccountManagement.Users.Update），且系统内置角色（isStatic，如 admin）不可授予。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              },
              {
                "name": "created",
                "cnName": "",
                "type": "integer",
                "description": "新建账号数。",
                "nullable": false
              },
              {
                "name": "alreadyBound",
                "cnName": "",
                "type": "integer",
                "description": "已绑定（已有账号）数。",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/employees/batch-set-status",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "上限同 SwapMid.SharedKernel.BatchOperationLimits.MaxIdsPerRequest（超限整批 400 拒绝，不截断）。",
                "required": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/employees/bulk-generate-accounts",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "enterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "限定企业（可空=全部企业）。",
                "required": false
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "description": "限定部门（可空=不限部门）。",
                "required": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "批量开通时追加授予的角色名（可选；null/空=沿用现行为，仅授予默认角色）。\r\n            语义为在默认角色基础上\"追加\"（并集，不替换），与默认角色重复的项自动幂等去重。\r\n            全部角色名须已存在，否则整批前置拒绝（不创建任何账号），不进入逐条容错。\r\n            非空时要求调用者另持账号域「分配角色」权限（AccountManagement.Users.Update），且系统内置角色（isStatic，如 admin）不可授予。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              },
              {
                "name": "created",
                "cnName": "",
                "type": "integer",
                "description": "新建账号数。",
                "nullable": false
              },
              {
                "name": "alreadyBound",
                "cnName": "",
                "type": "integer",
                "description": "已绑定（已有账号）数。",
                "nullable": false
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "account",
    "title": "账号与权限",
    "description": "ABP Identity 账号、角色、细粒度权限矩阵、密码策略、外部 OIDC 联邦与受控代登录。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "AccountUser",
        "title": "账号 /account-users",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/account-users",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤。"
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "是否启用筛选。"
              },
              {
                "name": "departmentId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按部门筛选账号（账号→员工反向关联：取该部门下员工绑定的账号）。"
              },
              {
                "name": "includeChildDepartments",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "部门筛选时是否含下级部门（默认含）。"
              },
              {
                "name": "roleId",
                "cnName": "",
                "type": "Guid",
                "required": false,
                "description": "按角色筛选账号（该角色的全部成员；服务端下推到 IdentityUser.Roles 导航集合查询，非内存过滤）。"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "nullable": true
              },
              {
                "name": "surname",
                "cnName": "",
                "type": "string",
                "description": "姓。",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "nullable": true
              },
              {
                "name": "phoneNumber",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "nullable": true
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "所属角色名集合。",
                "nullable": true
              },
              {
                "name": "boundEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的员工 ID。",
                "nullable": true
              },
              {
                "name": "boundEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工工号。",
                "nullable": true
              },
              {
                "name": "boundEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工姓名。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的企业 ID。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseName",
                "cnName": "",
                "type": "string",
                "description": "绑定的企业名称。",
                "nullable": true
              },
              {
                "name": "boundDepartmentId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的部门 ID。",
                "nullable": true
              },
              {
                "name": "boundDepartmentName",
                "cnName": "",
                "type": "string",
                "description": "绑定的部门名称。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-users",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "required": false
              },
              {
                "name": "surname",
                "cnName": "",
                "type": "string",
                "description": "姓。",
                "required": false
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "required": true
              },
              {
                "name": "phoneNumber",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "required": false
              },
              {
                "name": "password",
                "cnName": "",
                "type": "string",
                "description": "登录密码。",
                "required": true
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              },
              {
                "name": "employeeId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的员工 ID。",
                "required": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "分配的角色名集合。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "nullable": true
              },
              {
                "name": "surname",
                "cnName": "",
                "type": "string",
                "description": "姓。",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "nullable": true
              },
              {
                "name": "phoneNumber",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "nullable": true
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "所属角色名集合。",
                "nullable": true
              },
              {
                "name": "boundEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的员工 ID。",
                "nullable": true
              },
              {
                "name": "boundEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工工号。",
                "nullable": true
              },
              {
                "name": "boundEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工姓名。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的企业 ID。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseName",
                "cnName": "",
                "type": "string",
                "description": "绑定的企业名称。",
                "nullable": true
              },
              {
                "name": "boundDepartmentId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的部门 ID。",
                "nullable": true
              },
              {
                "name": "boundDepartmentName",
                "cnName": "",
                "type": "string",
                "description": "绑定的部门名称。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-users/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "nullable": true
              },
              {
                "name": "surname",
                "cnName": "",
                "type": "string",
                "description": "姓。",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "nullable": true
              },
              {
                "name": "phoneNumber",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "nullable": true
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "所属角色名集合。",
                "nullable": true
              },
              {
                "name": "boundEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的员工 ID。",
                "nullable": true
              },
              {
                "name": "boundEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工工号。",
                "nullable": true
              },
              {
                "name": "boundEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工姓名。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的企业 ID。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseName",
                "cnName": "",
                "type": "string",
                "description": "绑定的企业名称。",
                "nullable": true
              },
              {
                "name": "boundDepartmentId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的部门 ID。",
                "nullable": true
              },
              {
                "name": "boundDepartmentName",
                "cnName": "",
                "type": "string",
                "description": "绑定的部门名称。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/account-users/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "required": false
              },
              {
                "name": "surname",
                "cnName": "",
                "type": "string",
                "description": "姓。",
                "required": false
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "required": true
              },
              {
                "name": "phoneNumber",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "required": false
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              },
              {
                "name": "employeeId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的员工 ID。",
                "required": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "分配的角色名集合。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "nullable": true
              },
              {
                "name": "surname",
                "cnName": "",
                "type": "string",
                "description": "姓。",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "nullable": true
              },
              {
                "name": "phoneNumber",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "nullable": true
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "所属角色名集合。",
                "nullable": true
              },
              {
                "name": "boundEmployeeId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的员工 ID。",
                "nullable": true
              },
              {
                "name": "boundEmployeeNo",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工工号。",
                "nullable": true
              },
              {
                "name": "boundEmployeeName",
                "cnName": "",
                "type": "string",
                "description": "绑定的员工姓名。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的企业 ID。",
                "nullable": true
              },
              {
                "name": "boundEnterpriseName",
                "cnName": "",
                "type": "string",
                "description": "绑定的企业名称。",
                "nullable": true
              },
              {
                "name": "boundDepartmentId",
                "cnName": "",
                "type": "Guid",
                "description": "绑定的部门 ID。",
                "nullable": true
              },
              {
                "name": "boundDepartmentName",
                "cnName": "",
                "type": "string",
                "description": "绑定的部门名称。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/account-users/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-users/{id}/bound-employee",
            "summary": "查询账号绑定员工的基本信息（账号页点击绑定员工标签用；无绑定返回 null）。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "employeeId",
                "cnName": "",
                "type": "Guid",
                "description": "员工 ID。",
                "nullable": false
              },
              {
                "name": "employeeNo",
                "cnName": "",
                "type": "string",
                "description": "工号。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "姓名。",
                "nullable": true
              },
              {
                "name": "enterpriseName",
                "cnName": "",
                "type": "string",
                "description": "所属企业名称。",
                "nullable": true
              },
              {
                "name": "departmentName",
                "cnName": "",
                "type": "string",
                "description": "所属部门名称。",
                "nullable": true
              },
              {
                "name": "positionName",
                "cnName": "",
                "type": "string",
                "description": "岗位名称（关联岗位主数据）。",
                "nullable": true
              },
              {
                "name": "position",
                "cnName": "",
                "type": "string",
                "description": "岗位名称文本快照（岗位主数据缺失时的兜底展示）。",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱。",
                "nullable": true
              },
              {
                "name": "phone",
                "cnName": "",
                "type": "string",
                "description": "手机号。",
                "nullable": true
              },
              {
                "name": "status",
                "cnName": "",
                "type": "EmployeeStatus",
                "description": "状态；取值：0=Active（在职）；1=Suspended（停薪留职）；2=Resigned（离职）",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-users/{id}/reset-password",
            "summary": "reset-password",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-users/{id}/set-active",
            "summary": "启用或停用账号（列表行内切换）。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              },
              {
                "name": "isActive",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": ""
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-users/assignable-roles",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "nullable": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "nullable": false
              },
              {
                "name": "isStatic",
                "cnName": "",
                "type": "boolean",
                "description": "是否为内置角色（系统预置，不可删除）。",
                "nullable": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移）。",
                "nullable": true
              },
              {
                "name": "memberCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前成员数。",
                "nullable": false
              },
              {
                "name": "permissionCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前已授予的权限数。",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳（乐观并发，PUT 时需回传）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-users/batch-set-roles",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "userIds",
                "cnName": "",
                "type": "Guid[]",
                "description": "目标账号 ID 集合。",
                "required": true
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "目标角色名集合。覆盖模式下传空 = 清空所选用户的全部角色。",
                "required": false
              },
              {
                "name": "append",
                "cnName": "",
                "type": "boolean",
                "description": "true = 追加（保留用户现有角色，合并去重）；false = 覆盖为指定集合。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-users/for-assignment",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "账号唯一标识。",
                "nullable": false
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "用户名。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名。",
                "nullable": true
              },
              {
                "name": "roleNames",
                "cnName": "",
                "type": "string[]",
                "description": "所属角色名集合。",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-users/generate-from-employee",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "employeeId",
                "cnName": "",
                "type": "Guid",
                "description": "员工 ID。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "employeeId",
                "cnName": "",
                "type": "Guid",
                "description": "员工 ID。",
                "nullable": false
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "开通/绑定后的账号 ID。",
                "nullable": true
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "账号用户名。",
                "nullable": true
              },
              {
                "name": "created",
                "cnName": "",
                "type": "boolean",
                "description": "本次是否新建了账号。",
                "nullable": false
              },
              {
                "name": "alreadyBound",
                "cnName": "",
                "type": "boolean",
                "description": "该员工是否此前已绑定账号。",
                "nullable": false
              },
              {
                "name": "message",
                "cnName": "",
                "type": "string",
                "description": "处理结果说明。",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-users/set-role-members",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "roleName",
                "cnName": "",
                "type": "string",
                "description": "角色名。",
                "required": true
              },
              {
                "name": "userIds",
                "cnName": "",
                "type": "Guid[]",
                "description": "勾选的成员集合；不在集合中的现有成员将被移出该角色。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "requested",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "succeeded",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "skipped",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "messages",
                "cnName": "",
                "type": "string[]",
                "description": "",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "AuditUserLookup",
        "title": "审计用户检索 /audit-users/lookup",
        "endpoints": [
          {
            "method": "POST",
            "path": "/api/v1/audit-users/lookup",
            "summary": "按用户 Id 批量查询审计显示名与登录名。",
            "summaryDocumented": true,
            "requestBody": [
              {
                "name": "ids",
                "cnName": "",
                "type": "Guid[]",
                "description": "待解析的用户 Id 集合。超过单请求上限时整批拒绝，调用方应按上限分片，不得依赖服务端截断。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "用户 Id。",
                "nullable": false
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名；优先使用姓名，姓名为空时回退用户名。",
                "nullable": true
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "登录用户名。面向全部登录用户的最小目录字段，不是联系方式或组织资料。",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "AccountRole",
        "title": "角色 /account-roles",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/account-roles",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤。"
              },
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围开始，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "时间范围结束，yyyy-MM-dd HH:mm:ss"
              },
              {
                "name": "timeType",
                "cnName": "",
                "type": "ListQueryTimeType",
                "required": false,
                "description": "时间筛选针对的字段；取值：0=CreationTime；1=LastModificationTime；10=StartedAt；11=FinishedAt；20=DeliveredAt"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "nullable": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "nullable": false
              },
              {
                "name": "isStatic",
                "cnName": "",
                "type": "boolean",
                "description": "是否为内置角色（系统预置，不可删除）。",
                "nullable": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移）。",
                "nullable": true
              },
              {
                "name": "memberCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前成员数。",
                "nullable": false
              },
              {
                "name": "permissionCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前已授予的权限数。",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳（乐观并发，PUT 时需回传）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-roles",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "required": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "required": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移，可为空）。",
                "required": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳：编辑时须回传创建/上次查询得到的值，新建时忽略。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "nullable": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "nullable": false
              },
              {
                "name": "isStatic",
                "cnName": "",
                "type": "boolean",
                "description": "是否为内置角色（系统预置，不可删除）。",
                "nullable": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移）。",
                "nullable": true
              },
              {
                "name": "memberCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前成员数。",
                "nullable": false
              },
              {
                "name": "permissionCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前已授予的权限数。",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳（乐观并发，PUT 时需回传）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-roles/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "nullable": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "nullable": false
              },
              {
                "name": "isStatic",
                "cnName": "",
                "type": "boolean",
                "description": "是否为内置角色（系统预置，不可删除）。",
                "nullable": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移）。",
                "nullable": true
              },
              {
                "name": "memberCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前成员数。",
                "nullable": false
              },
              {
                "name": "permissionCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前已授予的权限数。",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳（乐观并发，PUT 时需回传）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/account-roles/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "required": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "required": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移，可为空）。",
                "required": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳：编辑时须回传创建/上次查询得到的值，新建时忽略。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "nullable": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "nullable": false
              },
              {
                "name": "isStatic",
                "cnName": "",
                "type": "boolean",
                "description": "是否为内置角色（系统预置，不可删除）。",
                "nullable": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移）。",
                "nullable": true
              },
              {
                "name": "memberCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前成员数。",
                "nullable": false
              },
              {
                "name": "permissionCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前已授予的权限数。",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳（乐观并发，PUT 时需回传）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/account-roles/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/account-roles/{id}/copy",
            "summary": "copy",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "新角色名称。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "角色名称。",
                "nullable": true
              },
              {
                "name": "isDefault",
                "cnName": "",
                "type": "boolean",
                "description": "是否为默认角色（新用户自动分配）。",
                "nullable": false
              },
              {
                "name": "isStatic",
                "cnName": "",
                "type": "boolean",
                "description": "是否为内置角色（系统预置，不可删除）。",
                "nullable": false
              },
              {
                "name": "isPublic",
                "cnName": "",
                "type": "boolean",
                "description": "是否公开角色。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "角色描述（走 ExtraProperties 存储，零迁移）。",
                "nullable": true
              },
              {
                "name": "memberCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前成员数。",
                "nullable": false
              },
              {
                "name": "permissionCount",
                "cnName": "",
                "type": "integer",
                "description": "该角色当前已授予的权限数。",
                "nullable": false
              },
              {
                "name": "concurrencyStamp",
                "cnName": "",
                "type": "string",
                "description": "并发戳（乐观并发，PUT 时需回传）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-roles/{roleName}/permission-changes",
            "summary": "查询角色权限变更历史(从审计日志提取语义化 diff,按时间倒序)。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "roleName",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "executionTime",
                "cnName": "",
                "type": "DateTime",
                "description": "变更发生时间（对应本次 PUT 请求的执行时间）。",
                "nullable": false
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "操作人用户名（未登录/系统调用可能为空）。",
                "nullable": true
              },
              {
                "name": "changes",
                "cnName": "",
                "type": "string[]",
                "description": "变更摘要行（形如 `{roleName} +[a,b] -[c]`，已剥离固定前缀 `[RolePermissionChange] `、\r\n            行首保留角色名；通常一条 PUT 请求对应一行;若该次请求未产生实际权限变化，为空列表。\r\n            消费端按 `+[...]`/`-[...]` 括号锚点解析,勿依赖行首形态）。",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "AccountPermission",
        "title": "权限 /account-roles",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/account-roles/{roleName}/permissions",
            "summary": "permissions",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "roleName",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "roleName",
                "cnName": "",
                "type": "string",
                "description": "角色名。",
                "nullable": true
              },
              {
                "name": "groups",
                "cnName": "",
                "type": "AccountPermissionGroupDto[]",
                "description": "权限分组列表。",
                "nullable": true
              },
              {
                "name": "groups[].name",
                "cnName": "",
                "type": "string",
                "description": "分组名（唯一标识）。",
                "nullable": true
              },
              {
                "name": "groups[].displayName",
                "cnName": "",
                "type": "string",
                "description": "分组显示名称。",
                "nullable": true
              },
              {
                "name": "groups[].permissions",
                "cnName": "",
                "type": "AccountPermissionDto[]",
                "description": "该分组下的权限项列表。",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/account-roles/{roleName}/permissions",
            "summary": "permissions",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "roleName",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ],
            "requestBody": [
              {
                "name": "permissions",
                "cnName": "",
                "type": "AccountPermissionGrantDto[]",
                "description": "权限授予变更项集合。",
                "required": false
              },
              {
                "name": "permissions[].name",
                "cnName": "",
                "type": "string",
                "description": "权限名（唯一标识）。",
                "required": false
              },
              {
                "name": "permissions[].isGranted",
                "cnName": "",
                "type": "boolean",
                "description": "是否授予。",
                "required": false
              }
            ]
          }
        ]
      },
      {
        "id": "AccountSettings",
        "title": "账号安全设置 /account-management/settings/defaults",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/account-management/settings/defaults",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "defaultPassword",
                "cnName": "",
                "type": "string",
                "description": "新账号初始密码。",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/account-management/settings/defaults",
            "summary": "更新",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "defaultPassword",
                "cnName": "",
                "type": "string",
                "description": "新账号初始密码。",
                "required": true
              }
            ],
            "responseFields": [
              {
                "name": "defaultPassword",
                "cnName": "",
                "type": "string",
                "description": "新账号初始密码。",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/account-management/settings/security",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "defaultPassword",
                "cnName": "",
                "type": "string",
                "description": "新账号初始密码。",
                "nullable": true
              },
              {
                "name": "forceChangePasswordOnFirstLogin",
                "cnName": "",
                "type": "boolean",
                "description": "首次登录是否强制修改密码。",
                "nullable": false
              },
              {
                "name": "passwordRequiredLength",
                "cnName": "",
                "type": "integer",
                "description": "密码最小长度。",
                "nullable": false
              },
              {
                "name": "passwordRequireDigit",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含数字。",
                "nullable": false
              },
              {
                "name": "passwordRequireLowercase",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含小写字母。",
                "nullable": false
              },
              {
                "name": "passwordRequireUppercase",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含大写字母。",
                "nullable": false
              },
              {
                "name": "passwordRequireNonAlphanumeric",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含非字母数字字符（特殊符号）。",
                "nullable": false
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/account-management/settings/security",
            "summary": "更新",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "defaultPassword",
                "cnName": "",
                "type": "string",
                "description": "新账号初始密码。",
                "required": true
              },
              {
                "name": "forceChangePasswordOnFirstLogin",
                "cnName": "",
                "type": "boolean",
                "description": "首次登录是否强制修改密码。",
                "required": false
              },
              {
                "name": "passwordRequiredLength",
                "cnName": "",
                "type": "integer",
                "description": "密码最小长度。",
                "required": false
              },
              {
                "name": "passwordRequireDigit",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含数字。",
                "required": false
              },
              {
                "name": "passwordRequireLowercase",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含小写字母。",
                "required": false
              },
              {
                "name": "passwordRequireUppercase",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含大写字母。",
                "required": false
              },
              {
                "name": "passwordRequireNonAlphanumeric",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含非字母数字字符（特殊符号）。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "defaultPassword",
                "cnName": "",
                "type": "string",
                "description": "新账号初始密码。",
                "nullable": true
              },
              {
                "name": "forceChangePasswordOnFirstLogin",
                "cnName": "",
                "type": "boolean",
                "description": "首次登录是否强制修改密码。",
                "nullable": false
              },
              {
                "name": "passwordRequiredLength",
                "cnName": "",
                "type": "integer",
                "description": "密码最小长度。",
                "nullable": false
              },
              {
                "name": "passwordRequireDigit",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含数字。",
                "nullable": false
              },
              {
                "name": "passwordRequireLowercase",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含小写字母。",
                "nullable": false
              },
              {
                "name": "passwordRequireUppercase",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含大写字母。",
                "nullable": false
              },
              {
                "name": "passwordRequireNonAlphanumeric",
                "cnName": "",
                "type": "boolean",
                "description": "密码是否必须包含非字母数字字符（特殊符号）。",
                "nullable": false
              }
            ]
          }
        ]
      },
      {
        "id": "CurrentUserPermission",
        "title": "当前用户权限 /account-users",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/account-users/{userId}/effective-permissions",
            "summary": "查询指定用户当前生效的权限清单(按分组组织,每项附授予来源)。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "关联账号 ID"
              }
            ],
            "responseFields": [
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "用户 Id。",
                "nullable": false
              },
              {
                "name": "groups",
                "cnName": "",
                "type": "AccountPermissionGroupDto[]",
                "description": "权限分组列表。",
                "nullable": true
              },
              {
                "name": "groups[].name",
                "cnName": "",
                "type": "string",
                "description": "分组名（唯一标识）。",
                "nullable": true
              },
              {
                "name": "groups[].displayName",
                "cnName": "",
                "type": "string",
                "description": "分组显示名称。",
                "nullable": true
              },
              {
                "name": "groups[].permissions",
                "cnName": "",
                "type": "AccountPermissionDto[]",
                "description": "该分组下的权限项列表。",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/my-permissions",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "grantedPermissions",
                "cnName": "",
                "type": "string[]",
                "description": "已授予的权限名集合。",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "AuthProvider",
        "title": "身份认证提供商 /auth-providers",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/auth-providers",
            "summary": "查询登录页可用的外部身份认证提供方公开配置（不含客户端密钥）。",
            "summaryDocumented": true,
            "responseFields": [
              {
                "name": "externalOidcEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否已启用并完整配置外部 OIDC 单点登录。",
                "nullable": false
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "外部 OIDC 提供方展示名称（如 Enterprise SSO / Keycloak / Entra ID）。",
                "nullable": true
              },
              {
                "name": "authority",
                "cnName": "",
                "type": "string",
                "description": "OIDC Issuer / Authority 根地址（仅启用时返回）。",
                "nullable": true
              },
              {
                "name": "clientId",
                "cnName": "",
                "type": "string",
                "description": "公开 OIDC ClientId（仅启用时返回）。",
                "nullable": true
              },
              {
                "name": "authorizationEndpoint",
                "cnName": "",
                "type": "string",
                "description": "可选显式覆盖的授权端点 URL；为空时由 BFF 按 OIDC Discovery 自动发现。",
                "nullable": true
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "system",
    "title": "系统运维",
    "description": "编码规则、两级数据字典、定时任务调度、审计日志与系统健康检查。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "CodeRule",
        "title": "编码规则 /code-rules",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/code-rules",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤。"
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "是否启用筛选。"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "适用实体类型（编码规则的业务对象标识）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名称。",
                "nullable": true
              },
              {
                "name": "prefix",
                "cnName": "",
                "type": "string",
                "description": "编码前缀。",
                "nullable": true
              },
              {
                "name": "suffix",
                "cnName": "",
                "type": "string",
                "description": "编码后缀。",
                "nullable": true
              },
              {
                "name": "includeDate",
                "cnName": "",
                "type": "boolean",
                "description": "编码中是否包含日期段。",
                "nullable": false
              },
              {
                "name": "dateFormat",
                "cnName": "",
                "type": "string",
                "description": "日期段格式（如 yyyyMMdd）。",
                "nullable": true
              },
              {
                "name": "sequencePad",
                "cnName": "",
                "type": "integer",
                "description": "流水号长度（不足左侧补零）。",
                "nullable": false
              },
              {
                "name": "resetCycle",
                "cnName": "",
                "type": "SequenceResetCycle",
                "description": "取值：0=Never；1=Daily；2=Monthly；3=Yearly",
                "nullable": false
              },
              {
                "name": "currentSequence",
                "cnName": "",
                "type": "integer",
                "description": "当前流水号。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "sample",
                "cnName": "",
                "type": "string",
                "description": "编码样例（按当前规则生成的示例）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/code-rules",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "适用实体类型（编码规则的业务对象标识）。",
                "required": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名称。",
                "required": true
              },
              {
                "name": "prefix",
                "cnName": "",
                "type": "string",
                "description": "编码前缀。",
                "required": false
              },
              {
                "name": "suffix",
                "cnName": "",
                "type": "string",
                "description": "编码后缀。",
                "required": false
              },
              {
                "name": "includeDate",
                "cnName": "",
                "type": "boolean",
                "description": "编码中是否包含日期段。",
                "required": false
              },
              {
                "name": "dateFormat",
                "cnName": "",
                "type": "string",
                "description": "日期段格式（如 yyyyMMdd）。",
                "required": false
              },
              {
                "name": "sequencePad",
                "cnName": "",
                "type": "integer",
                "description": "流水号长度（不足左侧补零）。",
                "required": false
              },
              {
                "name": "resetCycle",
                "cnName": "",
                "type": "SequenceResetCycle",
                "description": "取值：0=Never；1=Daily；2=Monthly；3=Yearly",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "适用实体类型（编码规则的业务对象标识）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名称。",
                "nullable": true
              },
              {
                "name": "prefix",
                "cnName": "",
                "type": "string",
                "description": "编码前缀。",
                "nullable": true
              },
              {
                "name": "suffix",
                "cnName": "",
                "type": "string",
                "description": "编码后缀。",
                "nullable": true
              },
              {
                "name": "includeDate",
                "cnName": "",
                "type": "boolean",
                "description": "编码中是否包含日期段。",
                "nullable": false
              },
              {
                "name": "dateFormat",
                "cnName": "",
                "type": "string",
                "description": "日期段格式（如 yyyyMMdd）。",
                "nullable": true
              },
              {
                "name": "sequencePad",
                "cnName": "",
                "type": "integer",
                "description": "流水号长度（不足左侧补零）。",
                "nullable": false
              },
              {
                "name": "resetCycle",
                "cnName": "",
                "type": "SequenceResetCycle",
                "description": "取值：0=Never；1=Daily；2=Monthly；3=Yearly",
                "nullable": false
              },
              {
                "name": "currentSequence",
                "cnName": "",
                "type": "integer",
                "description": "当前流水号。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "sample",
                "cnName": "",
                "type": "string",
                "description": "编码样例（按当前规则生成的示例）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/code-rules/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "适用实体类型（编码规则的业务对象标识）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名称。",
                "nullable": true
              },
              {
                "name": "prefix",
                "cnName": "",
                "type": "string",
                "description": "编码前缀。",
                "nullable": true
              },
              {
                "name": "suffix",
                "cnName": "",
                "type": "string",
                "description": "编码后缀。",
                "nullable": true
              },
              {
                "name": "includeDate",
                "cnName": "",
                "type": "boolean",
                "description": "编码中是否包含日期段。",
                "nullable": false
              },
              {
                "name": "dateFormat",
                "cnName": "",
                "type": "string",
                "description": "日期段格式（如 yyyyMMdd）。",
                "nullable": true
              },
              {
                "name": "sequencePad",
                "cnName": "",
                "type": "integer",
                "description": "流水号长度（不足左侧补零）。",
                "nullable": false
              },
              {
                "name": "resetCycle",
                "cnName": "",
                "type": "SequenceResetCycle",
                "description": "取值：0=Never；1=Daily；2=Monthly；3=Yearly",
                "nullable": false
              },
              {
                "name": "currentSequence",
                "cnName": "",
                "type": "integer",
                "description": "当前流水号。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "sample",
                "cnName": "",
                "type": "string",
                "description": "编码样例（按当前规则生成的示例）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/code-rules/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "适用实体类型（编码规则的业务对象标识）。",
                "required": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名称。",
                "required": true
              },
              {
                "name": "prefix",
                "cnName": "",
                "type": "string",
                "description": "编码前缀。",
                "required": false
              },
              {
                "name": "suffix",
                "cnName": "",
                "type": "string",
                "description": "编码后缀。",
                "required": false
              },
              {
                "name": "includeDate",
                "cnName": "",
                "type": "boolean",
                "description": "编码中是否包含日期段。",
                "required": false
              },
              {
                "name": "dateFormat",
                "cnName": "",
                "type": "string",
                "description": "日期段格式（如 yyyyMMdd）。",
                "required": false
              },
              {
                "name": "sequencePad",
                "cnName": "",
                "type": "integer",
                "description": "流水号长度（不足左侧补零）。",
                "required": false
              },
              {
                "name": "resetCycle",
                "cnName": "",
                "type": "SequenceResetCycle",
                "description": "取值：0=Never；1=Daily；2=Monthly；3=Yearly",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "适用实体类型（编码规则的业务对象标识）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "显示名称。",
                "nullable": true
              },
              {
                "name": "prefix",
                "cnName": "",
                "type": "string",
                "description": "编码前缀。",
                "nullable": true
              },
              {
                "name": "suffix",
                "cnName": "",
                "type": "string",
                "description": "编码后缀。",
                "nullable": true
              },
              {
                "name": "includeDate",
                "cnName": "",
                "type": "boolean",
                "description": "编码中是否包含日期段。",
                "nullable": false
              },
              {
                "name": "dateFormat",
                "cnName": "",
                "type": "string",
                "description": "日期段格式（如 yyyyMMdd）。",
                "nullable": true
              },
              {
                "name": "sequencePad",
                "cnName": "",
                "type": "integer",
                "description": "流水号长度（不足左侧补零）。",
                "nullable": false
              },
              {
                "name": "resetCycle",
                "cnName": "",
                "type": "SequenceResetCycle",
                "description": "取值：0=Never；1=Daily；2=Monthly；3=Yearly",
                "nullable": false
              },
              {
                "name": "currentSequence",
                "cnName": "",
                "type": "integer",
                "description": "当前流水号。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "sample",
                "cnName": "",
                "type": "string",
                "description": "编码样例（按当前规则生成的示例）。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/code-rules/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/code-rules/entity-types",
            "summary": "可选业务对象清单（各域注册；前端下拉选项与列表中文名映射的单一事实源）。",
            "summaryDocumented": true,
            "responseFields": [
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "description": "业务对象标识（= SwapMid.SystemManagement.CodeRuleDto.EntityType，各域常量，须逐字一致）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "业务对象中文名（如「数据管道」「消息主题」，作下拉标签与列表显示名）。",
                "nullable": true
              },
              {
                "name": "group",
                "cnName": "",
                "type": "string",
                "description": "所属业务域分组（下拉可按此分组，如「数据集成」「企业管理」）。",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/code-rules/generate",
            "summary": "新建",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "entityType",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              }
            ]
          }
        ]
      },
      {
        "id": "DataDictionary",
        "title": "数据字典 /data-dictionaries",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/data-dictionaries",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（字典编码 / 名称）。"
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "是否启用筛选。"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "字典编码（业务唯一键）。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "字典名称。",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "itemCount",
                "cnName": "",
                "type": "integer",
                "description": "字典项数量。",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-dictionaries",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "字典编码（业务唯一键，如 gender）。",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "字典名称。",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "字典编码（业务唯一键）。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "字典名称。",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "itemCount",
                "cnName": "",
                "type": "integer",
                "description": "字典项数量。",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/data-dictionaries/{dictionaryId}/items",
            "summary": "items",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "description": "所属字典 Id。",
                "nullable": false
              },
              {
                "name": "label",
                "cnName": "",
                "type": "string",
                "description": "显示文本。",
                "nullable": true
              },
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "实际值。",
                "nullable": true
              },
              {
                "name": "sortOrder",
                "cnName": "",
                "type": "integer",
                "description": "排序号（升序）。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/data-dictionaries/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "字典编码（业务唯一键）。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "字典名称。",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "itemCount",
                "cnName": "",
                "type": "integer",
                "description": "字典项数量。",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/data-dictionaries/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "字典编码（业务唯一键，如 gender）。",
                "required": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "字典名称。",
                "required": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "description": "字典编码（业务唯一键）。",
                "nullable": true
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "字典名称。",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "itemCount",
                "cnName": "",
                "type": "integer",
                "description": "字典项数量。",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/data-dictionaries/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/data-dictionaries/by-code/{code}/items",
            "summary": "items",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "code",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": "业务编码"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "description": "所属字典 Id。",
                "nullable": false
              },
              {
                "name": "label",
                "cnName": "",
                "type": "string",
                "description": "显示文本。",
                "nullable": true
              },
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "实际值。",
                "nullable": true
              },
              {
                "name": "sortOrder",
                "cnName": "",
                "type": "integer",
                "description": "排序号（升序）。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/data-dictionaries/items",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "description": "所属字典 Id（新增时必填；编辑时以路径 id 为准）。",
                "required": false
              },
              {
                "name": "label",
                "cnName": "",
                "type": "string",
                "description": "显示文本。",
                "required": true
              },
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "实际值。",
                "required": true
              },
              {
                "name": "sortOrder",
                "cnName": "",
                "type": "integer",
                "description": "排序号（升序）。",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "description": "所属字典 Id。",
                "nullable": false
              },
              {
                "name": "label",
                "cnName": "",
                "type": "string",
                "description": "显示文本。",
                "nullable": true
              },
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "实际值。",
                "nullable": true
              },
              {
                "name": "sortOrder",
                "cnName": "",
                "type": "integer",
                "description": "排序号（升序）。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/data-dictionaries/items/{itemId}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "itemId",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": ""
              }
            ],
            "requestBody": [
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "description": "所属字典 Id（新增时必填；编辑时以路径 id 为准）。",
                "required": false
              },
              {
                "name": "label",
                "cnName": "",
                "type": "string",
                "description": "显示文本。",
                "required": true
              },
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "实际值。",
                "required": true
              },
              {
                "name": "sortOrder",
                "cnName": "",
                "type": "integer",
                "description": "排序号（升序）。",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "required": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "dictionaryId",
                "cnName": "",
                "type": "Guid",
                "description": "所属字典 Id。",
                "nullable": false
              },
              {
                "name": "label",
                "cnName": "",
                "type": "string",
                "description": "显示文本。",
                "nullable": true
              },
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "实际值。",
                "nullable": true
              },
              {
                "name": "sortOrder",
                "cnName": "",
                "type": "integer",
                "description": "排序号（升序）。",
                "nullable": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述。",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/data-dictionaries/items/{itemId}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "itemId",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": ""
              }
            ]
          }
        ]
      },
      {
        "id": "JobScheduling",
        "title": "定时任务 /scheduled-jobs",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/scheduled-jobs",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "key",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "描述",
                "nullable": true
              },
              {
                "name": "cron",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "enabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否已在 Hangfire 中注册（启用）。",
                "nullable": false
              },
              {
                "name": "supportsParameterizedRun",
                "cnName": "",
                "type": "boolean",
                "description": "是否已接入统一执行管道（IScheduledJobHandler），支持带参 / 演练执行——\r\n            全部受管控任务均已接入，无论启用与否都可据此「立即执行」（恒 true，字段保留供前端按接口收缩演进）。",
                "nullable": false
              },
              {
                "name": "parameters",
                "cnName": "",
                "type": "JobParameterSchema[]",
                "description": "带参执行支持的参数定义（前端据此动态渲染表单）；未接入管道或无参任务为空列表。",
                "nullable": true
              },
              {
                "name": "parameters[].name",
                "cnName": "",
                "type": "string",
                "description": "JSON 字段名（ArgsJson 里的 key），如 \"month\"、\"date\"。",
                "nullable": true
              },
              {
                "name": "parameters[].label",
                "cnName": "",
                "type": "string",
                "description": "中文标签，如「月份」「补刷日期」。",
                "nullable": true
              },
              {
                "name": "parameters[].type",
                "cnName": "",
                "type": "JobParameterType",
                "description": "类型；取值：0=Text；1=Date；2=Month",
                "nullable": false
              },
              {
                "name": "parameters[].required",
                "cnName": "",
                "type": "boolean",
                "description": "是否必填；false 时前端留空即不传该字段，由 handler 走默认值。",
                "nullable": false
              },
              {
                "name": "parameters[].defaultHint",
                "cnName": "",
                "type": "string",
                "description": "留空时的提示文案，如「默认上月」「默认昨天」。",
                "nullable": true
              },
              {
                "name": "isDestructive",
                "cnName": "",
                "type": "boolean",
                "description": "危险操作（物理删除数据 / 写外部生产系统）：前端标红并在「立即执行」前二次确认。",
                "nullable": false
              },
              {
                "name": "isLongRunning",
                "cnName": "",
                "type": "boolean",
                "description": "长任务（实时查外部库的报表 / 全量同步，单次数十秒到分钟级）：前端「立即执行」默认走后台入队\r\n            （RunInBackground），入队后到执行历史看进度；演练仍同步等待结果。",
                "nullable": false
              },
              {
                "name": "mutexGroup",
                "cnName": "",
                "type": "string",
                "description": "运行时互斥组；同组任务不会并发执行。空值表示仅按自身 JobKey 防止重复执行。\r\n该字段只用于发布检查与管控页解释，不提供修改入口。",
                "nullable": true
              },
              {
                "name": "lastExecution",
                "cnName": "",
                "type": "DateTime",
                "description": "",
                "nullable": true
              },
              {
                "name": "nextExecution",
                "cnName": "",
                "type": "DateTime",
                "description": "",
                "nullable": true
              },
              {
                "name": "lastJobState",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/scheduled-jobs/preview-cron",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "cron",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/scheduled-jobs/run",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "key",
                "cnName": "",
                "type": "string",
                "description": "任务 Key（与 ScheduledJobCatalog 一致）。",
                "required": false
              },
              {
                "name": "dryRun",
                "cnName": "",
                "type": "boolean",
                "description": "演练模式：不写库、只发测试收件人，不触达正式订阅人。",
                "required": false
              },
              {
                "name": "testRecipient",
                "cnName": "",
                "type": "string",
                "description": "演练模式的测试收件人邮箱（演练时必填）。",
                "required": false
              },
              {
                "name": "argsJson",
                "cnName": "",
                "type": "string",
                "description": "补偿参数（JSON），如 {\"retainDays\":90}；空 = 按任务默认参数执行。",
                "required": false
              },
              {
                "name": "runInBackground",
                "cnName": "",
                "type": "boolean",
                "description": "后台执行（F6）：入队立即返回（结果 Queued=true），进度与结果看执行历史——供长任务\r\n（跨库批量归档 / 全量对账同步等长耗时作业）避免顶着 HTTP 超时。\r\n仅正式模式生效：演练必须同步等结果（操作者在弹窗前等摘要与测试邮件），传 true 且 DryRun=true 时报错。",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "success",
                "cnName": "",
                "type": "boolean",
                "description": "是否成功（Queued=true 时表示\"已成功入队\"，执行成败看执行历史）。",
                "nullable": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "JobExecutionStatus",
                "description": "状态；取值：0=Running；1=Succeeded；2=Failed；3=Interrupted；4=Skipped",
                "nullable": false
              },
              {
                "name": "summary",
                "cnName": "",
                "type": "string",
                "description": "业务摘要（如「演练 3 行」「生成 12 行」；后台入队时为入队提示文案）。",
                "nullable": true
              },
              {
                "name": "error",
                "cnName": "",
                "type": "string",
                "description": "失败时的错误信息。",
                "nullable": true
              },
              {
                "name": "queued",
                "cnName": "",
                "type": "boolean",
                "description": "已入队后台执行（RunInBackground=true 的返回形态）：结果尚未产生，进度与成败看执行历史。",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/scheduled-jobs/set-enabled",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "key",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": true
              },
              {
                "name": "enabled",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "required": false
              },
              {
                "name": "cron",
                "cnName": "",
                "type": "string",
                "description": "启用时使用的 cron（5 段标准表达式）；为空则用任务默认 cron。",
                "required": false
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/scheduled-jobs/stats",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "enabledCount",
                "cnName": "",
                "type": "integer",
                "description": "已启用任务数。",
                "nullable": false
              },
              {
                "name": "totalCount",
                "cnName": "",
                "type": "integer",
                "description": "受管控任务总数（ScheduledJobCatalog.All.Count）。",
                "nullable": false
              },
              {
                "name": "failed24hCount",
                "cnName": "",
                "type": "integer",
                "description": "近 24 小时内失败的执行次数（按 ScheduledJobExecution.Status=Failed 且 StartedAt 在窗口内统计）。",
                "nullable": false
              },
              {
                "name": "failed24hStartedFrom",
                "cnName": "",
                "type": "DateTime",
                "description": "近 24 小时统计窗口的精确起点（与 StartedAt 相同的服务器本地时间语义），供钻取执行历史复用。",
                "nullable": false
              },
              {
                "name": "failed24hMeasuredAt",
                "cnName": "",
                "type": "DateTime",
                "description": "近 24 小时统计快照的结束时刻（闭区间），避免统计与钻取之间新增记录造成口径漂移。",
                "nullable": false
              },
              {
                "name": "runningCount",
                "cnName": "",
                "type": "integer",
                "description": "当前处于「执行中」的执行次数（正常情况下应恒为 0 或很快归零；长期非 0 提示卡死，见 F3 僵尸执行收敛）。",
                "nullable": false
              }
            ]
          }
        ]
      },
      {
        "id": "ScheduledJobExecution",
        "title": "定时任务执行历史 /scheduling/executions",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/scheduling/executions",
            "summary": "分页查询执行历史（默认按开始时间倒序）。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "jobKey",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "按任务 key 过滤（ScheduledJobCatalog.Key）；空=全部。"
              },
              {
                "name": "status",
                "cnName": "",
                "type": "JobExecutionStatus",
                "required": false,
                "description": "状态；取值：0=Running；1=Succeeded；2=Failed；3=Interrupted；4=Skipped"
              },
              {
                "name": "triggerKind",
                "cnName": "",
                "type": "JobTriggerKind",
                "required": false,
                "description": "取值：0=Scheduled；1=Manual"
              },
              {
                "name": "isDryRun",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "按是否演练过滤；空=全部（含演练与正式）。"
              },
              {
                "name": "startedFrom",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "开始时间下限（含）；按 StartedAt 过滤。"
              },
              {
                "name": "startedTo",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "开始时间上限（含）；按 StartedAt 过滤。"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "jobKey",
                "cnName": "",
                "type": "string",
                "description": "受管控任务 key（ScheduledJobCatalog.Key）；手动入队作业可空。",
                "nullable": true
              },
              {
                "name": "jobType",
                "cnName": "",
                "type": "string",
                "description": "Hangfire 作业类型名。",
                "nullable": true
              },
              {
                "name": "jobMethod",
                "cnName": "",
                "type": "string",
                "description": "Hangfire 作业方法名。",
                "nullable": true
              },
              {
                "name": "triggerKind",
                "cnName": "",
                "type": "JobTriggerKind",
                "description": "取值：0=Scheduled；1=Manual",
                "nullable": false
              },
              {
                "name": "status",
                "cnName": "",
                "type": "JobExecutionStatus",
                "description": "状态；取值：0=Running；1=Succeeded；2=Failed；3=Interrupted；4=Skipped",
                "nullable": false
              },
              {
                "name": "startedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "开始时间。",
                "nullable": false
              },
              {
                "name": "finishedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "结束时间（执行中为空）。",
                "nullable": true
              },
              {
                "name": "durationMs",
                "cnName": "",
                "type": "integer",
                "description": "耗时（毫秒）。",
                "nullable": true
              },
              {
                "name": "summary",
                "cnName": "",
                "type": "string",
                "description": "业务摘要（如「生成 1985 行」）。",
                "nullable": true
              },
              {
                "name": "error",
                "cnName": "",
                "type": "string",
                "description": "失败错误信息。",
                "nullable": true
              },
              {
                "name": "isDryRun",
                "cnName": "",
                "type": "boolean",
                "description": "是否演练；老轨 Hangfire 过滤器记录的执行恒为 false。",
                "nullable": false
              },
              {
                "name": "argsJson",
                "cnName": "",
                "type": "string",
                "description": "带参执行的参数 JSON；无参或老轨执行为空。",
                "nullable": true
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "操作人（手动触发时的当前用户）；定时触发 / 老轨记录为空。",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "AuditLog",
        "title": "审计日志 /audit-logs",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/audit-logs",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "startTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "请求时间起（含）。"
              },
              {
                "name": "endTime",
                "cnName": "",
                "type": "DateTime",
                "required": false,
                "description": "请求时间止（含）。"
              },
              {
                "name": "url",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "URL 模糊匹配。"
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "用户名精确匹配。"
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "HTTP 方法（GET/POST/PUT/DELETE）。"
              },
              {
                "name": "hasException",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": "仅看发生异常的调用。"
              },
              {
                "name": "httpStatusCode",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "HTTP 状态码精确匹配。"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "applicationName",
                "cnName": "",
                "type": "string",
                "description": "应用名称。",
                "nullable": true
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "调用用户 ID。",
                "nullable": true
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "调用用户名。",
                "nullable": true
              },
              {
                "name": "executionTime",
                "cnName": "",
                "type": "DateTime",
                "description": "请求时间。",
                "nullable": false
              },
              {
                "name": "executionDuration",
                "cnName": "",
                "type": "integer",
                "description": "耗时（毫秒）。",
                "nullable": false
              },
              {
                "name": "clientIpAddress",
                "cnName": "",
                "type": "string",
                "description": "客户端 IP。",
                "nullable": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "string",
                "description": "HTTP 方法（GET/POST/PUT/DELETE）。",
                "nullable": true
              },
              {
                "name": "url",
                "cnName": "",
                "type": "string",
                "description": "请求 URL。",
                "nullable": true
              },
              {
                "name": "httpStatusCode",
                "cnName": "",
                "type": "integer",
                "description": "HTTP 状态码。",
                "nullable": true
              },
              {
                "name": "browserInfo",
                "cnName": "",
                "type": "string",
                "description": "浏览器信息。",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "关联 ID（用于串联同一请求链路）。",
                "nullable": true
              },
              {
                "name": "hasException",
                "cnName": "",
                "type": "boolean",
                "description": "是否发生异常（列表轻量标记；异常全文在详情）。",
                "nullable": false
              },
              {
                "name": "exceptions",
                "cnName": "",
                "type": "string",
                "description": "异常全文（仅详情返回）。",
                "nullable": true
              },
              {
                "name": "comments",
                "cnName": "",
                "type": "string",
                "description": "备注。",
                "nullable": true
              },
              {
                "name": "actions",
                "cnName": "",
                "type": "AuditLogActionDto[]",
                "description": "应用服务动作明细（仅详情返回）。",
                "nullable": true
              },
              {
                "name": "actions[].serviceName",
                "cnName": "",
                "type": "string",
                "description": "应用服务名称。",
                "nullable": true
              },
              {
                "name": "actions[].methodName",
                "cnName": "",
                "type": "string",
                "description": "方法名称。",
                "nullable": true
              },
              {
                "name": "actions[].parameters",
                "cnName": "",
                "type": "string",
                "description": "调用参数（JSON；数据源动作等敏感操作固定返回空对象）。",
                "nullable": true
              },
              {
                "name": "actions[].executionTime",
                "cnName": "",
                "type": "DateTime",
                "description": "执行时间。",
                "nullable": false
              },
              {
                "name": "actions[].executionDuration",
                "cnName": "",
                "type": "integer",
                "description": "耗时（毫秒）。",
                "nullable": false
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/audit-logs/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "applicationName",
                "cnName": "",
                "type": "string",
                "description": "应用名称。",
                "nullable": true
              },
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "description": "调用用户 ID。",
                "nullable": true
              },
              {
                "name": "userName",
                "cnName": "",
                "type": "string",
                "description": "调用用户名。",
                "nullable": true
              },
              {
                "name": "executionTime",
                "cnName": "",
                "type": "DateTime",
                "description": "请求时间。",
                "nullable": false
              },
              {
                "name": "executionDuration",
                "cnName": "",
                "type": "integer",
                "description": "耗时（毫秒）。",
                "nullable": false
              },
              {
                "name": "clientIpAddress",
                "cnName": "",
                "type": "string",
                "description": "客户端 IP。",
                "nullable": true
              },
              {
                "name": "httpMethod",
                "cnName": "",
                "type": "string",
                "description": "HTTP 方法（GET/POST/PUT/DELETE）。",
                "nullable": true
              },
              {
                "name": "url",
                "cnName": "",
                "type": "string",
                "description": "请求 URL。",
                "nullable": true
              },
              {
                "name": "httpStatusCode",
                "cnName": "",
                "type": "integer",
                "description": "HTTP 状态码。",
                "nullable": true
              },
              {
                "name": "browserInfo",
                "cnName": "",
                "type": "string",
                "description": "浏览器信息。",
                "nullable": true
              },
              {
                "name": "correlationId",
                "cnName": "",
                "type": "string",
                "description": "关联 ID（用于串联同一请求链路）。",
                "nullable": true
              },
              {
                "name": "hasException",
                "cnName": "",
                "type": "boolean",
                "description": "是否发生异常（列表轻量标记；异常全文在详情）。",
                "nullable": false
              },
              {
                "name": "exceptions",
                "cnName": "",
                "type": "string",
                "description": "异常全文（仅详情返回）。",
                "nullable": true
              },
              {
                "name": "comments",
                "cnName": "",
                "type": "string",
                "description": "备注。",
                "nullable": true
              },
              {
                "name": "actions",
                "cnName": "",
                "type": "AuditLogActionDto[]",
                "description": "应用服务动作明细（仅详情返回）。",
                "nullable": true
              },
              {
                "name": "actions[].serviceName",
                "cnName": "",
                "type": "string",
                "description": "应用服务名称。",
                "nullable": true
              },
              {
                "name": "actions[].methodName",
                "cnName": "",
                "type": "string",
                "description": "方法名称。",
                "nullable": true
              },
              {
                "name": "actions[].parameters",
                "cnName": "",
                "type": "string",
                "description": "调用参数（JSON；数据源动作等敏感操作固定返回空对象）。",
                "nullable": true
              },
              {
                "name": "actions[].executionTime",
                "cnName": "",
                "type": "DateTime",
                "description": "执行时间。",
                "nullable": false
              },
              {
                "name": "actions[].executionDuration",
                "cnName": "",
                "type": "integer",
                "description": "耗时（毫秒）。",
                "nullable": false
              }
            ]
          }
        ]
      },
      {
        "id": "SystemHealth",
        "title": "系统体检 /system-health",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/system-health",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "overallStatus",
                "cnName": "",
                "type": "HealthStatus",
                "description": "取值：0=Healthy（正常）；1=Warning（需留意）；2=Critical（异常）；3=Unknown（未知）",
                "nullable": false
              },
              {
                "name": "overallConclusion",
                "cnName": "",
                "type": "string",
                "description": "总体一句话结论（据 OverallStatus 生成，给管理者一眼看的话）。",
                "nullable": true
              },
              {
                "name": "checkedAt",
                "cnName": "",
                "type": "DateTime",
                "description": "体检时间（服务器本地时间）。",
                "nullable": false
              },
              {
                "name": "items",
                "cnName": "",
                "type": "HealthItemDto[]",
                "description": "各检查项明细。",
                "nullable": true
              },
              {
                "name": "items[].name",
                "cnName": "",
                "type": "string",
                "description": "检查项中文名称（如「网站可访问」「服务器内存」）。",
                "nullable": true
              },
              {
                "name": "items[].status",
                "cnName": "",
                "type": "HealthStatus",
                "description": "状态；取值：0=Healthy（正常）；1=Warning（需留意）；2=Critical（异常）；3=Unknown（未知）",
                "nullable": false
              },
              {
                "name": "items[].conclusion",
                "cnName": "",
                "type": "string",
                "description": "人话结论（如「运行正常，员工可正常访问」）。",
                "nullable": true
              },
              {
                "name": "items[].impact",
                "cnName": "",
                "type": "string",
                "description": "出问题时对业务的影响（如「新提交的订单可能延迟处理」）。正常时可为空。",
                "nullable": true
              },
              {
                "name": "items[].suggestion",
                "cnName": "",
                "type": "string",
                "description": "处置建议（如「通常几分钟自动恢复；超过 10 分钟仍异常请通知 IT」）。正常时可为空。",
                "nullable": true
              },
              {
                "name": "items[].detail",
                "cnName": "",
                "type": "string",
                "description": "可选：原始数值/明细（如「使用率 78%」），供需要细节者参考。",
                "nullable": true
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "notification",
    "title": "通知中心",
    "description": "通用 Bot Webhook 与 SMTP 邮件渠道、分级接收人、通知日志及 Prometheus Alertmanager 告警接入。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "NotificationSettings",
        "title": "通知渠道设置 /notification/settings",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/notification/settings",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "webhookEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "webhookProvider",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "webhookUrl",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "webhookSecretConfigured",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "webhookMentionUsers",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "emailEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "smtpHost",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "smtpPort",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "smtpUseSsl",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "smtpUserName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "smtpPasswordConfigured",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "fromAddress",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "fromName",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "alertTokenConfigured",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "alertWebhookPath",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/notification/settings",
            "summary": "更新",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "webhookEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "required": false
              },
              {
                "name": "webhookProvider",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "webhookUrl",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "webhookSecret",
                "cnName": "",
                "type": "string",
                "description": "Webhook 签名密钥：null 或空串 = 保持原密钥不变；传入 \"__CLEAR__\" 清空密钥。",
                "required": false
              },
              {
                "name": "webhookMentionUsers",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "emailEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "required": false
              },
              {
                "name": "smtpHost",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "smtpPort",
                "cnName": "",
                "type": "integer",
                "description": "",
                "required": false
              },
              {
                "name": "smtpUseSsl",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "required": false
              },
              {
                "name": "smtpUserName",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "smtpPassword",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "fromAddress",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "fromName",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/notification/settings/alert-token",
            "summary": "新建",
            "summaryDocumented": false
          },
          {
            "method": "POST",
            "path": "/api/v1/notification/settings/test",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "channel",
                "cnName": "",
                "type": "NotificationChannelType",
                "description": "取值：1=Webhook；2=Email",
                "required": false
              },
              {
                "name": "level",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "required": false
              },
              {
                "name": "toUser",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "success",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "error",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "NotificationRecipient",
        "title": "通知接收人 /notification/recipients",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/notification/recipients",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "webhookTarget",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "minLevel",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "nullable": false
              },
              {
                "name": "sources",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/notification/recipients",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "required": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "required": false
              },
              {
                "name": "webhookTarget",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "minLevel",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "required": false
              },
              {
                "name": "sources",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "webhookTarget",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "minLevel",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "nullable": false
              },
              {
                "name": "sources",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/notification/recipients/{id}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "webhookTarget",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "minLevel",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "nullable": false
              },
              {
                "name": "sources",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "PUT",
            "path": "/api/v1/notification/recipients/{id}",
            "summary": "更新",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ],
            "requestBody": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "required": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "required": false
              },
              {
                "name": "webhookTarget",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "minLevel",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "required": false
              },
              {
                "name": "sources",
                "cnName": "",
                "type": "string",
                "description": "",
                "required": false
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "名称",
                "nullable": true
              },
              {
                "name": "email",
                "cnName": "",
                "type": "string",
                "description": "邮箱",
                "nullable": true
              },
              {
                "name": "webhookTarget",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "minLevel",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "nullable": false
              },
              {
                "name": "sources",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "isEnabled",
                "cnName": "",
                "type": "boolean",
                "description": "是否启用",
                "nullable": false
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              },
              {
                "name": "lastModificationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "最后修改时间",
                "nullable": true
              },
              {
                "name": "lastModifierId",
                "cnName": "",
                "type": "Guid",
                "description": "最后修改人 ID",
                "nullable": true
              }
            ]
          },
          {
            "method": "DELETE",
            "path": "/api/v1/notification/recipients/{id}",
            "summary": "删除",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "唯一标识"
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/notification/recipients/sources",
            "summary": "全部已知通知来源（代码常量为事实源）：「订阅来源」下拉选项，替代手输防错配。",
            "summaryDocumented": true
          }
        ]
      },
      {
        "id": "NotificationLog",
        "title": "通知发送日志 /notification/logs",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/notification/logs",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "source",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              },
              {
                "name": "success",
                "cnName": "",
                "type": "boolean",
                "required": false,
                "description": ""
              },
              {
                "name": "level",
                "cnName": "",
                "type": "NotificationLevel",
                "required": false,
                "description": "取值：0=Info；1=Warning；2=Critical"
              },
              {
                "name": "sorting",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "排序表达式，如 creationTime desc、name asc"
              },
              {
                "name": "skipCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "跳过条数（分页偏移），默认 0"
              },
              {
                "name": "maxResultCount",
                "cnName": "",
                "type": "integer",
                "required": false,
                "description": "每页条数，默认 10、最大 1000"
              }
            ],
            "responseFields": [
              {
                "name": "id",
                "cnName": "",
                "type": "Guid",
                "description": "唯一标识",
                "nullable": false
              },
              {
                "name": "channel",
                "cnName": "",
                "type": "NotificationChannelType",
                "description": "取值：1=Webhook；2=Email",
                "nullable": false
              },
              {
                "name": "title",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "toUser",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "level",
                "cnName": "",
                "type": "NotificationLevel",
                "description": "取值：0=Info；1=Warning；2=Critical",
                "nullable": false
              },
              {
                "name": "source",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "success",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "error",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              },
              {
                "name": "creationTime",
                "cnName": "",
                "type": "DateTime",
                "description": "创建时间",
                "nullable": false
              },
              {
                "name": "creatorId",
                "cnName": "",
                "type": "Guid",
                "description": "创建人 ID",
                "nullable": true
              }
            ]
          }
        ]
      },
      {
        "id": "AlertWebhook",
        "title": "Alertmanager 告警接入 /notification/webhooks/alertmanager",
        "endpoints": [
          {
            "method": "POST",
            "path": "/api/v1/notification/webhooks/alertmanager",
            "summary": "接收 Prometheus Alertmanager 告警 Webhook 回调并转发至平台通知中心。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "token",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              }
            ],
            "requestBody": [
              {
                "name": "status",
                "cnName": "",
                "type": "string",
                "description": "状态",
                "required": false
              },
              {
                "name": "alerts",
                "cnName": "",
                "type": "AlertmanagerAlertItemDto[]",
                "description": "",
                "required": false
              },
              {
                "name": "alerts[].status",
                "cnName": "",
                "type": "string",
                "description": "状态",
                "required": false
              },
              {
                "name": "alerts[].labels",
                "cnName": "",
                "type": "object",
                "description": "",
                "required": false
              },
              {
                "name": "alerts[].annotations",
                "cnName": "",
                "type": "object",
                "description": "",
                "required": false
              }
            ],
            "responseFields": [
              {
                "name": "accepted",
                "cnName": "",
                "type": "boolean",
                "description": "",
                "nullable": false
              },
              {
                "name": "receivedCount",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "failedCount",
                "cnName": "",
                "type": "integer",
                "description": "",
                "nullable": false
              },
              {
                "name": "message",
                "cnName": "",
                "type": "string",
                "description": "",
                "nullable": true
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "developer-docs",
    "title": "开发者文档",
    "description": "EF Core 模型与外部库实时反射表字典。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "DbSchema",
        "title": "数据库表字典 /db-schema/tables",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/db-schema/export/excel",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/db-schema/export/markdown",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/db-schema/external/{sourceKey}/tables",
            "summary": "tables",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "sourceKey",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              },
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（按名称/编码模糊搜索）"
              }
            ],
            "responseFields": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "表名（如 AppDataDictionaries）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "中文显示名。",
                "nullable": true
              },
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "description": "所属业务域。",
                "nullable": true
              },
              {
                "name": "columnCount",
                "cnName": "",
                "type": "integer",
                "description": "字段数。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "表说明。",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/db-schema/external/{sourceKey}/tables/{tableName}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "sourceKey",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              },
              {
                "name": "tableName",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "表名。",
                "nullable": true
              },
              {
                "name": "schema",
                "cnName": "",
                "type": "string",
                "description": "schema 名。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "中文显示名。",
                "nullable": true
              },
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "description": "所属业务域。",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "表说明。",
                "nullable": true
              },
              {
                "name": "columns",
                "cnName": "",
                "type": "DbColumnDto[]",
                "description": "字段列表。",
                "nullable": true
              },
              {
                "name": "columns[].name",
                "cnName": "",
                "type": "string",
                "description": "列名。",
                "nullable": true
              },
              {
                "name": "columns[].storeType",
                "cnName": "",
                "type": "string",
                "description": "数据库存储类型（如 character varying(64)）。",
                "nullable": true
              },
              {
                "name": "columns[].clrType",
                "cnName": "",
                "type": "string",
                "description": "CLR 类型短名。",
                "nullable": true
              },
              {
                "name": "columns[].maxLength",
                "cnName": "",
                "type": "integer",
                "description": "最大长度（字符串列）。",
                "nullable": true
              },
              {
                "name": "columns[].isNullable",
                "cnName": "",
                "type": "boolean",
                "description": "是否可空。",
                "nullable": false
              },
              {
                "name": "columns[].isPrimaryKey",
                "cnName": "",
                "type": "boolean",
                "description": "是否主键。",
                "nullable": false
              },
              {
                "name": "columns[].isForeignKey",
                "cnName": "",
                "type": "boolean",
                "description": "是否外键。",
                "nullable": false
              },
              {
                "name": "columns[].foreignTable",
                "cnName": "",
                "type": "string",
                "description": "外键关联表名。",
                "nullable": true
              },
              {
                "name": "columns[].isIndexed",
                "cnName": "",
                "type": "boolean",
                "description": "是否建有索引。",
                "nullable": false
              },
              {
                "name": "columns[].defaultValue",
                "cnName": "",
                "type": "string",
                "description": "默认值。",
                "nullable": true
              },
              {
                "name": "columns[].comment",
                "cnName": "",
                "type": "string",
                "description": "中文含义（取实体属性 XML 注释，空 = 待补）。",
                "nullable": true
              },
              {
                "name": "columns[].isSystemColumn",
                "cnName": "",
                "type": "boolean",
                "description": "是否系统/审计字段（ABP 基类字段，前端可折叠置底）。",
                "nullable": false
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/db-schema/external/sources",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "key",
                "cnName": "",
                "type": "string",
                "description": "连接串名（appsettings ConnectionStrings 的 key，如 OrderReadDb）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "中文显示名（如「订单中心从库」）。",
                "nullable": true
              },
              {
                "name": "dialect",
                "cnName": "",
                "type": "string",
                "description": "方言（SqlServer / PostgreSql），决定元数据查询语法。",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/db-schema/tables",
            "summary": "查询列表",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "filter",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": "关键字过滤（按名称/编码模糊搜索）"
              },
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "required": false,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "表名（如 AppDataDictionaries）。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "中文显示名。",
                "nullable": true
              },
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "description": "所属业务域。",
                "nullable": true
              },
              {
                "name": "columnCount",
                "cnName": "",
                "type": "integer",
                "description": "字段数。",
                "nullable": false
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "表说明。",
                "nullable": true
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/db-schema/tables/{tableName}",
            "summary": "查询详情",
            "summaryDocumented": false,
            "queryParams": [
              {
                "name": "tableName",
                "cnName": "",
                "type": "string",
                "required": true,
                "description": ""
              }
            ],
            "responseFields": [
              {
                "name": "name",
                "cnName": "",
                "type": "string",
                "description": "表名。",
                "nullable": true
              },
              {
                "name": "schema",
                "cnName": "",
                "type": "string",
                "description": "schema 名。",
                "nullable": true
              },
              {
                "name": "displayName",
                "cnName": "",
                "type": "string",
                "description": "中文显示名。",
                "nullable": true
              },
              {
                "name": "domainGroup",
                "cnName": "",
                "type": "string",
                "description": "所属业务域。",
                "nullable": true
              },
              {
                "name": "description",
                "cnName": "",
                "type": "string",
                "description": "表说明。",
                "nullable": true
              },
              {
                "name": "columns",
                "cnName": "",
                "type": "DbColumnDto[]",
                "description": "字段列表。",
                "nullable": true
              },
              {
                "name": "columns[].name",
                "cnName": "",
                "type": "string",
                "description": "列名。",
                "nullable": true
              },
              {
                "name": "columns[].storeType",
                "cnName": "",
                "type": "string",
                "description": "数据库存储类型（如 character varying(64)）。",
                "nullable": true
              },
              {
                "name": "columns[].clrType",
                "cnName": "",
                "type": "string",
                "description": "CLR 类型短名。",
                "nullable": true
              },
              {
                "name": "columns[].maxLength",
                "cnName": "",
                "type": "integer",
                "description": "最大长度（字符串列）。",
                "nullable": true
              },
              {
                "name": "columns[].isNullable",
                "cnName": "",
                "type": "boolean",
                "description": "是否可空。",
                "nullable": false
              },
              {
                "name": "columns[].isPrimaryKey",
                "cnName": "",
                "type": "boolean",
                "description": "是否主键。",
                "nullable": false
              },
              {
                "name": "columns[].isForeignKey",
                "cnName": "",
                "type": "boolean",
                "description": "是否外键。",
                "nullable": false
              },
              {
                "name": "columns[].foreignTable",
                "cnName": "",
                "type": "string",
                "description": "外键关联表名。",
                "nullable": true
              },
              {
                "name": "columns[].isIndexed",
                "cnName": "",
                "type": "boolean",
                "description": "是否建有索引。",
                "nullable": false
              },
              {
                "name": "columns[].defaultValue",
                "cnName": "",
                "type": "string",
                "description": "默认值。",
                "nullable": true
              },
              {
                "name": "columns[].comment",
                "cnName": "",
                "type": "string",
                "description": "中文含义（取实体属性 XML 注释，空 = 待补）。",
                "nullable": true
              },
              {
                "name": "columns[].isSystemColumn",
                "cnName": "",
                "type": "boolean",
                "description": "是否系统/审计字段（ABP 基类字段，前端可折叠置底）。",
                "nullable": false
              }
            ]
          }
        ]
      }
    ]
  },
  {
    "id": "layout",
    "title": "个人与界面偏好",
    "description": "导航排序、多页签、主题字号语言偏好、表格列视图与头像。",
    "baseUrl": "/api/v1",
    "sections": [
      {
        "id": "NavPreference",
        "title": "导航偏好 /nav-preference",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/nav-preference",
            "summary": "查询列表",
            "summaryDocumented": false,
            "responseFields": [
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "当前用户生效的导航顺序 JSON（User→Global→默认 解析后）。",
                "nullable": true
              },
              {
                "name": "isAdmin",
                "cnName": "",
                "type": "boolean",
                "description": "当前用户是否 admin（admin 的保存写入全局默认，其余写入个人）。",
                "nullable": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/nav-preference",
            "summary": "新建",
            "summaryDocumented": false,
            "requestBody": [
              {
                "name": "value",
                "cnName": "",
                "type": "string",
                "description": "导航顺序 JSON。",
                "required": false
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/nav-preference/reset",
            "summary": "新建",
            "summaryDocumented": false
          }
        ]
      },
      {
        "id": "UserPreference",
        "title": "界面偏好 /user-preference",
        "endpoints": [
          {
            "method": "GET",
            "path": "/api/v1/user-preference",
            "summary": "读取当前用户生效的界面偏好（主题 / 字号 / 语言）。",
            "summaryDocumented": true,
            "responseFields": [
              {
                "name": "theme",
                "cnName": "",
                "type": "string",
                "description": "主题：system（跟随系统）/ light（浅色）/ dark（深色）。",
                "nullable": true
              },
              {
                "name": "fontSize",
                "cnName": "",
                "type": "integer",
                "description": "界面字号（px，13~18）。",
                "nullable": false
              },
              {
                "name": "language",
                "cnName": "",
                "type": "string",
                "description": "界面语言：zh-Hans（简体）/ zh-Hant（繁体）/ en（英语）。",
                "nullable": true
              }
            ]
          },
          {
            "method": "POST",
            "path": "/api/v1/user-preference",
            "summary": "保存当前用户的界面偏好（仅更新入参中非 null 的字段，写 User 级）。",
            "summaryDocumented": true,
            "requestBody": [
              {
                "name": "theme",
                "cnName": "",
                "type": "string",
                "description": "主题：system / light / dark。",
                "required": false
              },
              {
                "name": "fontSize",
                "cnName": "",
                "type": "integer",
                "description": "界面字号（px，13~18，越界自动收敛）。",
                "required": false
              },
              {
                "name": "language",
                "cnName": "",
                "type": "string",
                "description": "界面语言：zh-Hans / zh-Hant / en。",
                "required": false
              }
            ]
          }
        ]
      },
      {
        "id": "Avatar",
        "title": "个人头像 /avatars",
        "endpoints": [
          {
            "method": "POST",
            "path": "/api/v1/avatars",
            "summary": "当前用户上传/替换头像（jpg/png/webp，≤2MB）。",
            "summaryDocumented": true
          },
          {
            "method": "GET",
            "path": "/api/v1/avatars/{userId}",
            "summary": "指定用户头像；无头像返回 404，前端 AvatarImage 自动回退首字母。",
            "summaryDocumented": true,
            "queryParams": [
              {
                "name": "userId",
                "cnName": "",
                "type": "Guid",
                "required": true,
                "description": "关联账号 ID"
              }
            ]
          },
          {
            "method": "GET",
            "path": "/api/v1/avatars/me",
            "summary": "当前用户头像（header / 个人中心用，免在前端拼 userId）。",
            "summaryDocumented": true
          }
        ]
      }
    ]
  }
]
