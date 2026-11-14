using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SwapMid.SharedKernel;

/// <summary>
/// 批量写入契约的资源边界（2026-08-04）。
///
/// 契约层先拒的意义：没有它，一个手工构造的百万 id 请求会一路走到逐条 <c>FindAsync + DeleteAsync</c>，
/// 在数据库上跑成两百万次往返——请求本身就是 DoS。上限让这类 payload 在模型绑定阶段就被 400 挡下。
/// </summary>
public static class BatchOperationLimits
{
    /// <summary>
    /// 单次批量操作的 Id 数上限。
    ///
    /// **为什么是 2000 而不是更小**：前端 DataTable 的跨页行选择是既有能力，而每页档位用户可自定义、
    /// 服务端分页钳到 <c>SERVER_MAX_PAGE_SIZE = 5000</c>——把每页调到 5000 再点一次全选就是 5000 个 id，
    /// 一次完全正常的 UI 操作。所以**任何低于 5000 的裸上限都会让现有界面一键触发 400**。
    /// 配套做法是前端分片（<c>lib/crud/batch-chunk.ts</c> 的 <c>submitInChunks</c>）：超过一片就分段提交，
    /// 上限因此只需覆盖「单片」而非「用户一次能选多少」。2000 与样品域的批量软上限同值，语义一致。
    ///
    /// **为什么不是更大**：2000 个 id 逐条走完整删除（含权限与关联校验）已是数千次数据库往返、
    /// 秒级到十秒级的单请求；再大就该走后台作业 + 进度查询，而不是让人对着转圈等。
    ///
    /// ⚠️ 改这个值要同时改前端 <c>BATCH_CHUNK_SIZE</c>——**前端分片必须 ≤ 后端上限**，
    /// 大了每片都被整批拒绝，小了只是多跑几趟请求（无害）。两处注释互指。
    /// </summary>
    public const int MaxIdsPerRequest = 2000;
}

/// <summary>批量操作的通用入参：一组实体 Id。</summary>
public class BatchIdsInput
{
    /// <summary>
    /// 实体 Id 列表。上限见 <see cref="BatchOperationLimits.MaxIdsPerRequest"/>——**超限整批拒绝**
    /// （400），不截断：静默只处理前 N 个会让用户以为全做完了，是比报错糟得多的失败形态。
    /// </summary>
    [Required]
    [MaxLength(BatchOperationLimits.MaxIdsPerRequest)]
    public List<Guid> Ids { get; set; } = new();
}

/// <summary>批量操作结果摘要。</summary>
public class BatchOperationResultDto
{
    public int Requested { get; set; }
    public int Succeeded { get; set; }
    public int Skipped { get; set; }
    public List<string> Messages { get; set; } = new();
}
