using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.SystemManagement;

public class CodeRule : FullAuditedAggregateRoot<Guid>
{
    /// <summary>Entity type identifier, e.g. "Department", "Employee".</summary>
    public string EntityType { get; private set; } = null!;

    /// <summary>Human-readable label for this rule.</summary>
    public string DisplayName { get; private set; } = null!;

    /// <summary>Static prefix prepended to every generated code (e.g. "DEPT-").</summary>
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>Static suffix appended after the sequence (usually empty).</summary>
    public string? Suffix { get; private set; }

    /// <summary>Whether to embed a date segment in the code.</summary>
    public bool IncludeDate { get; private set; }

    /// <summary>Date format for the embedded date segment (e.g. "yyyyMMdd").</summary>
    public string? DateFormat { get; private set; }

    /// <summary>Minimum total length of the numeric sequence portion (zero-padded).</summary>
    public int SequencePad { get; private set; }

    /// <summary>When the sequence counter resets (Never / Daily / Monthly / Yearly).</summary>
    public SequenceResetCycle ResetCycle { get; private set; }

    /// <summary>Current sequence counter value.</summary>
    public long CurrentSequence { get; private set; }

    /// <summary>ISO date string of the last reset (used to detect when to auto-reset).</summary>
    public string? LastResetDate { get; private set; }

    /// <summary>Whether this code rule is enabled.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Optional remark / description of the rule.</summary>
    public string? Description { get; private set; }

    private CodeRule() { }

    public CodeRule(
        Guid id,
        string entityType,
        string displayName,
        string prefix = "",
        string? suffix = null,
        bool includeDate = false,
        string? dateFormat = "yyyyMMdd",
        int sequencePad = 4,
        SequenceResetCycle resetCycle = SequenceResetCycle.Never,
        string? description = null)
        : base(id)
    {
        EntityType = Check.NotNullOrWhiteSpace(entityType, nameof(entityType), CodeRuleConsts.MaxEntityTypeLength);
        DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName), CodeRuleConsts.MaxEntityTypeLength);
        Prefix = prefix ?? string.Empty;
        Suffix = suffix;
        IncludeDate = includeDate;
        DateFormat = dateFormat;
        SequencePad = Math.Clamp(sequencePad, 1, 20);
        ResetCycle = resetCycle;
        CurrentSequence = 0;
        Description = description;
        IsEnabled = true;
    }

    public void Update(
        string displayName,
        string prefix,
        string? suffix,
        bool includeDate,
        string? dateFormat,
        int sequencePad,
        SequenceResetCycle resetCycle,
        string? description)
    {
        DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName), CodeRuleConsts.MaxEntityTypeLength);
        Prefix = prefix ?? string.Empty;
        Suffix = suffix;
        IncludeDate = includeDate;
        DateFormat = includeDate ? dateFormat : null;
        SequencePad = Math.Clamp(sequencePad, 1, 20);
        ResetCycle = resetCycle;
        Description = description;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    /// <summary>Increment and return the next sequence number, resetting if needed.</summary>
    public long NextSequence(DateTime utcNow)
    {
        var resetKey = ResetCycle switch
        {
            SequenceResetCycle.Daily => utcNow.ToString("yyyyMMdd"),
            SequenceResetCycle.Monthly => utcNow.ToString("yyyyMM"),
            SequenceResetCycle.Yearly => utcNow.ToString("yyyy"),
            _ => null,
        };

        if (resetKey != null && resetKey != LastResetDate)
        {
            CurrentSequence = 0;
            LastResetDate = resetKey;
        }

        CurrentSequence += 1;
        return CurrentSequence;
    }

    /// <summary>Build a sample code using the given sequence number. 无入参时兜底 <see cref="DateTime.UtcNow"/>。</summary>
    public string BuildCode(long sequence, DateTime? date = null)
        => BuildCode(Prefix, Suffix, IncludeDate, DateFormat, SequencePad, sequence, date ?? DateTime.UtcNow);

    /// <summary>
    /// 纯函数拼码（R1，蓝图 §20.3）：按给定配置拼装编码，不依赖实体实例。供 <c>CodeRuleManager</c>
    /// 用原子取号（<see cref="ICodeRuleRepository.AdvanceSequenceAsync"/>）返回的投影列
    /// 直接拼码——取号已绕过 EF 变更跟踪，调用方手上没有一个「活的」<see cref="CodeRule"/> 实例可调实例方法。
    /// 实例方法 <see cref="BuildCode(long,DateTime?)"/> 委托本方法实现，两者格式化逻辑同源不重复。
    /// </summary>
    public static string BuildCode(string prefix, string? suffix, bool includeDate, string? dateFormat, int sequencePad, long sequence, DateTime date)
    {
        var datePart = includeDate && !string.IsNullOrEmpty(dateFormat)
            ? date.ToString(dateFormat)
            : string.Empty;
        var seqPart = sequence.ToString().PadLeft(sequencePad, '0');
        return $"{prefix}{datePart}{seqPart}{suffix}";
    }

    public string GetSample() => BuildCode(CurrentSequence > 0 ? CurrentSequence : 1);
}
