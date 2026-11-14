using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace SwapMid.HttpApi.Tests.Swagger;

public class RuntimeSwaggerContractTests
{
    [Fact]
    public async Task All_controllers_produce_valid_swagger_document_matching_committed_snapshot()
    {
        using var host = new ContractTestHost(enableRuntimeSwagger: true);
        using var client = host.CreateClient(authenticated: false);

        using var response = await client.GetAsync("/swagger/all/swagger.json");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.ShouldBeTrue(
            $"Swagger 生成在运行时抛异常（HTTP {(int)response.StatusCode}）：\n{body}");

        var runtime = JsonNode.Parse(body)!;
        var paths = runtime["paths"]!.AsObject();
        foreach (var contractOnly in paths.Select(p => p.Key)
                     .Where(k => k.StartsWith("/contract/", StringComparison.Ordinal))
                     .ToList())
        {
            paths.Remove(contractOnly);
        }

        paths.Count.ShouldBeGreaterThan(25,
            "PartManager 可能漏挂了 SwapMid.HttpApi 程序集，生成的端点数量异常偏少");

        var snapshotPath = Path.Combine(
            FindRepoRoot(),
            "docs",
            "api",
            "openapi.generated.json");

        var normalizedRuntime = Normalize(runtime);

        if (string.Equals(
                Environment.GetEnvironmentVariable("SWAPMID_UPDATE_OPENAPI_SNAPSHOT"),
                "1",
                StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, normalizedRuntime + "\n");
        }

        File.Exists(snapshotPath).ShouldBeTrue(
            $"未找到契约快照 {snapshotPath}；请使用 SWAPMID_UPDATE_OPENAPI_SNAPSHOT=1 导出快照");

        var committed = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!;
        normalizedRuntime.ShouldBe(
            Normalize(committed),
            "运行时 /swagger/all/swagger.json 与已提交的 docs/api/openapi.generated.json 不一致");
    }

    private static string Normalize(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SwapMid.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("向上查找未定位到 SwapMid.slnx");
    }
}
