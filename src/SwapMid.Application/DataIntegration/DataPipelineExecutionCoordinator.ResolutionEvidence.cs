using System;
using System.Linq;

namespace SwapMid.DataIntegration;

public sealed partial class DataPipelineExecutionCoordinator
{
    internal const string LegacyVerificationNotePrefix =
        "dba-evidence:legacy-redacted-";

    internal static string NormalizeVerificationNote(
        ResolvePipelineExecutionOutcomeInput input,
        PipelineExecutionResolution resolution)
    {
        if (resolution == PipelineExecutionResolution.ConfirmedNotApplied &&
            input.ProcessedRows.HasValue)
        {
            throw ResolutionInvalid();
        }

        var structuredReference = NormalizeEvidenceReference(
            input.VerificationNote);
        if (structuredReference != null)
        {
            return structuredReference;
        }

        // v1 已发布过 8–512 自由文本契约。继续接受其原有规范化边界，避免
        // 静默打断旧客户端；但正文可能含 SQL/连接串/密钥，绝不再持久化。
        var legacyNote = string.Join(
            ' ',
            (input.VerificationNote ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        if (legacyNote.Length is < 8 or > 512 ||
            legacyNote.Any(character => char.IsControl(character)))
        {
            throw ResolutionInvalid();
        }

        return LegacyVerificationNotePrefix + Guid.NewGuid().ToString("N");
    }

    private static bool IsLegacyRedactedVerificationNote(string value) =>
        value.StartsWith(
            LegacyVerificationNotePrefix,
            StringComparison.Ordinal);
}
