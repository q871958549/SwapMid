using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class IcebergV2SchemaEvolutionService
{
    internal sealed record ParsedIcebergSchema(
        int CurrentSchemaId,
        int LastColumnId,
        List<CdcLakehouseSchemaFieldDto> Fields,
        Dictionary<int, List<string>> NameAliasesByFieldId);

    internal static ParsedIcebergSchema ParseActiveSchema(string metadataJson)
    {
        var fields = new List<CdcLakehouseSchemaFieldDto>();
        var aliases = new Dictionary<int, List<string>>();
        var currentSchemaId = 0;
        var lastColumnId = MaxCoreEnvelopeFieldId;

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new ParsedIcebergSchema(currentSchemaId, lastColumnId, fields, aliases);
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("current-schema-id", out var sidEl) && sidEl.TryGetInt32(out var sid))
            {
                currentSchemaId = sid;
            }

            if (root.TryGetProperty("last-column-id", out var lcidEl) && lcidEl.TryGetInt32(out var lcid))
            {
                lastColumnId = lcid;
            }

            if (root.TryGetProperty("properties", out var propsEl) &&
                propsEl.ValueKind == JsonValueKind.Object &&
                propsEl.TryGetProperty("schema.name-mapping.default", out var nmEl) &&
                nmEl.ValueKind == JsonValueKind.String)
            {
                aliases = ParseNameMapping(nmEl.GetString());
            }

            if (root.TryGetProperty("schemas", out var schemasEl) && schemasEl.ValueKind == JsonValueKind.Array)
            {
                JsonElement? selectedSchema = null;
                foreach (var schemaEl in schemasEl.EnumerateArray())
                {
                    if (schemaEl.TryGetProperty("schema-id", out var itemSid) &&
                        itemSid.TryGetInt32(out var parsedSid) &&
                        parsedSid == currentSchemaId)
                    {
                        selectedSchema = schemaEl;
                        break;
                    }

                    selectedSchema ??= schemaEl;
                }

                if (selectedSchema.HasValue &&
                    selectedSchema.Value.TryGetProperty("fields", out var fieldsEl) &&
                    fieldsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in fieldsEl.EnumerateArray())
                    {
                        var fid = f.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var v) ? v : 0;
                        var name = f.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() : null;
                        var type = f.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString() : "string";
                        var req = f.TryGetProperty("required", out var rEl) && rEl.ValueKind == JsonValueKind.True;
                        var docStr = f.TryGetProperty("doc", out var dEl) && dEl.ValueKind == JsonValueKind.String ? dEl.GetString() : null;
                        if (fid > 0 && !string.IsNullOrWhiteSpace(name))
                        {
                            fields.Add(new CdcLakehouseSchemaFieldDto
                            {
                                FieldId = fid,
                                Name = name,
                                Type = type ?? "string",
                                Required = req,
                                IsCoreEnvelope = fid <= MaxCoreEnvelopeFieldId,
                                Doc = docStr,
                            });
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed metadata JSON and fall back to empty list.
        }

        return new ParsedIcebergSchema(currentSchemaId, lastColumnId, fields, aliases);
    }

    internal static string? ExtractActiveSchemaCompactJson(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            var root = JsonNode.Parse(metadataJson)?.AsObject();
            if (root == null || root["schemas"] is not JsonArray schemas || schemas.Count == 0) return null;
            var currentSchemaId = root["current-schema-id"]?.GetValue<int>() ?? 0;
            JsonNode? match = null;
            foreach (var s in schemas)
            {
                if (s is JsonObject obj && obj["schema-id"]?.GetValue<int>() == currentSchemaId)
                {
                    match = obj;
                    break;
                }
            }

            return (match ?? schemas[^1])?.ToJsonString(CompactJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateInputs(
        List<AddCdcLakehouseColumnInput> addCols,
        List<RenameCdcLakehouseColumnInput> renameCols,
        List<string> dropCols)
    {
        foreach (var add in addCols)
        {
            var name = (add.Name ?? string.Empty).Trim();
            var type = (add.Type ?? string.Empty).Trim();
            if (!ColumnNamePattern.IsMatch(name))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"列名「{name}」不合法：必须以字母或下划线开头，仅含字母/数字/下划线（1-64 字符）。");
            }

            if (!SupportedTypes.Contains(type))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"不支持的 Iceberg 列类型「{type}」，仅支持 string / long / double / boolean / timestamptz。");
            }
        }

        foreach (var ren in renameCols)
        {
            var fromName = (ren.FromName ?? string.Empty).Trim();
            var toName = (ren.ToName ?? string.Empty).Trim();
            if (!ColumnNamePattern.IsMatch(fromName) || !ColumnNamePattern.IsMatch(toName))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "重命名源列名与目标列名必须符合标识符规范（字母或下划线开头，1-64 字符）。");
            }

            if (string.Equals(fromName, toName, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"重命名列「{fromName}」的源名称与目标名称相同。");
            }
        }

        foreach (var drop in dropCols)
        {
            var name = (drop ?? string.Empty).Trim();
            if (!ColumnNamePattern.IsMatch(name))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"待删除列名「{name}」不符合标识符规范。");
            }
        }
    }

    private static Dictionary<int, List<string>> ParseNameMapping(string? json)
    {
        var result = new Dictionary<int, List<string>>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("field-id", out var fidEl) || !fidEl.TryGetInt32(out var fid) || fid <= 0) continue;
                var list = new List<string>();
                if (item.TryGetProperty("names", out var namesEl) && namesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var n in namesEl.EnumerateArray())
                    {
                        if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                        {
                            list.Add(n.GetString()!);
                        }
                    }
                }

                if (list.Count > 0) result[fid] = list;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed name mapping JSON.
        }

        return result;
    }

    private static void EnsureAllFieldsInNameMapping(Dictionary<int, List<string>> mapping, List<MutableField> fields)
    {
        foreach (var f in fields) AddAliasToNameMapping(mapping, f.FieldId, f.Name);
    }

    private static void AddAliasToNameMapping(Dictionary<int, List<string>> mapping, int fieldId, string name)
    {
        if (!mapping.TryGetValue(fieldId, out var list))
        {
            list = new List<string>();
            mapping[fieldId] = list;
        }

        if (!list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(name);
        }
    }

    private static string SerializeNameMapping(Dictionary<int, List<string>> mapping)
    {
        var arr = new JsonArray();
        foreach (var kv in mapping.OrderBy(k => k.Key))
        {
            var names = new JsonArray();
            foreach (var n in kv.Value) names.Add(n);
            arr.Add(new JsonObject
            {
                ["field-id"] = kv.Key,
                ["names"] = names,
            });
        }

        return arr.ToJsonString(CompactJsonOptions);
    }

    private static CdcLakehouseSchemaFieldDto ToDto(MutableField f) => new()
    {
        FieldId = f.FieldId,
        Name = f.Name,
        Type = f.Type,
        Required = f.Required,
        IsCoreEnvelope = f.FieldId <= MaxCoreEnvelopeFieldId,
        Doc = f.Doc,
    };

    private sealed class MutableField(int fieldId, string name, string type, bool required, string? doc)
    {
        public int FieldId { get; } = fieldId;
        public string Name { get; set; } = name;
        public string Type { get; } = type;
        public bool Required { get; } = required;
        public string? Doc { get; } = doc;
    }
}
