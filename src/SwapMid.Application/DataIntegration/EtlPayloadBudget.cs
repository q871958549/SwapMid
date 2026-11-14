namespace SwapMid.DataIntegration;

/// <summary>
/// 单次同步 ETL 的累计暂存载荷预算。使用减法比较避免累计值溢出后绕过上限。
/// </summary>
internal sealed class EtlPayloadBudget
{
    internal long UsedBytes { get; private set; }

    internal void Add(long payloadBytes)
    {
        if (payloadBytes < 0 ||
            payloadBytes >
            EtlPipelineRunner.MaxTotalPayloadBytes - UsedBytes)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        UsedBytes += payloadBytes;
    }
}
