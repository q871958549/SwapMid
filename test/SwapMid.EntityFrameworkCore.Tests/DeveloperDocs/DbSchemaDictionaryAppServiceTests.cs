using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.EntityFrameworkCore.DeveloperDocs;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace SwapMid.DeveloperDocs;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class DbSchemaDictionaryAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDbSchemaDictionaryAppService _service;

    public DbSchemaDictionaryAppServiceTests()
    {
        _service = GetRequiredService<IDbSchemaDictionaryAppService>();
    }

    [Fact]
    public async Task Should_Reflect_Platform_Business_Tables_Only()
    {
        var tables = await _service.GetTablesAsync();

        tables.ShouldNotBeEmpty();
        tables.ShouldContain(t => t.Name == "AppCodeRules");
        tables.ShouldContain(t => t.Name == "AppDataSources");
        tables.ShouldContain(t => t.Name == "AppDataPipelines");
        tables.ShouldContain(t => t.Name == "AppMessageTopics");
        tables.ShouldContain(t => t.Name == "AppApiRoutes");
        tables.ShouldNotContain(t => t.Name == "AbpUsers");
    }

    [Fact]
    public void Schema_Provider_Should_Inject_Every_Business_DbContext()
    {
        var injected = typeof(EfCoreSchemaMetadataProvider)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDbContextProvider<>))
            .Select(t => t.GetGenericArguments()[0])
            .ToHashSet();

        var contexts = typeof(SwapMidDbContext).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

        contexts.ShouldNotBeEmpty();
        foreach (var ctx in contexts)
            injected.ShouldContain(ctx);
    }

    [Fact]
    public async Task Should_Filter_Tables_By_Column_Name()
    {
        var table = await _service.GetTableAsync("AppCodeRules");
        var business = table.Columns.First(c => !c.IsSystemColumn);
        var tables = await _service.GetTablesAsync(filter: business.Name);

        tables.ShouldContain(t => t.Name == "AppCodeRules");
    }

    [Fact]
    public async Task Should_Get_Table_Columns_With_PrimaryKey()
    {
        var table = await _service.GetTableAsync("AppCodeRules");

        table.Name.ShouldBe("AppCodeRules");
        table.Columns.ShouldNotBeEmpty();
        table.Columns.ShouldContain(c => c.Name == "Id" && c.IsPrimaryKey);
    }

    [Fact]
    public async Task Should_Export_Excel_NonEmpty()
    {
        using var content = await _service.ExportExcelAsync();

        content.ShouldNotBeNull();
        content.GetStream().Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Should_Export_Markdown_With_Heading_And_Table()
    {
        using var content = await _service.ExportMarkdownAsync();
        using var reader = new StreamReader(content.GetStream());
        var md = await reader.ReadToEndAsync();

        md.ShouldContain("# 数据库表字典");
        md.ShouldContain("AppCodeRules");
        md.ShouldContain("| 字段 | 类型 | 可空 | 主键 | 外键 | 索引 | 默认值 | 中文含义 |");
    }

    [Fact]
    public async Task Should_Fallback_Audit_Column_Chinese_Names_And_Mark_System()
    {
        var table = await _service.GetTableAsync("AppCodeRules");

        var id = table.Columns.Single(c => c.Name == "Id");
        id.IsSystemColumn.ShouldBeTrue();
        id.Comment.ShouldBe("主键");

        var creationTime = table.Columns.Single(c => c.Name == "CreationTime");
        creationTime.Comment.ShouldBe("创建时间");

        table.Columns.ShouldContain(c => !c.IsSystemColumn);
    }
}
