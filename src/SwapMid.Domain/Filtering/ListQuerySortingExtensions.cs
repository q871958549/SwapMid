using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace SwapMid;

public static class ListQuerySortingExtensions
{
    /// <summary>
    /// 按实体类型一次性构建 <c>e =&gt; e.Id</c> 的排序 lambda（无 <c>Id</c> 属性则为 null）。
    ///
    /// 用**具体类型的 PropertyInfo** 而非接口成员：生成的 MemberExpression 与手写 <c>e =&gt; e.Id</c>
    /// 完全一致，EF 翻译零风险；也不需要 <c>EF.Property</c>（Domain 层不引 EF Core，分层规范禁止）。
    /// 泛型静态字段天然是 per-type 缓存，每个 TEntity 只反射一次。
    /// </summary>
    private static class IdTiebreaker<TEntity>
    {
        internal static readonly LambdaExpression? Lambda = Build();

        private static LambdaExpression? Build()
        {
            var property = typeof(TEntity).GetProperty(
                "Id", BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead)
            {
                return null;
            }
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            return Expression.Lambda(Expression.Property(parameter, property), parameter);
        }
    }

    /// <summary>
    /// 给已排序查询追加 <c>Id</c> 末级兜底键。类型没有 <c>Id</c> 属性时原样返回（join 投影行等）。
    /// </summary>
    private static IOrderedQueryable<TEntity> AppendIdTiebreaker<TEntity>(
        IOrderedQueryable<TEntity> ordered,
        bool descending)
    {
        var lambda = IdTiebreaker<TEntity>.Lambda;
        if (lambda is null)
        {
            return ordered;
        }
        // 方向跟随主序：升序配升序、降序配降序，这样 (主列, Id) 复合索引正反两向都能被索引扫描服务，
        // 不会因为方向混排而退化成 Sort（样品域 IX_AppSamples_RegisteredAt_Id 即按此设计）。
        var method = descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);
        var call = Expression.Call(
            typeof(Queryable),
            method,
            [typeof(TEntity), lambda.ReturnType],
            ordered.Expression,
            Expression.Quote(lambda));
        return (IOrderedQueryable<TEntity>)ordered.Provider.CreateQuery<TEntity>(call);
    }
    /// <summary>
    /// 白名单服务端排序：解析 ABP <c>Sorting</c> 串首段（<c>"字段 asc|desc"</c>），按字段映射表应用排序。
    /// 字段（前端列 id）大小写不敏感匹配，未命中一律回退 <paramref name="defaultKey"/>——
    /// 杜绝把用户串裸传给动态排序（排序注入）。关联名列（需 join）不放进字典、由前端禁排序即可。
    /// </summary>
    /// <param name="query">待排序的源查询。</param>
    /// <param name="sorting">ABP <c>Sorting</c> 串（如 <c>"name desc"</c>）；为空则回退 <paramref name="defaultKey"/>（按 <paramref name="defaultDescending"/> 决定方向）。</param>
    /// <param name="whitelist">列 id（小写）→ 排序委托 <c>(query, descending) => OrderBy/OrderByDescending</c>。</param>
    /// <param name="defaultKey">未命中时回退的字段键（须存在于 <paramref name="whitelist"/>）。</param>
    /// <param name="defaultDescending">
    /// <c>Sorting</c> 为空时的排序方向，默认 <c>false</c>（升序）。⚠️ 把原本硬编码 <c>OrderByDescending</c>
    /// 的字段（如「创建时间降序=最新在前」，列表页最常见的默认排序）迁到本方法时，**必须显式传 <c>true</c>**，
    /// 否则默认视图会从「最新在前」静默变成「最旧在前」——这是一个真实踩过的回归（讲师档案页曾经历此坑）。
    /// </param>
    /// <param name="logger">
    /// 可选日志器（AppService 直接传自带的 <c>Logger</c>）。前端传来白名单外的字段时记一条 Warning——
    /// 回退本身是刻意设计（防注入、旧书签不炸页），但「前端列头能点、后端不认」是**前后端契约缺口**，
    /// 静默吞掉会让「点了排序没反应」长期无人发现（员工页创建时间列即如此）。有日志才可运维定位。
    /// </param>
    /// <param name="appendIdTiebreaker">
    /// 是否在排序链末尾自动追加 <c>Id</c> 兜底键（默认 <c>true</c>）。
    ///
    /// 🔴 **默认开启修的是一个正确性 bug，不是优化**：SQL 对并列行的相对顺序**不作任何保证**，
    /// 而本平台的排序键大面积同值（批量导入/同步一次写入数千行，时间戳几乎全等；状态/类型等枚举
    /// 只有个位数取值）。缺兜底键时 OFFSET 分页会在页与页之间**重复某些行、同时丢掉另一些行**——
    /// 这与数据量无关，十几万行就能复现，且用户只会以为「数据有问题」。
    ///
    /// **什么时候要显式传 false**：调用方在本方法**返回值上**继续接 <c>.ThenBy(...)</c> 时。
    /// 那种写法下本方法追加的 Id 会排在调用方兜底键**之前**，把后者整个短路。典型是
    /// <c>K3InventoryAppService.ApplyStableTieBreakers</c>——它刻意用**跨同步稳定的业务键**
    /// （物料号/仓库/库位/状态）兜底，因为该表每次同步都重建、实体 Id 每次都变；被 Id 抢在前面
    /// 就等于每次同步后翻页顺序全部洗牌，比没有兜底更糟。
    ///
    /// 相对地，白名单**委托内部**已经写了 <c>.ThenBy(...)</c> 的（多列业务排序）无需 opt-out：
    /// 追加发生在整条链尾部，不影响委托内语义。
    /// </param>
    public static IOrderedQueryable<TEntity> ApplyWhitelistedSorting<TEntity>(
        this IQueryable<TEntity> query,
        string? sorting,
        IReadOnlyDictionary<string, Func<IQueryable<TEntity>, bool, IOrderedQueryable<TEntity>>> whitelist,
        string defaultKey,
        bool defaultDescending = false,
        ILogger? logger = null,
        bool appendIdTiebreaker = true)
    {
        var key = defaultKey.ToLowerInvariant();
        var descending = defaultDescending;
        var requested = false;
        if (!string.IsNullOrWhiteSpace(sorting))
        {
            var segments = sorting.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 0)
            {
                var first = segments[0].Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (first.Length > 0)
                {
                    key = first[0].ToLowerInvariant();
                    requested = true;
                    descending = first.Length > 1 &&
                        first[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        if (whitelist.TryGetValue(key, out var fn))
        {
            return Finish(fn(query, descending), key);
        }

        if (requested)
        {
            logger?.LogWarning(
                "排序字段 {SortKey} 不在 {Entity} 的排序白名单内，已回退 {DefaultKey}。"
                + "前端该列若可点排序，需补白名单键或给列加 enableSorting: false。",
                key, typeof(TEntity).Name, defaultKey);
        }

        var fallbackKey = defaultKey.ToLowerInvariant();
        return Finish(whitelist[fallbackKey](query, descending), fallbackKey);

        IOrderedQueryable<TEntity> Finish(IOrderedQueryable<TEntity> ordered, string appliedKey)
            // 主序就是 Id 时不重复追加（少数白名单确实有 id 键）。
            => appendIdTiebreaker && appliedKey != "id"
                ? AppendIdTiebreaker(ordered, descending)
                : ordered;
    }

    /// <summary>
    /// 白名单排序的**字符串版**：给不走 <see cref="IQueryable{T}"/> 的场景用（把排序串转交第三方 API，
    /// 典型是 ABP <c>IAuditLogRepository.GetListAsync(sorting:)</c> 的动态 LINQ 排序）。
    /// 解析前端 <c>Sorting</c> 串首段，只有命中 <paramref name="whitelist"/> 的列 id 才被翻译成目标字段名，
    /// 其余一律回退 <paramref name="defaultSorting"/>。
    /// </summary>
    /// <remarks>
    /// ⚠️ <paramref name="whitelist"/> 必须是硬编码字面量字典——本方法是「用户输入影响排序表达式」的收口点，
    /// 把用户串直接当字段名转交下游 = 排序注入（非法字段名会让动态 LINQ 抛异常返 500，字段名也可能被用于探测模型）。
    /// 裸 SQL 场景另有 <c>ScanQueryAppServiceBase.BuildOrderByClause</c>（同构，输出 SQL <c>ORDER BY</c> 片段）。
    /// </remarks>
    /// <param name="sorting">前端传入的 ABP <c>Sorting</c> 串（如 <c>"executionTime desc"</c>）。</param>
    /// <param name="whitelist">列 id（小写）→ 下游可安全接收的字段名。</param>
    /// <param name="defaultSorting">为空 / 未命中时的回退串，可为复合表达式（如 <c>"ExecutionTime DESC"</c>）。</param>
    /// <param name="logger">可选日志器：未命中时记 Warning，理由同上面的重载。</param>
    /// <param name="idFieldName">
    /// 末级兜底字段名（默认 <c>"Id"</c>），会被追加到输出串尾部。传 <c>null</c> 关闭追加。
    /// 理由同 <see cref="ApplyWhitelistedSorting"/> 的 <c>appendIdTiebreaker</c>——本重载的现实受害者
    /// 是审计日志：<c>ExecutionTime</c> 在高并发下同值极其密集，缺兜底键翻页必然重复/丢行。
    /// 下游是动态 LINQ（ABP 仓储），支持多列排序串。
    /// </param>
    public static string ResolveWhitelistedSorting(
        string? sorting,
        IReadOnlyDictionary<string, string> whitelist,
        string defaultSorting,
        ILogger? logger = null,
        string? idFieldName = "Id")
    {
        if (string.IsNullOrWhiteSpace(sorting)) return WithTiebreaker(defaultSorting, descending: null);

        // 逗号/空格全是分隔符本身时 RemoveEmptyEntries 后数组可能为空，[0] 会越界——先判空再取。
        var outer = sorting.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (outer.Length == 0) return WithTiebreaker(defaultSorting, descending: null);

        var first = outer[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (first.Length == 0) return WithTiebreaker(defaultSorting, descending: null);

        var key = first[0].ToLowerInvariant();
        if (!whitelist.TryGetValue(key, out var field))
        {
            logger?.LogWarning(
                "排序字段 {SortKey} 不在排序白名单内，已回退 {DefaultSorting}。"
                + "前端该列若可点排序，需补白名单键或给列加 enableSorting: false。",
                key, defaultSorting);
            return WithTiebreaker(defaultSorting, descending: null);
        }

        var descending = first.Length > 1 && first[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return WithTiebreaker($"{field} {(descending ? "DESC" : "ASC")}", descending);

        string WithTiebreaker(string expression, bool? descending)
        {
            if (string.IsNullOrWhiteSpace(idFieldName)
                // 回退串本身已经以兜底字段收尾（或就是按它排）时不重复追加。
                || expression.Contains(idFieldName!, StringComparison.OrdinalIgnoreCase))
            {
                return expression;
            }
            // 回退串可能是复合表达式且方向各异，此时以 DESC 追加（与最常见的「最新在前」默认序同向）。
            return $"{expression}, {idFieldName} {(descending ?? true ? "DESC" : "ASC")}";
        }
    }
}
