using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwapMid.DeveloperDocs;

/// <summary>
/// 数据库表字典的元数据源：反射 EF 模型 + 实体 XML 注释，得到平台业务表的结构与中文含义。
/// 实现在 EntityFrameworkCore 层（需访问 <c>DbContext.Model</c>），结果进程内缓存。
/// </summary>
public interface ISchemaMetadataProvider
{
    /// <summary>
    /// 平台业务表元数据（已按域 / 表名排序，进程内缓存）。
    /// 合并全部业务 DbContext 的 IModel（宿主 + Platform/Hr/Project/Query/Admin/Quality），
    /// 仅 <c>SwapMid.*</c> 实体，跳过 ABP 框架表。
    /// </summary>
    Task<IReadOnlyList<SchemaTableInfo>> GetTablesAsync();
}
