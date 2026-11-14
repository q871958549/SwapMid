using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.SystemManagement;

/// <summary>
/// <see cref="CodeRule"/> 自定义仓储：在通用 <see cref="IRepository{CodeRule,Guid}"/> 之上扩展原子取号方法
/// （R1，蓝图 docs/project-management-blueprint.md §20.3）。EFCore 层用单条参数化原生 SQL
/// <c>UPDATE ... RETURNING</c> 实现「按周期归零判断 + 序号自增」，兼容 PostgreSQL / SQLite 双 provider
/// （本仓测试用 SQLite）。取代原 <c>CodeRuleManager.GenerateAsync</c> 的「查行 → 内存改 → UpdateAsync」
/// 无锁读改写，根治全量同步等高并发场景下的行锁长阻塞与并发戳异常噪声。
/// </summary>
public interface ICodeRuleRepository : IRepository<CodeRule, Guid>
{
    /// <summary>
    /// 原子推进指定实体类型编码规则的序号：单条 UPDATE 在数据库行锁上完成「按重置周期判断是否归零 +
    /// 序号自增 + 取最新配置列」，并发者天然排队、先到先得、零业务异常。规则不存在或未启用时不更新任何行，
    /// 返回 null（调用方据此抛 <c>CodeRuleNotFound</c>）。
    /// </summary>
    /// <param name="entityType">编码规则的实体类型标识（如 "Sample"）。</param>
    /// <param name="now">取号时刻（调用方传 <c>Clock.Now</c>，本地时钟口径，R3）——归零键（yyyyMMdd/yyyyMM/yyyy）
    /// 由实现按此计算；具体挑选哪个候选键是 SQL 内部按行上 <see cref="CodeRule.ResetCycle"/> 决定的实现细节，
    /// 不需要调用方预先查询规则本身（否则会退化为「先读后写」，破坏原子性）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CodeRuleSequenceRow?> AdvanceSequenceAsync(string entityType, DateTime now, CancellationToken cancellationToken = default);
}

/// <summary>
/// 原子取号返回的投影列：拼码所需的规则配置 + 推进后的最新序号。不含整行实体——取号本身有意绕过 EF
/// 变更跟踪（不刷新 LastModificationTime/ConcurrencyStamp，高频取号不该污染这些审计字段，且管理端编辑
/// 规则的乐观锁不再被取号干扰，见蓝图 R1 决议「语义微变」）。
/// </summary>
public sealed record CodeRuleSequenceRow(
    string Prefix,
    string? Suffix,
    bool IncludeDate,
    string? DateFormat,
    int SequencePad,
    long CurrentSequence);
