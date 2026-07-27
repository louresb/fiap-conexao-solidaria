using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using ConexaoSolidaria.Contracts.Knowledge;

namespace ConexaoSolidaria.Knowledge.Api.Retrieval;

public sealed class KnowledgeOptions
{
    public string RootPath { get; set; } = "Knowledge";
    public double MinimumScore { get; set; } = 0.2;
    public int MaximumSources { get; set; } = 3;
}

public interface IKnowledgeRetriever
{
    IReadOnlyList<KnowledgeSourceDto> Search(string tenantId, string question);
    int CountDocuments(string tenantId);
}

public sealed partial class KnowledgeRetriever : IKnowledgeRetriever
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "as", "com", "como", "da", "das", "de", "do", "dos", "e", "em", "o", "os", "para", "por", "que", "um", "uma"
    };

    private readonly string _rootPath;
    private readonly KnowledgeOptions _options;

    public KnowledgeRetriever(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _options = configuration.GetSection("Knowledge").Get<KnowledgeOptions>() ?? new KnowledgeOptions();
        _rootPath = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(environment.ContentRootPath, _options.RootPath);
    }

    public IReadOnlyList<KnowledgeSourceDto> Search(string tenantId, string question)
    {
        var queryTokens = Tokenize(question);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        return ReadDocuments(tenantId)
            .SelectMany(document => document.Paragraphs.Select(paragraph => Score(document, paragraph, queryTokens)))
            .Where(result => result.Score >= _options.MinimumScore)
            .OrderByDescending(result => result.Score)
            .GroupBy(result => result.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Clamp(_options.MaximumSources, 1, 5))
            .Select(result => new KnowledgeSourceDto(
                result.DocumentId,
                result.Title,
                Truncate(result.Paragraph, 360),
                Math.Round(result.Score, 3)))
            .ToList();
    }

    public int CountDocuments(string tenantId) => ReadDocuments(tenantId).Count;

    private IReadOnlyList<KnowledgeDocument> ReadDocuments(string tenantId)
    {
        var tenantDirectory = Path.Combine(_rootPath, tenantId);
        if (!Directory.Exists(tenantDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(tenantDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var content = File.ReadAllText(path);
                var title = content.Split('\n')
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..]
                    ?? Path.GetFileNameWithoutExtension(path);
                var paragraphs = ParagraphSeparator().Split(content)
                    .Select(paragraph => paragraph.Trim())
                    .Where(paragraph => paragraph.Length >= 40)
                    .ToList();
                return new KnowledgeDocument(Path.GetFileNameWithoutExtension(path), title, paragraphs);
            })
            .ToList();
    }

    private static ScoredParagraph Score(KnowledgeDocument document, string paragraph, HashSet<string> queryTokens)
    {
        var paragraphTokens = Tokenize(paragraph);
        var matched = queryTokens.Count(token => paragraphTokens.Contains(token));
        var coverage = matched / (double)queryTokens.Count;
        var precision = matched / (double)Math.Max(paragraphTokens.Count, 1);
        var score = (coverage * 0.85) + (Math.Min(precision * 8, 1) * 0.15);
        return new ScoredParagraph(document.Id, document.Title, paragraph, score);
    }

    private static HashSet<string> Tokenize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
            }
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength
        ? value
        : string.Concat(value.AsSpan(0, maximumLength - 3), "...");

    [GeneratedRegex(@"\r?\n\s*\r?\n", RegexOptions.Compiled)]
    private static partial Regex ParagraphSeparator();

    private sealed record KnowledgeDocument(string Id, string Title, IReadOnlyList<string> Paragraphs);
    private sealed record ScoredParagraph(string DocumentId, string Title, string Paragraph, double Score);
}
