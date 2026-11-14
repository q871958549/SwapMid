using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.SharedKernel;

/// <summary>
/// 批量操作的**集合化**执行器（2026-08-06）。
///
/// ## 它取代的写法
///
/// <code>
/// foreach (var id in input.Ids.Distinct()) {
///     var e = await Repository.FindAsync(id);          // ← 每条一次往返
///     if (e == null) { result.Skipped++; continue; }
///     await Repository.DeleteAsync(e, autoSave: true); // ← 每条一次 SaveChanges
///     result.Succeeded++;
/// }
/// </code>
///
/// 单请求上限是 2000（<see cref="BatchOperationLimits.MaxIdsPerRequest"/>），所以上面这段在满载时是
/// **2000 次 Find + 2000 次 SaveChanges = 4000 次数据库往返**。局域网单次往返按 0.5ms 算就是 2 秒纯网络，
/// 还没算每次 SaveChanges 自身的开销。
///
/// 🔴 **批量改状态比批量删更糟，且糟在一个不显眼的地方**：`UpdateAsync` 后实体在 EF ChangeTracker 里
/// 从 Modified 变 Unchanged **留着不走**，而每次 `SaveChanges` 都要 `DetectChanges` 扫描全部被跟踪实体
/// ——第 k 次扫 k 个，总计 **O(N²)**，2000 条约 200 万次实体扫描。（批量删没这个问题：Deleted 实体
/// 保存后会被 detach。）这就是为什么「明明只有两千条却慢得离谱」，加索引一点用都没有。
///
/// ## 语义不变量（改这个文件前先读）
///
/// - **原子性不变**。ABP 对 POST 默认开事务性 UoW（`app.UseUnitOfWork()` + `TransactionBehavior.Auto`），
///   `autoSave: true` 只是 flush 不是 commit——所以逐条循环**本来就是整批原子**的，异常即全批回滚。
///   集合化不改变这一点，不要以为这里从「部分成功」变成了「全有全无」。
/// - **软删除 / 审计 / 领域事件不变**。`DeleteManyAsync` 内部走 `RemoveRange` + ABP 的 SaveChanges 拦截，
///   `ISoftDelete` 实体照样转成 `IsDeleted=true`。⚠️ 这正是本执行器与 `ExecuteDelete` 的分界——
///   后者绕过拦截器**真删行**，本仓已明确否决，不要「顺手优化」成它。
/// - **`Requested` 保留既有口径**：取 `input.Ids.Count`（含重复），而 `Succeeded`/`Skipped` 按去重后统计。
///   传了重复 id 时三者对不上，这是**沿用**原有行为，不在本次改动里一并变更。
///
/// ## 什么时候**不要**用它
///
/// 逐条 try/catch 容错的批量操作（`EmployeeBatchOperationService.BatchGenerateAccountsAsync`、
/// `AccountUserAppService`）**不适用**：它们逐条独立是设计意图——每条都带
/// 不可回滚的外部副作用（建账号、发邮件、推送外部回调），一条失败不能拖垮整批，失败原因还要逐条回给用户。
/// 那类请走各自的逐条隔离循环。
/// </summary>
public static class BatchOperationExecutor
{
    /// <summary>
    /// 一次取回 <paramref name="requestedIds"/> 对应的实体，并算出「请求了但库里没有」的条数。
    ///
    /// 供需要在删除前**额外做业务判断**的调用方使用（关联检查、级联删子表、状态变更等）——
    /// 它们的判断逻辑各不相同，不该硬套模板，但「一次取回 + 差集计数」这段骨架必须共享：
    /// 差集算错就是 `Skipped` 报错数，而这类计数错误在测试里极难被发现。
    /// </summary>
    public static async Task<BatchEntityLoad<TEntity>> LoadAsync<TEntity>(
        IRepository<TEntity, Guid> repository,
        IReadOnlyCollection<Guid> requestedIds)
        where TEntity : class, IEntity<Guid>
    {
        var distinctIds = requestedIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new BatchEntityLoad<TEntity>(new List<TEntity>(), 0);
        }

