namespace AcademicEditor.Core.Parsing;

/// <summary>
/// Quebra a fonte em linhas e classifica cada uma. Não constrói AST — só decide o que a linha é.
/// </summary>
public static class MarkupTokenizer
{
    /// <summary>Marcador de quebra de página explícita, sozinho numa linha.</summary>
    public const string PageBreakMarker = "\\page";

    private const int MaxHeadingLevel = 6;

    public static IReadOnlyList<MarkupToken> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new List<MarkupToken>();
        var position = 0;

        while (position < source.Length)
        {
            var newline = source.IndexOf('\n', position);
            var lineEnd = newline < 0 ? source.Length : newline;
            var next = newline < 0 ? source.Length : newline + 1;

            // Um \r final pertence ao terminador, não ao conteúdo. O caminho normal é LF —
            // a normalização de CRLF acontece na leitura do arquivo (Core/IO).
            if (lineEnd > position && source[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            tokens.Add(Classify(source, position, lineEnd - position));
            position = next;
        }

        return tokens;
    }

    private static MarkupToken Classify(string source, int lineStart, int lineLength)
    {
        var line = source.AsSpan(lineStart, lineLength);

        if (line.IsWhiteSpace())
        {
            return new MarkupToken(MarkupTokenKind.BlankLine, lineStart, lineLength, lineStart, Level: 0);
        }

        if (line.Trim().SequenceEqual(PageBreakMarker))
        {
            return new MarkupToken(MarkupTokenKind.PageBreak, lineStart, lineLength, lineStart, Level: 0);
        }

        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        // Sem espaço depois dos '#', não é heading: "#hashtag" é texto, e é o que o autor espera.
        // Mais de seis níveis também não é heading — a marcação para no ######.
        if (level == 0 || level > MaxHeadingLevel || level >= line.Length || line[level] != ' ')
        {
            return new MarkupToken(MarkupTokenKind.Text, lineStart, lineLength, lineStart, Level: 0);
        }

        var contentStart = level;
        while (contentStart < line.Length && line[contentStart] == ' ')
        {
            contentStart++;
        }

        return new MarkupToken(MarkupTokenKind.Heading, lineStart, lineLength, lineStart + contentStart, level);
    }
}
