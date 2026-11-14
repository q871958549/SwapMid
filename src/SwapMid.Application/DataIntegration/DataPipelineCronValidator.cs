using Cronos;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 数据管道标准 5 段式 Cron 表达式校验器（基于 Cronos）。
/// </summary>
public static class DataPipelineCronValidator
{
    public static string? ValidateAndNormalize(
        bool scheduleEnabled,
        string? scheduleCron)
    {
        var normalized = string.IsNullOrWhiteSpace(scheduleCron)
            ? null
            : scheduleCron.Trim();

        if (normalized == null)
        {
            if (scheduleEnabled)
            {
                throw InvalidCron();
            }

            return null;
        }

        try
        {
            _ = CronExpression.Parse(normalized, CronFormat.Standard);
            return normalized;
        }
        catch (CronFormatException)
        {
            throw InvalidCron();
        }
    }

    private static BusinessException InvalidCron() =>
        new(SwapMidDomainErrorCodes.DataIntegration.PipelineScheduleCronInvalid);
}
