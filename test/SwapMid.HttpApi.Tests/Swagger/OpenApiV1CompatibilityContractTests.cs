using System.Text.Json;
using Shouldly;

namespace SwapMid.HttpApi.Tests.Swagger;

/// <summary>
/// v1.0.0 LTS 公开契约兼容性守卫：冻结三大核心集成引擎与平台核心 /api/v1 路由 + HTTP 动词，
/// 防止后续 1.x 维护演进意外重命名或移除稳定公开契约。
/// </summary>
public class OpenApiV1CompatibilityContractTests
{
    private static readonly (string Path, string[] Methods)[] FrozenV1Endpoints =
    [
        ("/api/v1/data-sources", ["get", "post"]),
        ("/api/v1/data-pipelines", ["get", "post"]),
        ("/api/v1/data-pipelines/capabilities", ["get"]),
        ("/api/v1/data-pipelines/preflight-schema", ["post"]),
        ("/api/v1/pipeline-executions", ["get"]),
        ("/api/v1/message-topics", ["get", "post"]),
        ("/api/v1/message-subscriptions", ["get", "post"]),
        ("/api/v1/message-records", ["get"]),
        ("/api/v1/api-groups", ["get", "post"]),
        ("/api/v1/api-routes", ["get", "post"]),
        ("/api/v1/third-party-clients", ["get", "post"]),
        ("/api/v1/third-party-clients/{clientId}/policy", ["put"]),
        ("/api/v1/auth-providers", ["get"]),
        ("/api/v1/notification/webhooks/alertmanager", ["post"]),
    ];

    [Fact]
    public async Task Committed_openapi_snapshot_preserves_frozen_v1_lts_surface()
    {
        var snapshotPath = Path.Combine(
            FindRepoRoot(),
            "docs",
            "api",
            "openapi.generated.json");
        File.Exists(snapshotPath).ShouldBeTrue($"Missing OpenAPI snapshot at {snapshotPath}");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(snapshotPath));
        var paths = doc.RootElement.GetProperty("paths");

        foreach (var (path, requiredMethods) in FrozenV1Endpoints)
        {
            paths.TryGetProperty(path, out var operations)
                .ShouldBeTrue($"v1 LTS frozen endpoint '{path}' was removed or renamed.");

            foreach (var method in requiredMethods)
            {
                operations.TryGetProperty(method, out _)
                    .ShouldBeTrue($"v1 LTS frozen operation '{method.ToUpperInvariant()} {path}' was removed.");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SwapMid.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Cannot locate SwapMid.slnx root.");
    }
}
