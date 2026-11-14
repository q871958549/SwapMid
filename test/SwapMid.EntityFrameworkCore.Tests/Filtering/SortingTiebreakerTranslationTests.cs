using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.EntityFrameworkCore.Filtering;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class SortingTiebreakerTranslationTests : SwapMidEntityFrameworkCoreTestBase
{
    private static readonly IReadOnlyDictionary<
        string,
        Func<IQueryable<DataPipeline>, bool, IOrderedQueryable<DataPipeline>>> Whitelist =
        new Dictionary<string, Func<IQueryable<DataPipeline>, bool, IOrderedQueryable<DataPipeline>>>
        {
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

    [Fact]
    public async Task Id兜底键应被EF翻译进ORDER_BY且方向跟随主序()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var query = await GetRequiredService<IRepository<DataPipeline, Guid>>().GetQueryableAsync();

            var descendingSql = query
                .ApplyWhitelistedSorting("creationtime desc", Whitelist, "creationtime")
                .ToQueryString();
            var ascendingSql = query
                .ApplyWhitelistedSorting("creationtime asc", Whitelist, "creationtime")
                .ToQueryString();

            descendingSql.ShouldContain("ORDER BY");
            descendingSql.ShouldContain("DESC");
            var ascendingOrderBy = ascendingSql[ascendingSql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
            ascendingOrderBy.ShouldNotContain("DESC");
        });
    }

    [Fact]
    public async Task 兜底键不应重复出现在ORDER_BY里()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var query = await GetRequiredService<IRepository<DataPipeline, Guid>>().GetQueryableAsync();

            var byId = new Dictionary<string, Func<IQueryable<DataPipeline>, bool, IOrderedQueryable<DataPipeline>>>
            {
                ["id"] = (q, d) => d ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
            };
            var sql = query.ApplyWhitelistedSorting("id asc", byId, "id").ToQueryString();

            var orderByClause = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
            CountOccurrences(orderByClause, "\"Id\"").ShouldBe(1);
        });
    }

    [Fact]
    public async Task 关闭兜底时ORDER_BY不应含Id()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var query = await GetRequiredService<IRepository<DataPipeline, Guid>>().GetQueryableAsync();

            var sql = query
                .ApplyWhitelistedSorting("creationtime desc", Whitelist, "creationtime",
                    appendIdTiebreaker: false)
                .ToQueryString();

            var orderByClause = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
            orderByClause.ShouldNotContain("\"Id\"");
        });
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}
