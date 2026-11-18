using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.Layout;

public interface ITablePreferenceAppService : IApplicationService
{
    Task<TablePreferenceSnapshotDto> GetAsync();

    Task SaveTableViewAsync(SaveTableViewPreferenceInput input);

    Task SaveDensityAsync(SaveTableDensityInput input);
}
