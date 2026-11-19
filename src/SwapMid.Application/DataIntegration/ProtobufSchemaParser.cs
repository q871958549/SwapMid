using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SwapMid.DataIntegration;

/// <summary>
/// 零依赖纯 .NET Protobuf Schema（proto2/proto3）AST 解析器：
/// 解析 package、顶层/嵌套 message、enum、oneof、repeated、map 与字段编号供 <see cref="ProtobufWireDatumReader"/> 解码使用。
/// </summary>
internal static class ProtobufSchemaParser
{
    private static readonly Regex BlockCommentRegex = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineCommentRegex = new(@"//[^\r\n]*", RegexOptions.Compiled);
    private static readonly Regex MapFieldRegex = new(
        @"^map\s*<\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_.]+)\s*>\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(\d+)",
        RegexOptions.Compiled);
    private static readonly Regex RegularFieldRegex = new(
        @"^(?:(repeated|optional|required)\s+)?([A-Za-z_.][A-Za-z0-9_.]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(\d+)",
        RegexOptions.Compiled);
    private static readonly Regex EnumValueRegex = new(
        @"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(-?\d+)",
        RegexOptions.Compiled);

    internal sealed class ProtoFileSchema
    {
        public string Package { get; init; } = string.Empty;
        public List<ProtoMessageDef> Messages { get; } = new();
        public List<ProtoEnumDef> Enums { get; } = new();
    }

    internal sealed class ProtoMessageDef
    {
        public string Name { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public ProtoMessageDef? Parent { get; init; }
        public Dictionary<int, ProtoFieldDef> FieldsByNumber { get; } = new();
        public List<ProtoMessageDef> NestedMessages { get; } = new();
        public List<ProtoEnumDef> NestedEnums { get; } = new();
    }

    internal sealed class ProtoEnumDef
    {
        public string Name { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public Dictionary<int, string> NamesByNumber { get; } = new();
    }

    internal sealed record ProtoFieldDef(
        int Number,
        string Name,
        string TypeName,
        bool IsRepeated,
        bool IsMap,
        string? MapKeyType,
        string? MapValueType);

    internal static ProtoFileSchema Parse(string protoText)
    {
        var cleaned = BlockCommentRegex.Replace(protoText ?? string.Empty, " ");
        cleaned = LineCommentRegex.Replace(cleaned, string.Empty);

        var package = string.Empty;
        var pkgMatch = Regex.Match(cleaned, @"\bpackage\s+([A-Za-z0-9_.]+)\s*;");
        if (pkgMatch.Success)
        {
            package = pkgMatch.Groups[1].Value;
        }

        var file = new ProtoFileSchema { Package = package };
        var pos = 0;
        ParseContainerBody(cleaned, ref pos, file, parentMsg: null);
        return file;
    }

    internal static ProtoMessageDef? ResolveMessage(ProtoFileSchema schema, ProtoMessageDef context, string name)
    {
        var clean = name.TrimStart('.');
        for (var cur = context; cur != null; cur = cur.Parent)
        {
            var match = cur.NestedMessages.FirstOrDefault(m =>
                string.Equals(m.Name, clean, StringComparison.Ordinal) ||
                string.Equals(m.FullName, clean, StringComparison.Ordinal));
            if (match != null) return match;
        }

        return FindMessageRecursive(schema.Messages, clean);
    }

    internal static ProtoEnumDef? ResolveEnum(ProtoFileSchema schema, ProtoMessageDef context, string name)
    {
        var clean = name.TrimStart('.');
        for (var cur = context; cur != null; cur = cur.Parent)
        {
            var match = cur.NestedEnums.FirstOrDefault(e =>
                string.Equals(e.Name, clean, StringComparison.Ordinal) ||
                string.Equals(e.FullName, clean, StringComparison.Ordinal));
            if (match != null) return match;
        }

        return schema.Enums.FirstOrDefault(e =>
            string.Equals(e.Name, clean, StringComparison.Ordinal) ||
            string.Equals(e.FullName, clean, StringComparison.Ordinal));
    }

    private static ProtoMessageDef? FindMessageRecursive(IEnumerable<ProtoMessageDef> messages, string target)
    {
        foreach (var m in messages)
        {
            if (string.Equals(m.Name, target, StringComparison.Ordinal) ||
                string.Equals(m.FullName, target, StringComparison.Ordinal) ||
                m.FullName.EndsWith($".{target}", StringComparison.Ordinal))
            {
                return m;
            }

            var child = FindMessageRecursive(m.NestedMessages, target);
            if (child != null) return child;
        }

        return null;
    }

    private static void ParseContainerBody(string text, ref int pos, ProtoFileSchema file, ProtoMessageDef? parentMsg)
    {
        while (pos < text.Length)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length) break;
            if (text[pos] == '}')
            {
                pos++;
                return;
            }

