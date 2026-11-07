using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MovieAG.Infrastructure.Configuration;

public static partial class SourcesConfigParser
{
    private static readonly string[] AllowedTypes = ["balancer", "api", "site"];

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>Разбирает и проверяет YAML. Ссылки вида ${NAME} раскрываются через resolveVariable.</summary>
    public static SourcesConfig Parse(string yaml, Func<string, string?> resolveVariable)
    {
        SourcesConfig config;
        try
        {
            config = Deserializer.Deserialize<SourcesConfig?>(yaml) ?? new SourcesConfig();
        }
        catch (YamlException ex)
        {
            throw new SourcesConfigException([$"Строка {ex.Start.Line}: {ex.Message}"]);
        }

        Validate(config);

        foreach (var source in config.Sources)
        {
            source.Token = ResolveToken(source.Token, resolveVariable);
        }

        return config;
    }

    /// <summary>Меняет enabled только у нужного источника, сохраняя комментарии и порядок.</summary>
    public static string SetEnabled(string yaml, string id, bool enabled)
    {
        var newline = yaml.Contains("\r\n") ? "\r\n" : "\n";
        var lines = yaml.Replace("\r\n", "\n").Split('\n').ToList();

        var start = lines.FindIndex(l => IdLine().Match(l) is { Success: true } m && m.Groups["id"].Value == id);
        if (start < 0) throw new SourcesConfigException([$"Источник «{id}» не найден в конфиге"]);

        var itemIndent = lines[start].IndexOf('-');
        var value = enabled ? "true" : "false";
        var propertyIndent = new string(' ', itemIndent + 2);

        for (var i = start + 1; i < lines.Count && BelongsToItem(lines[i], itemIndent); i++)
        {
            if (lines[i].TrimStart().StartsWith("enabled:", StringComparison.Ordinal))
            {
                lines[i] = $"{propertyIndent}enabled: {value}";
                return string.Join(newline, lines);
            }
        }

        lines.Insert(start + 1, $"{propertyIndent}enabled: {value}");
        return string.Join(newline, lines);
    }

    private static bool BelongsToItem(string line, int itemIndent)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        var indent = line.Length - line.TrimStart().Length;
        return indent > itemIndent;
    }

    private static void Validate(SourcesConfig config)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(config.Catalog.Url, UriKind.Absolute, out _))
        {
            errors.Add("catalog.url: нужен абсолютный адрес");
        }

        foreach (var (source, index) in config.Sources.Select((s, i) => (s, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                errors.Add($"Источник №{index}: не указан id");
                continue;
            }

            if (!AllowedTypes.Contains(source.Type))
            {
                errors.Add($"{source.Id}: type должен быть одним из: {string.Join(", ", AllowedTypes)}");
            }

            if (source.Url is not null && !IsHttpUrl(source.Url))
            {
                errors.Add($"{source.Id}: url должен начинаться с http:// или https://");
            }
        }

        errors.AddRange(config.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: id повторяется"));

        if (errors.Count > 0) throw new SourcesConfigException(errors);
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string? ResolveToken(string? token, Func<string, string?> resolveVariable)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var match = VariableReference().Match(token);
        return match.Success ? resolveVariable(match.Groups["name"].Value) : token;
    }

    [GeneratedRegex(@"^\s*-\s*id:\s*[""']?(?<id>[^\s""'#]+)")]
    private static partial Regex IdLine();

    [GeneratedRegex(@"^\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}$")]
    private static partial Regex VariableReference();
}
