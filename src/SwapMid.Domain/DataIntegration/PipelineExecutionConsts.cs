namespace SwapMid.DataIntegration;

public static class PipelineExecutionConsts
{
    public const int MaxPipelineNameLength = 128;
    public const int MaxErrorMessageLength = 2048;
    public const int Sha256HexLength = 64;
    // SQL Server 二段名合法上限：128(schema) + 1('.') + 128(table)。
    public const int MaxCanonicalTargetTableLength = 257;
}
