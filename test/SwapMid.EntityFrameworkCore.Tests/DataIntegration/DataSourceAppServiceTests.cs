using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class DataSourceAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly IRepository<DataSource, Guid> _dataSourceRepository;

    public DataSourceAppServiceTests()
    {
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _dataPipelineAppService = GetRequiredService<IDataPipelineAppService>();
        _dataSourceRepository = GetRequiredService<IRepository<DataSource, Guid>>();
    }

    [Fact]
    public async Task Should_Create_And_Filter_Data_Source()
    {
        var name = $"Source {Guid.NewGuid():N}";

        var created = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = name,
            Description = "PostgreSQL source",
            Type = DataSourceType.RelationalDatabase,
            Config = """
                {"provider":"sqlserver","host":"localhost","port":1433,"database":"SwapMid","username":"reader"}
                """,
            IsEnabled = true
        });

        created.Id.ShouldNotBe(Guid.Empty);
        created.Name.ShouldBe(name);
        created.Type.ShouldBe(DataSourceType.RelationalDatabase);
        created.IsEnabled.ShouldBeTrue();

        var list = await _dataSourceAppService.GetListAsync(new GetDataSourceListInput
        {
            Filter = name,
            Type = DataSourceType.RelationalDatabase,
            MaxResultCount = 10
        });

        list.TotalCount.ShouldBe(1);
        list.Items.Single().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Should_Return_Data_Sources_By_Name()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Source B {suffix}",
            Type = DataSourceType.File,
            Config = "{\"path\":\"b.csv\"}"
        });
        await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Source A {suffix}",
            Type = DataSourceType.File,
            Config = "{\"path\":\"a.csv\"}"
        });

        var list = await _dataSourceAppService.GetListAsync(new GetDataSourceListInput
        {
            Filter = suffix,
            MaxResultCount = 10
        });

        list.Items.Select(x => x.Name).ToArray().ShouldBe(new[]
        {
            $"Source A {suffix}",
            $"Source B {suffix}"
        });
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Data_Source_Name()
    {
        var name = $"Duplicate Source {Guid.NewGuid():N}";
        await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = name,
            Type = DataSourceType.RestApi,
            Config = "{\"baseUrl\":\"https://example.test\"}"
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
            {
                Name = name,
                Type = DataSourceType.RestApi,
                Config = "{\"baseUrl\":\"https://example.test\"}"
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.DataSourceNameAlreadyExists);
    }

    [Fact]
    public async Task Should_Not_Expose_Secret_In_List_Get_Create_Or_Update_Response()
    {
        const string secret = "response-secret-must-not-appear";
        var name = $"Protected Source {Guid.NewGuid():N}";
        var created = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = name,
            Type = DataSourceType.RelationalDatabase,
            Config = $$"""
                {
                  "provider":"sqlserver",
                  "host":"db.example.test",
                  "port":1433,
                  "database":"SwapMid",
                  "username":"reader",
                  "password":"{{secret}}",
                  "encrypt":false,
                  "trustServerCertificate":true
                }
                """
        });

        AssertProtectedResponse(created, secret);

        var detail = await _dataSourceAppService.GetAsync(created.Id);
        AssertProtectedResponse(detail, secret);

        var list = await _dataSourceAppService.GetListAsync(new GetDataSourceListInput
        {
            Filter = name,
            MaxResultCount = 10
        });
        AssertProtectedResponse(list.Items.Single(), secret);

        // 前端只回传脱敏配置；作用域未变时后端恢复旧密码，但更新响应仍不可读。
        var updated = await _dataSourceAppService.UpdateAsync(created.Id, new DataSourceCreateUpdateDto
        {
            Name = name,
            Type = DataSourceType.RelationalDatabase,
            Config = detail.Config,
            Description = "metadata changed",
            IsEnabled = true
        });
        AssertProtectedResponse(updated, secret);

        var storedConfig = await WithUnitOfWorkAsync(async () =>
            (await _dataSourceRepository.GetAsync(created.Id)).Config);
        storedConfig.ShouldContain(secret);
    }

    [Fact]
    public async Task Should_Fail_Closed_When_Legacy_Stored_Config_Is_Invalid()
    {
        const string malformed = "{\"password\":\"legacy-secret\"";
        var source = new DataSource(
            Guid.NewGuid(),
            $"Malformed Source {Guid.NewGuid():N}",
            DataSourceType.RelationalDatabase,
            malformed);
        await WithUnitOfWorkAsync(() => _dataSourceRepository.InsertAsync(source, autoSave: true));

        var result = await _dataSourceAppService.GetAsync(source.Id);

        result.Config.ShouldBe("{}");
        result.HasSecret.ShouldBeTrue();
        result.Config.ShouldNotContain("legacy-secret");

        var repaired = await _dataSourceAppService.UpdateAsync(source.Id, new DataSourceCreateUpdateDto
        {
            Name = source.Name,
            Type = DataSourceType.RelationalDatabase,
            Config = """
                {"provider":"sqlserver","host":"db.example.test","port":1433,"database":"SwapMid","username":"reader","password":"replacement-secret"}
                """
        });

        AssertProtectedResponse(repaired, "replacement-secret");
        var storedConfig = await WithUnitOfWorkAsync(async () =>
            (await _dataSourceRepository.GetAsync(source.Id)).Config);
        storedConfig.ShouldContain("replacement-secret");
        storedConfig.ShouldNotContain("legacy-secret");
    }

    [Fact]
    public async Task Should_Reject_Invalid_New_Config_With_Safe_Error_Code()
    {
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
            {
                Name = $"Invalid Config Source {Guid.NewGuid():N}",
                Type = DataSourceType.RestApi,
                Config = "{\"token\":\"do-not-store\""
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid);
        exception.Message.ShouldNotContain("do-not-store");
    }

    [Fact]
    public async Task Test_Connection_Invalid_Config_And_Provider_Should_Not_Echo_Input()
    {
        var malformed = await _dataSourceAppService.TestConnectionAsync(new DataSourceTestConnectionInput
        {
            Type = DataSourceType.RelationalDatabase,
            Config = "{\"provider\":\"secret-provider\",\"password\":\"do-not-echo\""
        });
        malformed.Success.ShouldBeFalse();
        malformed.Message.ShouldBe("连接配置格式无效。");
        malformed.Message.ShouldNotContain("secret-provider");
        malformed.Message.ShouldNotContain("do-not-echo");

        var unsupported = await _dataSourceAppService.TestConnectionAsync(new DataSourceTestConnectionInput
        {
            Type = DataSourceType.RelationalDatabase,
            Config = "{\"provider\":\"secret-provider\",\"host\":\"localhost\",\"password\":\"do-not-echo\"}"
        });
        unsupported.Success.ShouldBeFalse();
        unsupported.Message.ShouldBe("连接配置格式无效。");
        unsupported.Message.ShouldNotContain("secret-provider");
        unsupported.Message.ShouldNotContain("do-not-echo");
    }

    [Fact]
    public async Task Test_By_Id_Should_Reject_Forwarding_Stored_Password_To_New_Host()
    {
        var created = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Scoped Test Source {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = """
                {"provider":"sqlserver","host":"old-db.example.test","port":1433,"database":"SwapMid","username":"reader","password":"old-secret"}
                """
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.TestConnectionByIdAsync(created.Id, new DataSourceTestConnectionInput
            {
                Type = DataSourceType.RelationalDatabase,
                Config = """
                    {"provider":"sqlserver","host":"new-db.example.test","port":1433,"database":"SwapMid","username":"reader"}
                    """
            }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.DataSourceCredentialScopeChanged);
    }

    [Fact]
    public async Task Opaque_Config_Should_Require_Complete_Non_Empty_Replacement()
    {
        var name = $"Opaque Source {Guid.NewGuid():N}";
        var created = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = name,
            Type = DataSourceType.RestApi,
            Config = "{\"baseUrl\":\"https://old.example.test\",\"token\":\"old-secret\"}"
        });
        created.Config.ShouldBe("{}");
        created.HasSecret.ShouldBeTrue();

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.UpdateAsync(created.Id, new DataSourceCreateUpdateDto
            {
                Name = name,
                Type = DataSourceType.RestApi,
                Config = created.Config
            }));
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigReplacementRequired);

        var updated = await _dataSourceAppService.UpdateAsync(created.Id, new DataSourceCreateUpdateDto
        {
            Name = name,
            Type = DataSourceType.RestApi,
            Config = "{\"baseUrl\":\"https://new.example.test\",\"token\":\"new-secret\"}"
        });
        updated.Config.ShouldBe("{}");
        updated.HasSecret.ShouldBeTrue();

        var storedConfig = await WithUnitOfWorkAsync(async () =>
            (await _dataSourceRepository.GetAsync(created.Id)).Config);
        storedConfig.ShouldContain("new-secret");
        storedConfig.ShouldNotContain("old-secret");
    }

    [Fact]
    public async Task Established_Target_Resource_Group_Should_Be_Immutable_While_Referenced()
    {
        var source = await CreateRelationalSourceAsync(
            "Stable Group Source",
            "source-group");
        var target = await CreateRelationalSourceAsync(
            "Stable Group Target",
            "physical-db-primary");
        await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Stable Group Pipeline {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = target.Id,
                TransformConfig =
                    "{\"sourceQuery\":\"SELECT 1 AS Id\",\"targetTable\":\"dbo.Target\",\"mode\":\"append\"}",
            });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.UpdateAsync(
                target.Id,
                new DataSourceCreateUpdateDto
                {
                    Name = target.Name,
                    Type = DataSourceType.RelationalDatabase,
                    Config = BuildRelationalConfig("physical-db-renamed"),
                    IsEnabled = true,
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .DataSourceTargetResourceGroupImmutable);
        var stored = await _dataSourceAppService.GetAsync(target.Id);
        stored.Config.ShouldContain("physical-db-primary");
        stored.Config.ShouldNotContain("physical-db-renamed");
    }

    [Fact]
    public async Task Established_Target_Resource_Group_Should_Remain_Immutable_After_Retarget()
    {
        var source = await CreateRelationalSourceAsync(
            "Retarget Source",
            "source-group");
        var originalTarget = await CreateRelationalSourceAsync(
            "Retarget Original",
            "physical-db-original");
        var replacementTarget = await CreateRelationalSourceAsync(
            "Retarget Replacement",
            "physical-db-replacement");
        const string transform =
            "{\"sourceQuery\":\"SELECT 1 AS Id\",\"targetTable\":\"dbo.Target\",\"mode\":\"append\"}";
        var pipeline = await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Retarget Pipeline {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = originalTarget.Id,
                TransformConfig = transform,
            });
        await _dataPipelineAppService.UpdateAsync(
            pipeline.Id,
            new DataPipelineCreateUpdateDto
            {
                Name = pipeline.Name,
                SourceId = source.Id,
                TargetId = replacementTarget.Id,
                TransformConfig = transform,
            });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _dataSourceAppService.UpdateAsync(
                originalTarget.Id,
                new DataSourceCreateUpdateDto
                {
                    Name = originalTarget.Name,
                    Type = DataSourceType.RelationalDatabase,
                    Config = BuildRelationalConfig("physical-db-reclassified"),
                    IsEnabled = true,
                }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .DataSourceTargetResourceGroupImmutable);
        var stored = await _dataSourceAppService.GetAsync(originalTarget.Id);
        stored.Config.ShouldContain("physical-db-original");
        stored.Config.ShouldNotContain("physical-db-reclassified");
    }

    [Fact]
    public async Task Referenced_Legacy_Target_Should_Allow_One_Time_Group_Initialization()
    {
        var source = await CreateRelationalSourceAsync(
            "Legacy Group Source",
            "source-group");
        var target = await _dataSourceAppService.CreateAsync(
            new DataSourceCreateUpdateDto
            {
                Name = $"Legacy Group Target {Guid.NewGuid():N}",
                Type = DataSourceType.RelationalDatabase,
                Config = BuildRelationalConfig(targetResourceGroup: null),
            });
        await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Legacy Group Pipeline {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = target.Id,
                TransformConfig =
                    "{\"sourceQuery\":\"SELECT 1 AS Id\",\"targetTable\":\"dbo.Target\",\"mode\":\"append\"}",
            });

        var updated = await _dataSourceAppService.UpdateAsync(
            target.Id,
            new DataSourceCreateUpdateDto
            {
                Name = target.Name,
                Type = DataSourceType.RelationalDatabase,
                Config = BuildRelationalConfig("physical-db-initialized"),
                IsEnabled = true,
            });

        updated.Config.ShouldContain("physical-db-initialized");
    }

    [Fact]
    public async Task Referenced_Target_With_Invalid_Legacy_Group_Should_Allow_Repair()
    {
        var source = await CreateRelationalSourceAsync(
            "Invalid Legacy Group Source",
            "source-group");
        var target = new DataSource(
            Guid.NewGuid(),
            $"Invalid Legacy Group Target {Guid.NewGuid():N}",
            DataSourceType.RelationalDatabase,
            BuildRelationalConfig("bad group"));
        await WithUnitOfWorkAsync(() =>
            _dataSourceRepository.InsertAsync(target, autoSave: true));
        await _dataPipelineAppService.CreateAsync(
            new DataPipelineCreateUpdateDto
            {
                Name = $"Invalid Legacy Group Pipeline {Guid.NewGuid():N}",
                SourceId = source.Id,
                TargetId = target.Id,
                TransformConfig =
                    "{\"sourceQuery\":\"SELECT 1 AS Id\",\"targetTable\":\"dbo.Target\",\"mode\":\"append\"}",
            });

        var updated = await _dataSourceAppService.UpdateAsync(
            target.Id,
            new DataSourceCreateUpdateDto
            {
                Name = target.Name,
                Type = DataSourceType.RelationalDatabase,
                Config = """
                    {"provider":"sqlserver","host":"localhost","port":1433,"database":"SwapMid","username":"reader","password":"replacement-secret","targetResourceGroup":"physical-db-repaired"}
                    """,
                IsEnabled = true,
            });

        updated.Config.ShouldContain("physical-db-repaired");
        updated.Config.ShouldNotContain("replacement-secret");
    }

    private Task<DataSourceDto> CreateRelationalSourceAsync(
        string prefix,
        string targetResourceGroup)
        => _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"{prefix} {Guid.NewGuid():N}",
            Type = DataSourceType.RelationalDatabase,
            Config = BuildRelationalConfig(targetResourceGroup),
        });

    private static string BuildRelationalConfig(string? targetResourceGroup)
    {
        var group = targetResourceGroup == null
            ? string.Empty
            : $",\"targetResourceGroup\":\"{targetResourceGroup}\"";
        return
            $"{{\"provider\":\"sqlserver\",\"host\":\"localhost\",\"port\":1433,\"database\":\"SwapMid\",\"username\":\"reader\"{group}}}";
    }

    private static void AssertProtectedResponse(DataSourceDto dto, string secret)
    {
        dto.HasSecret.ShouldBeTrue();
        dto.Config.ShouldNotContain(secret);
        dto.Config.ShouldNotContain("\"password\"");
        dto.Config.ShouldNotContain("\"url\"");
        dto.Config.ShouldNotContain("\"driverClassName\"");
    }
}
