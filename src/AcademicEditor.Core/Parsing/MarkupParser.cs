using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Parsing;

/// <summary>
/// Constrói a AST a partir da fonte. Subconjunto do MVP: parágrafo, heading de 1 a 6 níveis e
/// quebra de página explícita.
/// </summary>
/// <remarks>
/// <para>
/// <b>Uma linha da fonte é uma linha na página.</b> O parser não reflui linhas consecutivas num
/// parágrafo só, como faria o CommonMark: num editor paginado, o que o autor vê tem de ser o que
/// ele digitou. Um <c>\n</c> é uma quebra visível e uma linha em branco é uma linha em branco —
/// com altura, com posição e com um lugar onde o caret pousa. A quebra automática que resta é a
/// da largura da página, e é do <c>LineBreaker</c>.
/// </para>
/// <para>
/// Cada bloco emite um <see cref="InlineRun"/>, todos com o mesmo estilo. A estrutura já suporta
/// runs heterogêneos numa linha — é o line breaker que quebra sobre a sequência — então
/// acrescentar <c>**negrito**</c> depois é trabalho deste arquivo, não do motor de layout.
/// </para>
/// <para>
/// <b>Pressupõe fonte normalizada em LF.</b> Com CRLF cru, o <c>\r</c> fica fora de todo run e
/// abre um vão de um caractere entre o fim de uma linha e o início da seguinte. A normalização é
/// feita na leitura do arquivo (Core/IO).
/// </para>
/// </remarks>
public static class MarkupParser
{
    // Preset tipográfico do MVP. Na Fase 5 isto vira parte de um preset de norma (ABNT/APA)
    // junto com PageSettings, em vez de constante no parser.
    private static readonly double[] HeadingSizesPt = [20.0, 17.0, 14.0, 12.0, 11.0, 11.0];

    public static DocumentNode Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = MarkupTokenizer.Tokenize(source);
        var blocks = new List<BlockNode>(tokens.Count);

        foreach (var token in tokens)
        {
            blocks.Add(token.Kind switch
            {
                MarkupTokenKind.PageBreak => new PageBreakNode(token.LineStart, token.LineLength),
                MarkupTokenKind.Heading => BuildHeading(source, token),

                // Texto e linha em branco são o mesmo bloco: a segunda é a primeira sem conteúdo.
                // Uma linha só de espaços mantém os espaços, e o caret anda por dentro deles.
                _ => BuildParagraph(source, token),
            });
        }

        return new DocumentNode(blocks);
    }

    private static HeadingNode BuildHeading(string source, MarkupToken token)
    {
        var style = new TextStyle(HeadingSizesPt[token.Level - 1], FontWeightKind.Bold, Italic: false);
        var text = source.Substring(token.ContentStart, token.ContentLength);

        return new HeadingNode(token.Level, token.LineStart, token.LineLength, BuildRuns(text, token.ContentStart, style));
    }

    private static ParagraphNode BuildParagraph(string source, MarkupToken token)
    {
        var text = source.Substring(token.ContentStart, token.ContentLength);

        return new ParagraphNode(token.LineStart, token.LineLength, BuildRuns(text, token.ContentStart, TextStyle.Body));
    }

    // Todo bloco emite ao menos um run, ainda que de texto vazio. É dele que o line breaker tira
    // a altura e o offset da linha: sem run algum, "# " recém-digitado seria medido com a altura
    // do corpo e uma linha em branco reivindicaria o offset zero do documento.
    private static IReadOnlyList<InlineRun> BuildRuns(string text, int sourceStart, TextStyle style) =>
        [new InlineRun(text, sourceStart, style)];
}
