using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AmpersandOfTruth.Web.Features.Analysis.Schema;

/// <summary>
/// Validates model output against the JSON Schema subset our prompts use — Gemini's structured output is
/// constrained decoding, but it ignores some keywords (minLength, maxLength, uniqueItems, …) and a model can
/// still emit garbage on truncation, so every response is re-checked in code before it is trusted or cached.
/// Pure and thread-safe.
/// </summary>
public static class JsonSchemaValidator
{
    private const int MaxErrors = 20;
    private const int MaxRefDepth = 64;

    /// <summary>Returns human-readable errors with JSON paths (e.g. <c>$.evidence[2]: expected string, got number</c>);
    /// empty list = valid. Fed back to the model verbatim in the repair prompt, so keep them short and specific.</summary>
    public static IReadOnlyList<string> Validate(JsonNode schema, JsonNode? instance)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var errors = new List<string>();
        ValidateNode(schema, schema as JsonObject, instance, "$", 0, errors);
        return errors;
    }

    private static void ValidateNode(
        JsonNode schemaNode,
        JsonObject? rootSchema,
        JsonNode? instance,
        string path,
        int refDepth,
        List<string> errors)
    {
        if (errors.Count >= MaxErrors)
        {
            return;
        }

        if (schemaNode is JsonValue boolSchema)
        {
            if (boolSchema.GetValueKind() == JsonValueKind.False)
            {
                AddError(errors, $"{path}: value not allowed");
            }
            return;
        }

        if (schemaNode is not JsonObject schema)
        {
            return;
        }

        if (schema.TryGetPropertyValue("$ref", out var refNode)
            && refNode is JsonValue refVal
            && TryGetString(refVal, out var refStr))
        {
            if (refDepth >= MaxRefDepth)
            {
                AddError(errors, $"{path}: $ref recursion depth exceeded");
                return;
            }

            var resolved = ResolveRef(rootSchema, refStr);
            if (resolved is null)
            {
                AddError(errors, $"{path}: unresolved $ref '{refStr}'");
            }
            else
            {
                ValidateNode(resolved, rootSchema, instance, path, refDepth + 1, errors);
                if (errors.Count >= MaxErrors)
                {
                    return;
                }
            }
        }

        if (schema.TryGetPropertyValue("type", out var typeNode) && typeNode is not null)
        {
            ValidateType(typeNode, instance, path, errors);
            if (errors.Count >= MaxErrors)
            {
                return;
            }
        }

        if (schema.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArray)
        {
            var matched = false;
            foreach (var candidate in enumArray)
            {
                if (JsonEquals(candidate, instance))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                AddError(errors, $"{path}: value {FormatValue(instance)} not in enum");
                if (errors.Count >= MaxErrors)
                {
                    return;
                }
            }
        }

        if (schema.TryGetPropertyValue("const", out var constNode))
        {
            if (!JsonEquals(constNode, instance))
            {
                AddError(errors, $"{path}: expected const {FormatValue(constNode)}, got {FormatValue(instance)}");
                if (errors.Count >= MaxErrors)
                {
                    return;
                }
            }
        }

        if (schema.TryGetPropertyValue("anyOf", out var anyOfNode)
            && anyOfNode is JsonArray anyOfArray
            && anyOfArray.Count > 0)
        {
            var matchedAny = false;
            var branchSummaries = new List<string>(anyOfArray.Count);
            var prefix = $"{path}: ";

            foreach (var branch in anyOfArray)
            {
                if (branch is null)
                {
                    continue;
                }

                var branchErrors = new List<string>();
                ValidateNode(branch, rootSchema, instance, path, refDepth, branchErrors);
                if (branchErrors.Count == 0)
                {
                    matchedAny = true;
                    break;
                }

                var first = branchErrors[0];
                branchSummaries.Add(first.StartsWith(prefix, StringComparison.Ordinal) ? first[prefix.Length..] : first);
            }

            if (!matchedAny)
            {
                AddError(errors, $"{path}: did not match anyOf ({string.Join("; ", branchSummaries)})");
                if (errors.Count >= MaxErrors)
                {
                    return;
                }
            }
        }

        if (instance is JsonObject obj)
        {
            ValidateObject(schema, rootSchema, obj, path, errors);
        }
        else if (instance is JsonArray arr)
        {
            ValidateArray(schema, rootSchema, arr, path, errors);
        }
        else if (instance is JsonValue val)
        {
            if (TryGetString(val, out var str))
            {
                ValidateString(schema, str, path, errors);
            }
            else if (TryGetNumber(val, out var num))
            {
                ValidateNumber(schema, instance, num, path, errors);
            }
        }
    }

    private static void ValidateType(JsonNode typeNode, JsonNode? instance, string path, List<string> errors)
    {
        if (typeNode is JsonValue singleVal && TryGetString(singleVal, out var expectedType))
        {
            if (!MatchesType(expectedType, instance))
            {
                AddError(errors, $"{path}: expected {expectedType}, got {GetTypeName(instance)}");
            }
            return;
        }

        if (typeNode is JsonArray typeArray)
        {
            var allowed = new List<string>(typeArray.Count);
            foreach (var item in typeArray)
            {
                if (item is JsonValue iv && TryGetString(iv, out var t))
                {
                    if (MatchesType(t, instance))
                    {
                        return;
                    }
                    allowed.Add(t);
                }
            }

            if (allowed.Count > 0)
            {
                AddError(errors, $"{path}: expected {string.Join(" or ", allowed)}, got {GetTypeName(instance)}");
            }
        }
    }

    private static void ValidateObject(
        JsonObject schema,
        JsonObject? rootSchema,
        JsonObject obj,
        string path,
        List<string> errors)
    {
        var propsObj = schema["properties"] as JsonObject;

        if (schema["required"] is JsonArray reqArray)
        {
            foreach (var reqItem in reqArray)
            {
                if (reqItem is JsonValue rv && TryGetString(rv, out var reqName) && !obj.ContainsKey(reqName))
                {
                    AddError(errors, $"{path}: missing required property '{reqName}'");
                    if (errors.Count >= MaxErrors)
                    {
                        return;
                    }
                }
            }
        }

        if (propsObj is not null)
        {
            foreach (var (propName, propSchema) in propsObj)
            {
                if (propSchema is not null && obj.TryGetPropertyValue(propName, out var propVal))
                {
                    ValidateNode(propSchema, rootSchema, propVal, $"{path}.{propName}", 0, errors);
                    if (errors.Count >= MaxErrors)
                    {
                        return;
                    }
                }
            }
        }

        if (schema.TryGetPropertyValue("additionalProperties", out var addProps) && addProps is not null)
        {
            if (addProps is JsonValue apBool && apBool.GetValueKind() == JsonValueKind.False)
            {
                foreach (var (propName, _) in obj)
                {
                    if (propsObj is null || !propsObj.ContainsKey(propName))
                    {
                        AddError(errors, $"{path}.{propName}: property not allowed");
                        if (errors.Count >= MaxErrors)
                        {
                            return;
                        }
                    }
                }
            }
            else if (addProps is JsonObject apSchema)
            {
                foreach (var (propName, propVal) in obj)
                {
                    if (propsObj is null || !propsObj.ContainsKey(propName))
                    {
                        ValidateNode(apSchema, rootSchema, propVal, $"{path}.{propName}", 0, errors);
                        if (errors.Count >= MaxErrors)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    private static void ValidateArray(
        JsonObject schema,
        JsonObject? rootSchema,
        JsonArray arr,
        string path,
        List<string> errors)
    {
        if (TryGetSchemaInt(schema["minItems"], out var minItems) && arr.Count < minItems)
        {
            AddError(errors, $"{path}: expected at least {minItems} items, got {arr.Count}");
            if (errors.Count >= MaxErrors)
            {
                return;
            }
        }

        if (TryGetSchemaInt(schema["maxItems"], out var maxItems) && arr.Count > maxItems)
        {
            AddError(errors, $"{path}: expected at most {maxItems} items, got {arr.Count}");
            if (errors.Count >= MaxErrors)
            {
                return;
            }
        }

        if (schema.TryGetPropertyValue("items", out var itemsSchema) && itemsSchema is not null)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                ValidateNode(itemsSchema, rootSchema, arr[i], $"{path}[{i}]", 0, errors);
                if (errors.Count >= MaxErrors)
                {
                    return;
                }
            }
        }
    }

    private static void ValidateString(JsonObject schema, string str, string path, List<string> errors)
    {
        if (TryGetSchemaInt(schema["minLength"], out var minLen) && str.Length < minLen)
        {
            AddError(errors, $"{path}: expected length >= {minLen}, got {str.Length}");
            if (errors.Count >= MaxErrors)
            {
                return;
            }
        }

        if (TryGetSchemaInt(schema["maxLength"], out var maxLen) && str.Length > maxLen)
        {
            AddError(errors, $"{path}: expected length <= {maxLen}, got {str.Length}");
        }
    }

    private static void ValidateNumber(
        JsonObject schema,
        JsonNode instance,
        ParsedNumber num,
        string path,
        List<string> errors)
    {
        if (schema["minimum"] is JsonValue minNode
            && TryGetNumber(minNode, out var minNum)
            && CompareNumbers(num, minNum) < 0)
        {
            AddError(errors, $"{path}: expected >= {FormatValue(minNode)}, got {FormatValue(instance)}");
            if (errors.Count >= MaxErrors)
            {
                return;
            }
        }

        if (schema["maximum"] is JsonValue maxNode
            && TryGetNumber(maxNode, out var maxNum)
            && CompareNumbers(num, maxNum) > 0)
        {
            AddError(errors, $"{path}: expected <= {FormatValue(maxNode)}, got {FormatValue(instance)}");
        }
    }

    private static JsonNode? ResolveRef(JsonObject? rootSchema, string refStr)
    {
        if (rootSchema is null)
        {
            return null;
        }

        if (refStr == "#")
        {
            return rootSchema;
        }

        const string DefsPrefix = "#/$defs/";
        if (refStr.StartsWith(DefsPrefix, StringComparison.Ordinal))
        {
            var defName = refStr[DefsPrefix.Length..];
            if (defName.Length > 0
                && !defName.Contains('/')
                && rootSchema["$defs"] is JsonObject defs
                && defs.TryGetPropertyValue(defName, out var defNode))
            {
                return defNode;
            }
        }

        return null;
    }

    private static bool MatchesType(string expectedType, JsonNode? instance) => expectedType switch
    {
        "null" => instance is null || instance.GetValueKind() == JsonValueKind.Null,
        "boolean" => instance is not null && instance.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "string" => instance is not null && instance.GetValueKind() == JsonValueKind.String,
        "array" => instance is JsonArray,
        "object" => instance is JsonObject,
        "number" => instance is JsonValue v && TryGetNumber(v, out _),
        "integer" => instance is JsonValue v && TryGetNumber(v, out var n) && n.IsInteger,
        _ => false,
    };

    private static string GetTypeName(JsonNode? instance)
    {
        if (instance is null)
        {
            return "null";
        }

        return instance.GetValueKind() switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            _ => "unknown",
        };
    }

    private static bool JsonEquals(JsonNode? a, JsonNode? b)
    {
        var aNull = a is null || a.GetValueKind() == JsonValueKind.Null;
        var bNull = b is null || b.GetValueKind() == JsonValueKind.Null;
        if (aNull || bNull)
        {
            return aNull && bNull;
        }

        var kindA = a!.GetValueKind();
        var kindB = b!.GetValueKind();
        if (kindA != kindB)
        {
            return false;
        }

        switch (kindA)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                return true;

            case JsonValueKind.String:
                return TryGetString((JsonValue)a, out var sa)
                    && TryGetString((JsonValue)b, out var sb)
                    && string.Equals(sa, sb, StringComparison.Ordinal);

            case JsonValueKind.Number:
                return TryGetNumber((JsonValue)a, out var na)
                    && TryGetNumber((JsonValue)b, out var nb)
                    && CompareNumbers(na, nb) == 0;

            case JsonValueKind.Array:
                var arrA = (JsonArray)a;
                var arrB = (JsonArray)b;
                if (arrA.Count != arrB.Count)
                {
                    return false;
                }
                for (var i = 0; i < arrA.Count; i++)
                {
                    if (!JsonEquals(arrA[i], arrB[i]))
                    {
                        return false;
                    }
                }
                return true;

            case JsonValueKind.Object:
                var objA = (JsonObject)a;
                var objB = (JsonObject)b;
                if (objA.Count != objB.Count)
                {
                    return false;
                }
                foreach (var (key, valA) in objA)
                {
                    if (!objB.TryGetPropertyValue(key, out var valB) || !JsonEquals(valA, valB))
                    {
                        return false;
                    }
                }
                return true;

            default:
                return false;
        }
    }

    private static string FormatValue(JsonNode? node) =>
        node is null || node.GetValueKind() == JsonValueKind.Null ? "null" : node.ToJsonString();

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count < MaxErrors)
        {
            errors.Add(message);
        }
    }

    private static bool TryGetString(JsonValue val, out string result)
    {
        if (val.GetValueKind() != JsonValueKind.String)
        {
            result = string.Empty;
            return false;
        }

        if (val.TryGetValue<string>(out var s) && s is not null)
        {
            result = s;
            return true;
        }

        if (val.TryGetValue<JsonElement>(out var el) && el.GetString() is { } es)
        {
            result = es;
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool TryGetSchemaInt(JsonNode? node, out int value)
    {
        if (node is JsonValue v && TryGetNumber(v, out var n) && n.IsInteger)
        {
            if (n.HasDecimal && n.DecimalValue >= int.MinValue && n.DecimalValue <= int.MaxValue)
            {
                value = (int)n.DecimalValue;
                return true;
            }
            if (!n.HasDecimal && n.DoubleValue >= int.MinValue && n.DoubleValue <= int.MaxValue)
            {
                value = (int)n.DoubleValue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetNumber(JsonValue val, out ParsedNumber number)
    {
        if (val.GetValueKind() != JsonValueKind.Number)
        {
            number = default;
            return false;
        }

        if (val.TryGetValue<JsonElement>(out var el))
        {
            if (el.TryGetInt64(out var i64))
            {
                number = new ParsedNumber(true, i64, i64, true);
                return true;
            }
            if (el.TryGetDecimal(out var dec))
            {
                number = new ParsedNumber(true, dec, (double)dec, decimal.Truncate(dec) == dec);
                return true;
            }
            if (el.TryGetDouble(out var dbl))
            {
                number = new ParsedNumber(false, 0m, dbl, double.IsFinite(dbl) && double.Truncate(dbl) == dbl);
                return true;
            }
        }

        if (val.TryGetValue<int>(out var i32))
        {
            number = new ParsedNumber(true, i32, i32, true);
            return true;
        }

        if (val.TryGetValue<long>(out var l64))
        {
            number = new ParsedNumber(true, l64, l64, true);
            return true;
        }

        if (val.TryGetValue<decimal>(out var d))
        {
            number = new ParsedNumber(true, d, (double)d, decimal.Truncate(d) == d);
            return true;
        }

        if (val.TryGetValue<double>(out var db))
        {
            if (decimal.TryParse(db.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var dbDec))
            {
                number = new ParsedNumber(true, dbDec, db, decimal.Truncate(dbDec) == dbDec);
                return true;
            }
            number = new ParsedNumber(false, 0m, db, double.IsFinite(db) && double.Truncate(db) == db);
            return true;
        }

        if (val.TryGetValue<float>(out var fl))
        {
            if (decimal.TryParse(fl.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var flDec))
            {
                number = new ParsedNumber(true, flDec, fl, decimal.Truncate(flDec) == flDec);
                return true;
            }
            number = new ParsedNumber(false, 0m, fl, float.IsFinite(fl) && float.Truncate(fl) == fl);
            return true;
        }

        var raw = val.ToJsonString();
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawDec))
        {
            number = new ParsedNumber(true, rawDec, (double)rawDec, decimal.Truncate(rawDec) == rawDec);
            return true;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawDbl))
        {
            number = new ParsedNumber(false, 0m, rawDbl, double.IsFinite(rawDbl) && double.Truncate(rawDbl) == rawDbl);
            return true;
        }

        number = default;
        return false;
    }

    private static int CompareNumbers(ParsedNumber a, ParsedNumber b) =>
        a.HasDecimal && b.HasDecimal
            ? a.DecimalValue.CompareTo(b.DecimalValue)
            : a.DoubleValue.CompareTo(b.DoubleValue);

    private readonly record struct ParsedNumber(bool HasDecimal, decimal DecimalValue, double DoubleValue, bool IsInteger);
}
