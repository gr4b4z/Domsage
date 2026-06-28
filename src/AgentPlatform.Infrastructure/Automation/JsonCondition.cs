using System.Globalization;
using System.Text.Json;

namespace AgentPlatform.Infrastructure.Automation;

/// <summary>
/// Deterministic predicate over a tool's JSON result. No LLM, no expression language — a dot-path
/// into the result (case-insensitive; numeric segments index arrays, e.g. "Days.1.PrecipProb"),
/// an operator, and a literal. This is what runs every period, so it must be cheap and predictable.
/// </summary>
public static class JsonCondition
{
    public static bool Evaluate(JsonElement root, string path, string op, string value)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!TryNavigate(root, path, out var el)) return false;
        op = op.Trim();

        if (op is ">=" or "<=" or ">" or "<")
        {
            if (!TryNumber(el, out var a) ||
                !double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                return false;
            return op switch { ">=" => a >= b, "<=" => a <= b, ">" => a > b, "<" => a < b, _ => false };
        }

        return op switch
        {
            "==" or "=" or "eq" => Equals(el, value),
            "!=" or "ne" => !Equals(el, value),
            "contains" => AsString(el).Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>Fills {value} in the message with the matched element's value (e.g. a rain probability).</summary>
    public static string Render(string template, JsonElement root, string path)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{value}")) return template ?? "";
        var val = TryNavigate(root, path, out var el) ? AsString(el) : "";
        return template.Replace("{value}", val);
    }

    private static bool TryNavigate(JsonElement root, string path, out JsonElement result)
    {
        result = root;
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(raw, out var idx))
            {
                if (result.ValueKind != JsonValueKind.Array || idx < 0 || idx >= result.GetArrayLength())
                    return false;
                result = result[idx];
                continue;
            }
            if (result.ValueKind != JsonValueKind.Object) return false;
            var found = false;
            foreach (var prop in result.EnumerateObject())
                if (string.Equals(prop.Name, raw, StringComparison.OrdinalIgnoreCase))
                {
                    result = prop.Value;
                    found = true;
                    break;
                }
            if (!found) return false;
        }
        return true;
    }

    private static bool TryNumber(JsonElement el, out double d)
    {
        d = 0;
        if (el.ValueKind == JsonValueKind.Number) { d = el.GetDouble(); return true; }
        return el.ValueKind == JsonValueKind.String &&
               double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
    }

    private static string AsString(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText();

    private static bool Equals(JsonElement el, string value)
    {
        if (TryNumber(el, out var a) &&
            double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
            return Math.Abs(a - b) < 1e-9;
        return string.Equals(AsString(el), value, StringComparison.OrdinalIgnoreCase);
    }
}
