using System.Collections.Generic;
using SwapMid.Permissions;

namespace SwapMid.Mcp;

public sealed record McpToolDescriptor(
    string Name,
    string RequiredPermission,
    string Description,
    object InputSchema);

public static class McpToolCatalog
{
    public const string ListDatasources = "list_datasources";
    public const string PreflightPipelineSchema = "preflight_pipeline_schema";
    public const string DiagnoseFailedExecutions = "diagnose_failed_executions";
    public const string DiagnoseDeadLetters = "diagnose_dead_letters";
    public const string DraftDataPipeline = "draft_data_pipeline";
    public const string DiagnoseCdcStreams = "diagnose_cdc_streams";

    public static IReadOnlyList<McpToolDescriptor> All { get; } =
    [
        new McpToolDescriptor(
            Name: ListDatasources,
            RequiredPermission: SwapMidPermissions.DataIntegration.DataSources.Default,
            Description: "Read-only catalog of configured SwapMid data sources (strictly redacted; zero credentials) together with connector capabilities.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    filter = new { type = "string", description = "Optional name substring filter." },
                    limit = new { type = "integer", minimum = 1, maximum = 100, description = "Maximum items to return (default 20)." },
                },
                additionalProperties = false,
            }),
        new McpToolDescriptor(
            Name: PreflightPipelineSchema,
            RequiredPermission: SwapMidPermissions.DataIntegration.DataPipelines.Default,
            Description: "Run a zero-row read-only schema compatibility pre-flight between a source SELECT query and a target table without writing or mutating any data.",
            InputSchema: new
            {
                type = "object",
                required = new[] { "sourceId", "transformConfig" },
                properties = new
                {
                    sourceId = new { type = "string", format = "uuid", description = "Source DataSource GUID." },
                    targetId = new { type = "string", format = "uuid", description = "Optional Target DataSource GUID." },
                    transformConfig = new { type = "string", description = "ETL transform JSON ({sourceQuery, targetTable, mode, keyColumns, watermarkColumn})." },
                },
                additionalProperties = false,
            }),
        new McpToolDescriptor(
            Name: DiagnoseFailedExecutions,
            RequiredPermission: SwapMidPermissions.DataIntegration.PipelineExecutions.Default,
            Description: "Inspect recent Failed and OutcomeUnknown ETL pipeline executions and return safe remediation guidance.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    pipelineId = new { type = "string", format = "uuid", description = "Optional pipeline GUID filter." },
                    limit = new { type = "integer", minimum = 1, maximum = 50, description = "Max items per status bucket (default 10)." },
                },
                additionalProperties = false,
            }),
        new McpToolDescriptor(
            Name: DiagnoseDeadLetters,
            RequiredPermission: SwapMidPermissions.MessageBus.Records.Default,
            Description: "Inspect recent DeadLettered and Failed MessageBus records along with per-subscription delivery attempt diagnostics.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    topicId = new { type = "string", format = "uuid", description = "Optional MessageTopic GUID filter." },
                    limit = new { type = "integer", minimum = 1, maximum = 30, description = "Max records to inspect (default 10)." },
                },
                additionalProperties = false,
            }),
        new McpToolDescriptor(
            Name: DraftDataPipeline,
            RequiredPermission: SwapMidPermissions.DataIntegration.DataPipelines.Create,
            Description: "Create a new ETL DataPipeline strictly in Draft state (scheduleEnabled=false, never activated or executed).",
            InputSchema: new
            {
                type = "object",
                required = new[] { "name", "sourceId", "transformConfig" },
                properties = new
                {
                    name = new { type = "string", minLength = 1, maxLength = 128 },
                    description = new { type = "string", maxLength = 512 },
                    sourceId = new { type = "string", format = "uuid" },
                    targetId = new { type = "string", format = "uuid" },
                    transformConfig = new { type = "string", description = "Validated ETL transform JSON." },
                },
                additionalProperties = false,
            }),
        new McpToolDescriptor(
            Name: DiagnoseCdcStreams,
            RequiredPermission: SwapMidPermissions.DataIntegration.CdcStreams.Default,
            Description: "Inspect configured PostgreSQL logical replication CDC streams, LSN checkpoints, initial snapshot status, upstream schema drift flags/summaries, and optional live WAL lag & schema pre-flight diagnostics.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    filter = new { type = "string", description = "Optional CDC stream name/slot substring filter." },
                    limit = new { type = "integer", minimum = 1, maximum = 50, description = "Max CDC streams to inspect (default 20)." },
                    includePreflight = new { type = "boolean", description = "When true (requires CdcStreams.Manage), runs live pre-flight to inspect WAL lag bytes and table REPLICA IDENTITY." },
                },
                additionalProperties = false,
            }),
    ];
}
