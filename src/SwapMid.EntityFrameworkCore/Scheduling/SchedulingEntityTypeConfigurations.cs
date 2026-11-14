using SwapMid.Scheduling;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SwapMid.EntityFrameworkCore.Scheduling;

public static class SchedulingEntityTypeConfigurations
{
    public static void ConfigureScheduling(this ModelBuilder builder)
    {
        builder.Entity<ScheduledJobExecution>(b =>
        {
            b.ToTable(SwapMidConsts.DbTablePrefix + "ScheduledJobExecutions", SwapMidConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.JobKey).HasMaxLength(128);
            b.Property(x => x.JobType).IsRequired().HasMaxLength(256);
            b.Property(x => x.JobMethod).HasMaxLength(128);
            b.Property(x => x.HangfireJobId).HasMaxLength(64);
            b.Property(x => x.TriggerKind).HasConversion<int>();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.Summary).HasMaxLength(512);
            b.Property(x => x.Error).HasMaxLength(2048);
            b.Property(x => x.ArgsJson).HasMaxLength(1024);
            // 查询主路径：按任务 key + 开始时间倒序取历史；另留 StartedAt 单列索引供全局历史。
            b.HasIndex(x => new { x.JobKey, x.StartedAt });
            b.HasIndex(x => x.StartedAt);
        });
    }
}