            if (TryConsumeKeyword(text, ref pos, "message"))
            {
                var name = ReadIdentifier(text, ref pos);
                SkipToChar(text, ref pos, '{');
                var fullName = parentMsg != null
                    ? $"{parentMsg.FullName}.{name}"
                    : (string.IsNullOrEmpty(file.Package) ? name : $"{file.Package}.{name}");
                var msg = new ProtoMessageDef { Name = name, FullName = fullName, Parent = parentMsg };
                if (parentMsg != null) parentMsg.NestedMessages.Add(msg);
                else file.Messages.Add(msg);
                ParseContainerBody(text, ref pos, file, msg);
                continue;
            }

            if (TryConsumeKeyword(text, ref pos, "enum"))
            {
                var name = ReadIdentifier(text, ref pos);
                SkipToChar(text, ref pos, '{');
                var fullName = parentMsg != null
                    ? $"{parentMsg.FullName}.{name}"
                    : (string.IsNullOrEmpty(file.Package) ? name : $"{file.Package}.{name}");
                var enumDef = new ProtoEnumDef { Name = name, FullName = fullName };
                ParseEnumBody(text, ref pos, enumDef);
                if (parentMsg != null) parentMsg.NestedEnums.Add(enumDef);
                else file.Enums.Add(enumDef);
                continue;
            }

            if (TryConsumeKeyword(text, ref pos, "oneof"))
            {
                _ = ReadIdentifier(text, ref pos);
                SkipToChar(text, ref pos, '{');
                ParseContainerBody(text, ref pos, file, parentMsg);
                continue;
            }

            var stmt = ReadStatement(text, ref pos);
            if (parentMsg == null || string.IsNullOrWhiteSpace(stmt)) continue;
            if (stmt.StartsWith("option ", StringComparison.Ordinal) ||
                stmt.StartsWith("reserved ", StringComparison.Ordinal) ||
                stmt.StartsWith("extensions ", StringComparison.Ordinal))
            {
                continue;
            }

            var mapMatch = MapFieldRegex.Match(stmt);
            if (mapMatch.Success && int.TryParse(mapMatch.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapNum))
            {
                parentMsg.FieldsByNumber[mapNum] = new ProtoFieldDef(
                    mapNum, mapMatch.Groups[3].Value, "map", false, true, mapMatch.Groups[1].Value, mapMatch.Groups[2].Value);
                continue;
            }

            var fieldMatch = RegularFieldRegex.Match(stmt);
            if (fieldMatch.Success && int.TryParse(fieldMatch.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fieldNum))
            {
                parentMsg.FieldsByNumber[fieldNum] = new ProtoFieldDef(
                    fieldNum, fieldMatch.Groups[3].Value, fieldMatch.Groups[2].Value,
                    string.Equals(fieldMatch.Groups[1].Value, "repeated", StringComparison.Ordinal),
                    false, null, null);
            }
        }
    }

    private static void ParseEnumBody(string text, ref int pos, ProtoEnumDef enumDef)
    {
        while (pos < text.Length)
        {
            SkipWhitespace(text, ref pos);
            if (pos >= text.Length) break;
            if (text[pos] == '}')
            {
                pos++;
                return;
            }

            var stmt = ReadStatement(text, ref pos);
            if (string.IsNullOrWhiteSpace(stmt) || stmt.StartsWith("option ", StringComparison.Ordinal) || stmt.StartsWith("reserved ", StringComparison.Ordinal))
            {
                continue;
            }

            var m = EnumValueRegex.Match(stmt);
            if (m.Success && int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
            {
                enumDef.NamesByNumber.TryAdd(val, m.Groups[1].Value);
            }
        }
    }

    private static void SkipWhitespace(string text, ref int pos)
    {
        while (pos < text.Length && (char.IsWhiteSpace(text[pos]) || text[pos] == ';')) pos++;
    }

    private static bool TryConsumeKeyword(string text, ref int pos, string keyword)
    {
        if (pos + keyword.Length >= text.Length || !text.AsSpan(pos, keyword.Length).SequenceEqual(keyword.AsSpan())) return false;
        if (!char.IsWhiteSpace(text[pos + keyword.Length])) return false;
        pos += keyword.Length;
        SkipWhitespace(text, ref pos);
        return true;
    }

    private static string ReadIdentifier(string text, ref int pos)
    {
        SkipWhitespace(text, ref pos);
        var start = pos;
        while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_' || text[pos] == '.')) pos++;
        return text[start..pos];
    }

    private static void SkipToChar(string text, ref int pos, char target)
    {
        while (pos < text.Length && text[pos] != target) pos++;
        if (pos < text.Length && text[pos] == target) pos++;
    }

    private static string ReadStatement(string text, ref int pos)
    {
        var start = pos;
        while (pos < text.Length && text[pos] != ';' && text[pos] != '}') pos++;
        var stmt = text[start..pos].Trim();
        if (pos < text.Length && text[pos] == ';') pos++;
        return stmt;
    }
}
