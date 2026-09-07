using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Parsing;

/// <summary>
/// Constrói a AST a partir da fonte. Subconjunto do MVP: parágrafo, heading de 1 a 6 níveis e
/// quebra de página explícita.
/// </summary>
/// <remarks>
/// <para>
/// Cada bloco emite um <see cref="InlineRun"/> por linha da fonte, todos com o mesmo estilo.
/// A estrutura já suporta runs heterogêneos numa linha — é o line breaker que quebra sobre a
/// sequência — então acrescentar <c>**negrito**</c> depois é trabalho deste arquivo, não do
/// motor de layout.
/// </para>
/// <para>
/// <b>Pressupõe fonte normalizada em LF.</b> É o que mantém a invariante de mapeamento do
/// <see cref="InlineRun"/>: o espaço de junção que este parser acrescenta no fim de uma linha
/// soft-wrapped ocupa exatamente a posição do <c>\n</c>. Com CRLF cru, os offsets do caret
/// andariam um caractere por linha. A normalização é feita na leitura do arquivo (Core/IO).
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
        var blocks = new List<BlockNode>();
        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (token.Kind == MarkupTokenKind.BlankLine)
            {
                index++;
                continue;
            }

            if (token.Kind == MarkupTokenKind.PageBreak)
            {
                blocks.Add(new PageBreakNode(token.LineStart, token.LineLength));
                index++;
                continue;
            }

            if (token.Kind == MarkupTokenKind.Heading)
            {
                blocks.Add(BuildHeading(source, token));
                index++;
                continue;
            }

            // Linhas de texto consecutivas refluem juntas: são um parágrafo só. Qualquer outro
            // tipo de linha encerra o parágrafo, inclusive um heading sem linha em branco antes.
            var first = index;
            while (index < tokens.Count && tokens[index].Kind == MarkupTokenKind.Text)
            {
                index++;
            }

            blocks.Add(BuildParagraph(source, tokens, first, index));
        }

        return new DocumentNode(blocks);
    }

    private static HeadingNode BuildHeading(string source, MarkupToken token)
    {
        var style = new TextStyle(HeadingSizesPt[token.Level - 1], FontWeightKind.Bold, Italic: false);

        // "# " sem texto ainda emite um run, vazio. Todo bloco com texto tem ao menos um run,
        // e é dele que o line breaker tira a altura da linha: sem isso, um heading recém-começado
        // seria medido com a altura do corpo e saltaria de tamanho ao receber a primeira letra.
        var text = source.Substring(token.ContentStart, token.ContentLength);
        IReadOnlyList<InlineRun> runs = [new InlineRun(text, token.ContentStart, style)];

        return new HeadingNode(token.Level, token.LineStart, token.LineLength, runs);
    }

    private static ParagraphNode BuildParagraph(string source, IReadOnlyList<MarkupToken> tokens, int first, int end)
    {
        var runs = new List<InlineRun>(end - first);

        for (var i = first; i < end; i++)
        {
            var token = tokens[i];
            var isLastLine = i == end - 1;

            // O espaço de junção separa as palavras das duas linhas e ocupa a posição do '\n'
            // — por isso Text.Length continua igual ao trecho que o run cobre na fonte.
            var text = isLastLine
                ? source.Substring(token.ContentStart, token.ContentLength)
                : string.Concat(source.AsSpan(token.ContentStart, token.ContentLength), " ");

            runs.Add(new InlineRun(text, token.ContentStart, TextStyle.Body));
        }

        var start = tokens[first].LineStart;
        var last = tokens[end - 1];

        return new ParagraphNode(start, last.LineStart + last.LineLength - start, runs);
    }
}
