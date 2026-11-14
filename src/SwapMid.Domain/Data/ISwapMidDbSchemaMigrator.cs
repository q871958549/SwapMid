using System.Threading.Tasks;

namespace SwapMid.Data;

public interface ISwapMidDbSchemaMigrator
{
    Task MigrateAsync();
}
