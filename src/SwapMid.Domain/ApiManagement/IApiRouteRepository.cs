using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.ApiManagement;

public interface IApiRouteRepository : IRepository<ApiRoute, Guid>
{
    Task<bool> PathAndMethodExistsAsync(string path, HttpMethodType httpMethod, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>网关转发:找匹配 path + method（method=ALL 通配）的已启用路由;多条按 Order、具体方法优先于 ALL。path 忽略大小写。</summary>
    Task<ApiRoute?> FindEnabledMatchAsync(string path, HttpMethodType httpMethod, CancellationToken cancellationToken = default);

    Task<List<ApiRoute>> GetListAsync(
        string? filter = null,
        Guid? groupId = null,
        bool? isEnabled = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        string? sorting = null,
        int maxResultCount = 10,
        int skipCount = 0,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(
        string? filter = null,
        Guid? groupId = null,
        bool? isEnabled = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        CancellationToken cancellationToken = default);
}
