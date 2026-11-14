using System.Text.Json.Nodes;
using SwapMid.HttpApi.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;

namespace SwapMid.HttpApi.Tests.Swagger;

public class ApiDeprecationContractTests
{
    [Fact]
    public async Task Deprecated_endpoint_emits_standard_headers_and_openapi_marker()
    {
        using var host = new ContractTestHost(enableRuntimeSwagger: true);
        using var client = host.CreateClient(authenticated: false);

        using var response = await client.GetAsync("/contract/api-versioning/deprecated");
        response.EnsureSuccessStatusCode();
        response.Headers.GetValues("Deprecation").Single().ShouldBe("@1767225600");
        response.Headers.GetValues("Sunset").Single().ShouldBe("Fri, 01 Jan 2027 00:00:00 GMT");
        response.Headers.GetValues("Link").Single().ShouldBe(
            "</docs/api/migrations/contract-example>; rel=\"deprecation\"");

        var swagger = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;
        swagger["paths"]!["/contract/api-versioning/deprecated"]!["get"]!["deprecated"]!
            .GetValue<bool>()
            .ShouldBeTrue();
    }

    [Fact]
    public void Sunset_must_be_later_than_deprecation()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ApiDeprecationAttribute(
                "2026-01-01T00:00:00Z",
                "/docs/api/migrations/contract-example")
            {
                SunsetAtUtc = "2025-12-31T23:59:59Z",
            });
    }

    [Fact]
    public void Documentation_link_must_be_safe_and_reachable_by_contract()
    {
        Should.Throw<ArgumentException>(() =>
            new ApiDeprecationAttribute("2026-01-01T00:00:00Z", "http://example.com/migration"));
        Should.Throw<ArgumentException>(() =>
            new ApiDeprecationAttribute("2026-01-01T00:00:00Z", "relative/migration"));
    }
}

[ApiController]
[AllowAnonymous]
[Route("contract/api-versioning")]
public sealed class ApiDeprecationContractController : ControllerBase
{
    [HttpGet("deprecated")]
    [ApiDeprecation(
        "2026-01-01T00:00:00Z",
        "/docs/api/migrations/contract-example",
        SunsetAtUtc = "2027-01-01T00:00:00Z")]
    public IActionResult GetDeprecated() => Ok(new { status = "deprecated" });
}
