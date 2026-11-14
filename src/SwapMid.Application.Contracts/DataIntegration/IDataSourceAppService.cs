using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.DataIntegration;

public interface IDataSourceAppService
    : ICrudAppService<DataSourceDto, Guid, GetDataSourceListInput, DataSourceCreateUpdateDto>
{
    Task<TestConnectionResultDto> TestConnectionAsync(DataSourceTestConnectionInput input);
    Task<TestConnectionResultDto> TestConnectionByIdAsync(Guid id, DataSourceTestConnectionInput input);
}
