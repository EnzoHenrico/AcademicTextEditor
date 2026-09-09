using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Parsing;

/// <summary>
/// A notação acadêmica dentro de uma linha: chamada de nota (<c>[^1]</c>) e fórmula
/// (<c>$x^2$</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Roda antes do <see cref="InlineMarkup"/>, e não dentro dele.</b> Aquele é uma máquina de
/// parear delimitadores repetidos; estes são trechos cercados, com abertura e fechamento
/// distintos. Fatiar a linha aqui e entregar os pedaços de texto comum para lá mantém as duas
/// regras separadas — e é o que faz um <c>*</c> dentro de uma fórmula continuar sendo multiplicação.
/// </para>
/// <para>
/// <b>A invariante do <see cref="InlineRun"/> continua de pé:</b> todo caractere da linha entra em
/// exatamente um run, na ordem, cópia literal. É ela que mantém o caret funcionando, e é por isso
/// que <c>[^</c> e <c>$</c> viram runs de marcação em vez de sumirem do texto.
/// </para>
/// <para>
/// <b>É também o que limita o que dá para fazer aqui.</b> Numerar a nota automaticamente —
/// <c>[^abc]</c> aparecendo como <c>¹</c> — exigiria um run cujo texto exibido é <i>diferente</i>
/// do texto da fonte, e aí <c>CaretGeometry.OffsetInRun</c>, que mede prefixos, devolveria offsets
/// errados. O que aparece é o próprio identificador, literal. A mesma limitação é o que segura
/// <c>[@cite]</c> resolvido contra um <c>.bib</c>.
/// </para>
/// </remarks>
public static class AcademicMarkup
{
    /// <summary>Abertura da chamada de nota de rodapé.</summary>
    public const string FootnoteOpen = "[^";

    /// <summary>Fechamento da chamada de nota de rodapé.</summary>
    public const string FootnoteClose = "]";

    /// <summary>Delimitador de fórmula, dos dois lados.</summary>
    public const char MathDelimiter = '$';

    private enum SpanKind
    {
        Footnote,
        Math,
    }

    private readonly record struct Span(SpanKind Kind, int Start, int ContentStart, int ContentEnd, int End);

    public static IReadOnlyList<InlineRun> Parse(string text, int sourceStart, TextStyle baseStyle)
    {
        ArgumentNullException.ThrowIfNull(text);

        // O caminho comum é a linha sem notação nenhuma, e ele não paga nada: nem varredura de
        // trechos, nem lista, nem substring. Vai direto para quem já cuidava dela.
        if (!text.Contains(MathDelimiter) && !text.Contains(FootnoteOpen, StringComparison.Ordinal))
        {
            return InlineMarkup.Parse(text, sourceStart, baseStyle);
        }

        var runs = new List<InlineRun>();
        var plain = 0;
        var position = 0;

        while (position < text.Length)
        {
            if (!TryRead(text, position, out var span))
            {
                position++;
                continue;
            }

            AddPlain(runs, text, sourceStart, baseStyle, plain, span.Start);
            AddSpan(runs, text, sourceStart, baseStyle, span);

            position = span.End;
            plain = span.End;
        }

        AddPlain(runs, text, sourceStart, baseStyle, plain, text.Length);

        // Linha que só tinha delimitadores soltos não produziu trecho nenhum, e o texto inteiro já
        // saiu como comum — mas uma linha vazia ainda precisa do seu run, como sempre.
        return runs.Count > 0 ? runs : InlineMarkup.Parse(text, sourceStart, baseStyle);
    }

    private static bool TryRead(string text, int position, out Span span)
    {
        span = default;

        if (text[position] == MathDelimiter)
        {
            return TryReadMath(text, position, out span);
        }

        return text.AsSpan(position).StartsWith(FootnoteOpen, StringComparison.Ordinal)
            && TryReadFootnote(text, position, out span);
    }

    /// <remarks>
    /// O identificador não leva branco: é rótulo, não texto. Sem essa regra, um <c>[^</c> solto
    /// engoliria o resto do parágrafo até achar um <c>]</c> qualquer.
    /// </remarks>
    private static bool TryReadFootnote(string text, int position, out Span span)
    {
        span = default;

        var contentStart = position + FootnoteOpen.Length;
        var close = text.IndexOf(FootnoteClose, contentStart, StringComparison.Ordinal);

        if (close <= contentStart)
        {
            return false;
        }

        foreach (var current in text.AsSpan(contentStart, close - contentStart))
        {
            if (char.IsWhiteSpace(current))
            {
                return false;
            }
        }

        span = new Span(SpanKind.Footnote, position, contentStart, close, close + FootnoteClose.Length);

        return true;
    }

    /// <remarks>
    /// As mesmas duas condições de flanqueamento do negrito: delimitador seguido de branco não
    /// abre, e precedido de branco não fecha. É o que impede <c>"R$ 50 e R$ 70"</c> de virar uma
    /// fórmula — e um <c>$</c> sem par continua sendo o caractere que o autor digitou.
    /// </remarks>
    private static bool TryReadMath(string text, int position, out Span span)
    {
        span = default;

        var contentStart = position + 1;

        if (contentStart >= text.Length || char.IsWhiteSpace(text[contentStart]))
        {
            return false;
        }

        var close = text.IndexOf(MathDelimiter, contentStart);

        if (close < 0 || close == contentStart || char.IsWhiteSpace(text[close - 1]))
        {
            return false;
        }

        span = new Span(SpanKind.Math, position, contentStart, close, close + 1);

        return true;
    }

    private static void AddPlain(
        List<InlineRun> runs,
        string text,
        int sourceStart,
        TextStyle baseStyle,
        int start,
        int end)
    {
        if (end > start)
        {
            runs.AddRange(InlineMarkup.Parse(text[start..end], sourceStart + start, baseStyle));
        }
    }

    /// <remarks>
    /// Três runs, e o do meio é o que se vê quando a marcação está escondida: o identificador da
    /// nota, sobrescrito, e o corpo da fórmula, em itálico — que é como uma variável se compõe em
    /// texto matemático, e o que sai impresso. <b>Parsear e estilizar, não tipografar:</b> não há
    /// quem componha TeX aqui, e um compositor de fórmulas é uma fase inteira sozinho.
    /// </remarks>
    private static void AddSpan(
        List<InlineRun> runs,
        string text,
        int sourceStart,
        TextStyle baseStyle,
        Span span)
    {
        var content = span.Kind == SpanKind.Footnote
            ? baseStyle.AsSuperscript()
            : baseStyle with { Italic = true };

        Add(runs, text, sourceStart, baseStyle, span.Start, span.ContentStart, markup: true);
        Add(runs, text, sourceStart, content, span.ContentStart, span.ContentEnd, markup: false);
        Add(runs, text, sourceStart, baseStyle, span.ContentEnd, span.End, markup: true);
    }

    private static void Add(
        List<InlineRun> runs,
        string text,
        int sourceStart,
        TextStyle style,
        int start,
        int end,
        bool markup) =>
        runs.Add(new InlineRun(text[start..end], sourceStart + start, style, markup));
}
