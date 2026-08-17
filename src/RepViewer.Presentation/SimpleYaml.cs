using System.Globalization;

namespace RepViewer.Presentation;

/// <summary>A deliberately small YAML reader for presentation files: nested maps and scalar values.</summary>
internal static class SimpleYaml
{
    public static Dictionary<string, object> Read(string text)
    {
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(int Indent, Dictionary<string, object> Map)>();
        stack.Push((-1, root));
        foreach (var sourceLine in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(sourceLine)) continue;
            var indent = sourceLine.TakeWhile(char.IsWhiteSpace).Count();
            var line = sourceLine.Trim();
            if (line.StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new FormatException($"Invalid presentation YAML line: {sourceLine}");
            var key = line[..colon].Trim();
            var value = StripComment(line[(colon + 1)..]).Trim();
            while (stack.Peek().Indent >= indent) stack.Pop();
            var parent = stack.Peek().Map;
            if (value.Length == 0)
            {
                var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                parent[key] = child;
                stack.Push((indent, child));
            }
            else parent[key] = Scalar(value);
        }
        return root;
    }

    private static string StripComment(string value)
    {
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is '\'' or '"') quote = quote == value[index] ? '\0' : quote == '\0' ? value[index] : quote;
            if (value[index] == '#' && quote == '\0') return value[..index];
        }
        return value;
    }

    private static object Scalar(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"') return value[1..^1];
        if (bool.TryParse(value, out var boolean)) return boolean;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return value;
    }
}
