using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.MessageBus;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.MessageBus;

public class EfCoreMessageRecordRepository :
    EfCoreRepository<SwapMidDbContext, MessageRecord, Guid>,
    IMessageRecordRepository
{
    public EfCoreMessageRecordRepository(IDbContextProvider<SwapMidDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public async Task<List<MessageRecord>> GetListAsync(
        Guid? topicId = null, MessageStatus? status = null, string? filter = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        string? sorting = null, int maxResultCount = 20, int skipCount = 0, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        var query = BuildQuery(dbSet, topicId, status, filter)
            .ApplyTimeRange(startTime, endTime, timeType);
        return await query.ApplyWhitelistedSorting(sorting, SortWhitelist, "creationtime", defaultDescending: true)
            .Skip(skipCount).Take(maxResultCount).ToListAsync(cancellationToken);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）：未命中回退 creationtime 降序（创建时间最新在前，
    // 列表默认视图）。⚠️ defaultDescending 必须显式传 true，否则默认视图会从「最新在前」静默变
    // 「最旧在前」。payload 是发布时落库的大段 JSON 文本快照，不适合做排序键，不进白名单——前端对应
    // 列已加 enableSorting: false（同 training-exam-questions 页 stem 长文本列的处理）。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<MessageRecord>, bool, IOrderedQueryable<MessageRecord>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<MessageRecord>, bool, IOrderedQueryable<MessageRecord>>>
        {
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["topicname"] = (q, d) => d ? q.OrderByDescending(x => x.TopicName) : q.OrderBy(x => x.TopicName),
            ["subject"] = (q, d) => d ? q.OrderByDescending(x => x.Subject) : q.OrderBy(x => x.Subject),
            ["retrycount"] = (q, d) => d ? q.OrderByDescending(x => x.RetryCount) : q.OrderBy(x => x.RetryCount),
            ["status"] = (q, d) => d ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            ["deliveredat"] = (q, d) => d ? q.OrderByDescending(x => x.DeliveredAt) : q.OrderBy(x => x.DeliveredAt),
            ["errormessage"] = (q, d) => d ? q.OrderByDescending(x => x.ErrorMessage) : q.OrderBy(x => x.ErrorMessage),
        };

    public async Task<int> GetCountAsync(
        Guid? topicId = null, MessageStatus? status = null, string? filter = null,
        DateTime? startTime = null, DateTime? endTime = null,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await BuildQuery(dbSet, topicId, status, filter)
            .ApplyTimeRange(startTime, endTime, timeType)
            .CountAsync(cancellationToken);
    }

    private static IQueryable<MessageRecord> BuildQuery(IQueryable<MessageRecord> q, Guid? topicId, MessageStatus? status, string? filter)
    {
        if (topicId.HasValue) q = q.Where(x => x.TopicId == topicId.Value);
        if (status.HasValue) q = q.Where(x => x.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(filter)) q = q.Where(x => x.Subject.Contains(filter));
        return q;
    }
}
