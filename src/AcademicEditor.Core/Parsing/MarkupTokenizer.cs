using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Parsing;

/// <summary>
/// Quebra a fonte em linhas e classifica cada uma. Não constrói AST — só decide o que a linha é.
/// </summary>
public static class MarkupTokenizer
{
    /// <summary>Marcador de quebra de página explícita, sozinho numa linha.</summary>
    public const string PageBreakMarker = "\\page";

    /// <summary>Marcação de alinhamento, no início da linha e seguida de espaço.</summary>
    /// <remarks>
    /// É a notação que o próprio Markdown usa para alinhar coluna de tabela — <c>| :-- | :-: | --: |</c>
    /// —, e o dois-pontos marca o lado a que o texto se prende. Não há marcação para justificado:
    /// ele é o padrão da norma, e o estado "sem marcação" é como se volta a ele.
    /// </remarks>
    public const string CenterMarker = ":-:";

    public const string LeftMarker = ":-";

    public const string RightMarker = "-:";

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

        // Fonte terminada em '\n' tem uma última linha, vazia, que o laço acima não vê: ele para
        // quando a posição alcança o fim. Sem este token, o Enter no fim do documento levaria o
        // caret para um offset que nenhuma linha cobre — e o caret desaparece da tela.
        if (source.Length > 0 && source[^1] == '\n')
        {
            tokens.Add(new MarkupToken(MarkupTokenKind.BlankLine, source.Length, 0, source.Length, Level: 0));
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

        // O alinhamento é lido ANTES do resto porque modifica a linha inteira, e o que sobra depois
        // dele continua sendo classificado como sempre. É o que faz ":-: # Título" ser um título
        // centralizado sem que heading e alinhamento precisem se conhecer.
        var alignmentLength = ReadAlignment(line, out var alignment);
        var rest = line[alignmentLength..];
        var restStart = lineStart + alignmentLength;

        // Antes do heading, e pelo mesmo desenho: marcação de bloco decidida no início da linha.
        // Uma definição de nota nunca é heading, então a ordem entre os dois é indiferente — o que
        // importa é que as duas vêm depois do alinhamento.
        var footnote = ReadFootnoteDefinition(rest, out var idLength);

        if (footnote > 0)
        {
            return new MarkupToken(
                MarkupTokenKind.Footnote,
                lineStart,
                lineLength,
                restStart + footnote,
                Level: 0,
                alignment,
                alignmentLength,
                idLength);
        }

        var level = 0;
        while (level < rest.Length && rest[level] == '#')
        {
            level++;
        }

        // Sem espaço depois dos '#', não é heading: "#hashtag" é texto, e é o que o autor espera.
        // Mais de seis níveis também não é heading — a marcação para no ######.
        if (level == 0 || level > MaxHeadingLevel || level >= rest.Length || rest[level] != ' ')
        {
            return new MarkupToken(
                MarkupTokenKind.Text, lineStart, lineLength, restStart, Level: 0, alignment, alignmentLength);
        }

        var contentStart = level;
        while (contentStart < rest.Length && rest[contentStart] == ' ')
        {
            contentStart++;
        }

        return new MarkupToken(
            MarkupTokenKind.Heading,
            lineStart,
            lineLength,
            restStart + contentStart,
            level,
            alignment,
            alignmentLength);
    }

    /// <summary>
    /// Lê o <c>[^id]: </c> de uma definição de nota e devolve quantos caracteres ele ocupa, espaço
    /// de separação incluso. Zero quando a linha não é uma definição.
    /// </summary>
    /// <remarks>
    /// <b>Sem o espaço não é definição</b>, como no heading e no alinhamento: <c>[^1]:algo</c>
    /// continua sendo texto. E o identificador não leva branco nem colchete — é rótulo, não texto —,
    /// que é a mesma regra da chamada em <c>AcademicMarkup</c>. As duas precisam concordar, senão
    /// existiria definição que nenhuma chamada casa.
    /// </remarks>
    private static int ReadFootnoteDefinition(ReadOnlySpan<char> line, out int idLength)
    {
        idLength = 0;

        if (!line.StartsWith("[^", StringComparison.Ordinal))
        {
            return 0;
        }

        var end = 2;

        while (end < line.Length && line[end] != ']' && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        // Identificador vazio, sem fechamento, sem dois-pontos ou sem espaço: nada disso é
        // definição, e a linha volta a ser texto comum.
        if (end == 2 || end + 2 >= line.Length || line[end] != ']' || line[end + 1] != ':' || line[end + 2] != ' ')
        {
            return 0;
        }

        idLength = end - 2;

        var content = end + 2;

        while (content < line.Length && line[content] == ' ')
        {
            content++;
        }

        return content;
    }

    /// <summary>
    /// Lê a marcação de alinhamento no início da linha e devolve quantos caracteres ela ocupa,
    /// espaço de separação incluso.
    /// </summary>
    /// <remarks>
    /// <c>:-:</c> é testado antes de <c>:-</c>: o prefixo mais curto casaria primeiro e roubaria a
    /// marcação de centro. E, como no heading, <b>sem o espaço não é marcação</b> — o que garante
    /// que uma linha começando por dois-pontos e hífen continue sendo texto.
    /// </remarks>
    public static int ReadAlignment(ReadOnlySpan<char> line, out TextAlignment? alignment)
    {
        int marker;

        if (line.StartsWith(CenterMarker, StringComparison.Ordinal))
        {
            (alignment, marker) = (TextAlignment.Center, CenterMarker.Length);
        }
        else if (line.StartsWith(LeftMarker, StringComparison.Ordinal))
        {
            (alignment, marker) = (TextAlignment.Left, LeftMarker.Length);
        }
        else if (line.StartsWith(RightMarker, StringComparison.Ordinal))
        {
            (alignment, marker) = (TextAlignment.Right, RightMarker.Length);
        }
        else
        {
            alignment = null;
            return 0;
        }

        if (marker >= line.Length || line[marker] != ' ')
        {
            alignment = null;
            return 0;
        }

        var end = marker;
        while (end < line.Length && line[end] == ' ')
        {
            end++;
        }

        return end;
    }
}