        var found = await repository.GetListAsync(x => distinctIds.Contains(x.Id));
        return new BatchEntityLoad<TEntity>(found, distinctIds.Count - found.Count);
    }

    /// <summary>
    /// 批量删除的完整实现：一次取回 →（可选）一次前置清理 → 一次 `DeleteManyAsync` → 按差集填 `Skipped`。
    /// 适用于「不存在就跳过、存在就删」的批删端点（本仓多数批删属于此类）。
    /// 删除前需要**逐条**校验、或校验结果会影响该条删不删的，用 <see cref="LoadAsync"/> 自行组装。
    /// </summary>
    /// <param name="beforeDelete">
    /// 删除主表前的批量前置动作，入参是**实际查到的**实体 id（不含请求了但不存在的）。
    /// 用于级联清理子表：写成 <c>ids => _children.DeleteDirectAsync(x => ids.Contains(x.ParentId))</c>
    /// 即一次删光，而不是每个父 id 发一次删除。
    /// ⚠️ 只在 <c>Found</c> 非空时调用——空集合传给 <c>Contains</c> 会生成恒假条件，白跑一次往返。
    /// </param>
    public static async Task<BatchOperationResultDto> DeleteAsync<TEntity>(
        IRepository<TEntity, Guid> repository,
        IReadOnlyCollection<Guid> requestedIds,
        Func<List<Guid>, Task>? beforeDelete = null)
        where TEntity : class, IEntity<Guid>
    {
        var load = await LoadAsync(repository, requestedIds);
        if (load.Found.Count > 0)
        {
            if (beforeDelete != null)
            {
                await beforeDelete(load.Found.Select(x => x.Id).ToList());
            }

            await repository.DeleteManyAsync(load.Found, autoSave: true);
        }

        return new BatchOperationResultDto
        {
            Requested = requestedIds.Count,
            Succeeded = load.Found.Count,
            Skipped = load.MissingCount,
        };
    }

    /// <summary>
    /// 带「被引用就跳过」判断的批量删除：一次取回 → 一次查出**不能删**的 → 删剩下的。
    ///
    /// 适用于删除前要检查关联的批删（宿舍有房间则不删、房间有在住人员则不删、培训资源被课时引用则不删）。
    /// 这类原本是**双重 N+1**——每条既 Find 一次又 Any 一次，2000 条就是 6000 次往返。
    /// </summary>
    /// <param name="findBlocked">
    /// 给定实际存在的实体 id，返回其中**不该删**的那些。
    /// 必须一次查完（<c>ids => ...Where(x => ids.Contains(x.ParentId))...</c>）；
    /// ⚠️ 不要在里面对 ids 再 foreach 逐个查——那只是把 N+1 挪了个位置，而且从调用点完全看不出来。
    /// </param>
    /// <param name="beforeDelete">同 <see cref="DeleteAsync"/>，入参是**最终真要删**的 id（已排除 blocked）。</param>
    public static async Task<BatchOperationResultDto> DeleteExceptAsync<TEntity>(
        IRepository<TEntity, Guid> repository,
        IReadOnlyCollection<Guid> requestedIds,
        Func<List<Guid>, Task<HashSet<Guid>>> findBlocked,
        Func<List<Guid>, Task>? beforeDelete = null)
        where TEntity : class, IEntity<Guid>
    {
        var load = await LoadAsync(repository, requestedIds);
        var deletable = load.Found;
        if (load.Found.Count > 0)
        {
            var blocked = await findBlocked(load.Found.Select(x => x.Id).ToList());
            if (blocked.Count > 0)
            {
                deletable = load.Found.Where(x => !blocked.Contains(x.Id)).ToList();
            }

            if (deletable.Count > 0)
            {
                if (beforeDelete != null)
                {
                    await beforeDelete(deletable.Select(x => x.Id).ToList());
                }

                await repository.DeleteManyAsync(deletable, autoSave: true);
            }
        }

        return new BatchOperationResultDto
        {
            Requested = requestedIds.Count,
            // 跳过 = 压根不存在的 + 存在但被引用的。两项都要算，漏掉后者会让 Succeeded+Skipped 对不上 Requested。
            Skipped = load.MissingCount + (load.Found.Count - deletable.Count),
            Succeeded = deletable.Count,
        };
    }

    /// <summary>
    /// 批量状态变更：一次取回 → 对每个实体应用 <paramref name="mutate"/> → 一次 `UpdateManyAsync`。
    ///
    /// <paramref name="mutate"/> 必须是**纯内存**的领域方法调用（`Enable()` / `SetStatus(...)` 之类），
    /// 不要在里面发查询或做 I/O——那会把刚消掉的 N+1 原样长回来，而且从签名上看不出来。
    /// </summary>
    public static async Task<BatchOperationResultDto> UpdateAsync<TEntity>(
        IRepository<TEntity, Guid> repository,
        IReadOnlyCollection<Guid> requestedIds,
        Action<TEntity> mutate)
        where TEntity : class, IEntity<Guid>
    {
        var load = await LoadAsync(repository, requestedIds);
        if (load.Found.Count > 0)
        {
            foreach (var entity in load.Found)
            {
                mutate(entity);
            }

            await repository.UpdateManyAsync(load.Found, autoSave: true);
        }

        return new BatchOperationResultDto
        {
            Requested = requestedIds.Count,
            Succeeded = load.Found.Count,
            Skipped = load.MissingCount,
        };
    }
}

/// <summary>
/// <see cref="BatchOperationExecutor.LoadAsync"/> 的结果：查到的实体 + 请求了但不存在的条数。
/// </summary>
/// <param name="Found">库里真实存在的实体（顺序由数据库决定，调用方不应依赖）。</param>
/// <param name="MissingCount">去重后请求的 id 中查不到实体的条数，直接作为 `Skipped` 的起始值。</param>
public readonly record struct BatchEntityLoad<TEntity>(List<TEntity> Found, int MissingCount)
    where TEntity : class, IEntity<Guid>;
