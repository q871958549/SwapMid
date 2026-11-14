using System;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class PostgreSqlContractFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "SWAPMID_RUN_POSTGRES_CONTRACT_TESTS";

    public PostgreSqlContractFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.Equals(enabled, "1", StringComparison.Ordinal) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Requires Docker PostgreSQL contract suite ({EnvironmentVariable}=1).";
        }
    }
}
