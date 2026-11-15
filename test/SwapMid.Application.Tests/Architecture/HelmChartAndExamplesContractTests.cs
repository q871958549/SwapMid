using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class HelmChartAndExamplesContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Helm_chart_contains_production_workloads_and_fail_closed_defaults()
    {
        var chartDir = Path.Combine(RepoRoot, "charts", "swapmid");
        Directory.Exists(chartDir).ShouldBeTrue("charts/swapmid directory must exist");

        string[] requiredFiles =
        [
            "Chart.yaml",
            "values.yaml",
            "templates/_helpers.tpl",
            "templates/secret.yaml",
            "templates/configmap.yaml",
            "templates/migrator-job.yaml",
            "templates/api-deployment.yaml",
            "templates/api-service.yaml",
            "templates/web-deployment.yaml",
            "templates/web-service.yaml",
            "templates/ingress.yaml",
            "templates/hpa.yaml",
            "templates/pdb.yaml",
            "templates/servicemonitor.yaml",
            "templates/NOTES.txt",
        ];

        foreach (var rel in requiredFiles)
        {
            File.Exists(Path.Combine(chartDir, rel))
                .ShouldBeTrue($"Expected Helm chart file '{rel}' to exist");
        }

        var valuesYaml = File.ReadAllText(Path.Combine(chartDir, "values.yaml")).Replace("\r\n", "\n");
        valuesYaml.ShouldContain("apiGateway:\n  enabled: false", Case.Sensitive);
        valuesYaml.ShouldContain("mcp:\n  enabled: false", Case.Sensitive);
        valuesYaml.ShouldContain("externalOidc:\n  enabled: false", Case.Sensitive);
        valuesYaml.ShouldContain("authorizationEndpoint: \"\"");
        valuesYaml.ShouldContain("postgresPassword: \"\"");
        valuesYaml.ShouldContain("stringEncryptionPassphrase: \"\"");

        var chartYaml = File.ReadAllText(Path.Combine(chartDir, "Chart.yaml"));
        var configMapYaml = File.ReadAllText(Path.Combine(chartDir, "templates", "configmap.yaml"));
        configMapYaml.ShouldContain("AuthServer__ExternalOidc__AuthorizationEndpoint");
        if (chartYaml.Contains("github.com/q871958549/SwapMid", StringComparison.OrdinalIgnoreCase))
        {
            valuesYaml.ShouldContain("repository: q871958549/swapmid-api");
            valuesYaml.ShouldContain("repository: q871958549/swapmid-web");
            valuesYaml.ShouldContain("repository: q871958549/swapmid-migrator");
        }

        var migratorJob = File.ReadAllText(Path.Combine(chartDir, "templates", "migrator-job.yaml"));
        migratorJob.ShouldContain("\"helm.sh/hook\": pre-install,pre-upgrade");

        var apiDeployment = File.ReadAllText(Path.Combine(chartDir, "templates", "api-deployment.yaml"));
        apiDeployment.ShouldContain("/health/live");
        apiDeployment.ShouldContain("/health/ready");
    }

    [Fact]
    public void Reference_examples_exist_and_contain_valid_json_templates()
    {
        var examplesDir = Path.Combine(RepoRoot, "examples");
        Directory.Exists(examplesDir).ShouldBeTrue("examples/ directory must exist");
        File.Exists(Path.Combine(examplesDir, "README.md")).ShouldBeTrue();

        var jsonFiles = Directory.GetFiles(examplesDir, "*.json", SearchOption.AllDirectories);
        jsonFiles.Length.ShouldBeGreaterThanOrEqualTo(2);

        foreach (var file in jsonFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        }
    }

    [Fact]
    public void Turnkey_observability_pack_contains_valid_grafana_dashboard_and_closed_loop_alerts()
    {
        var obsCompose = Path.Combine(RepoRoot, "docker", "docker-compose.observability.yml");
        var promRules = Path.Combine(RepoRoot, "docker", "observability", "prometheus", "alert-rules.yml");
        var alertmanager = Path.Combine(RepoRoot, "docker", "observability", "alertmanager", "alertmanager.yml");
        var dashboard = Path.Combine(RepoRoot, "docker", "observability", "grafana", "dashboards", "swapmid-overview.json");

        File.Exists(obsCompose).ShouldBeTrue();
        File.Exists(promRules).ShouldBeTrue();
        File.Exists(alertmanager).ShouldBeTrue();
        File.Exists(dashboard).ShouldBeTrue();

        var rulesText = File.ReadAllText(promRules);
        rulesText.ShouldContain("swapmid_etl_execution_results_total");
        rulesText.ShouldContain("swapmid_message_delivery_total");
        rulesText.ShouldContain("swapmid_gateway_requests_total");

        var amText = File.ReadAllText(alertmanager);
        amText.ShouldContain("/api/v1/notification/webhooks/alertmanager");

        var dashboardJson = File.ReadAllText(dashboard);
        using var doc = JsonDocument.Parse(dashboardJson);
        doc.RootElement.GetProperty("uid").GetString().ShouldBe("swapmid-overview-v1");
        doc.RootElement.GetProperty("panels").GetArrayLength().ShouldBeGreaterThanOrEqualTo(6);
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
