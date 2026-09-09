using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Parsing;

/// <summary>
/// Negrito e itálico dentro de uma linha: <c>**assim**</c> e <c>*assim*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nada muda fora daqui.</b> <see cref="TextStyle"/> já tinha <c>Weight</c> e <c>Italic</c>, o
/// <c>AvaloniaTextMeasurer</c> já traduzia os dois, o <c>LineBreaker</c> já quebra sobre a
/// sequência de runs e já descarta os de marcação, e o <c>LayoutEngine</c> já revela a marcação do
/// bloco onde o caret está. Era a promessa que o line breaker fazia desde o MVP — <i>"o motor já
/// está pronto para <c>**negrito**</c> no meio da frase"</i> — e o que faltava era o parser.
/// </para>
/// <para>
/// <b>A invariante do <see cref="InlineRun"/> continua de pé:</b> todo caractere da linha entra em
/// exatamente um run, na ordem, cópia literal. É ela que mantém o caret funcionando, e é por isso
/// que a marcação vira run em vez de sumir do texto.
/// </para>
/// <para>
/// <b><c>_</c> não é marcação.</b> Uma regra em vez de duas, e <c>x_1</c> numa fórmula não vira
/// itálico. Ênfase também não atravessa linha, e isso sai de graça: aqui um bloco <i>é</i> uma
/// linha da fonte.
/// </para>
/// </remarks>
public static class InlineMarkup
{
    private const char Delimiter = '*';

    /// <summary>Maior sequência de <c>*</c> que ainda é marcação: <c>***</c> é negrito e itálico.</summary>
    private const int MaxDelimiterLength = 3;

    private enum Role
    {
        /// <summary>Sem par: o autor escreveu asteriscos, e asteriscos é o que ele vê.</summary>
        Literal,
        Open,
        Close,
    }

    public static IReadOnlyList<InlineRun> Parse(string text, int sourceStart, TextStyle baseStyle)
    {
        ArgumentNullException.ThrowIfNull(text);

        var markers = FindDelimiters(text);

        Pair(text, markers);

        return markers.Any(marker => marker.Role != Role.Literal)
            ? Build(text, sourceStart, baseStyle, markers)
            : [new InlineRun(text, sourceStart, baseStyle)];
    }

    /// <summary>
    /// As sequências maximais de <c>*</c> que podem ser marcação.
    /// </summary>
    /// <remarks>
    /// Quatro ou mais asteriscos seguidos não são delimitador de nada, e viram texto: é o que
    /// resolve <c>****</c> sem caso especial — pareá-lo daria uma ênfase vazia, que some da tela e
    /// deixa o autor sem entender para onde foi o que digitou.
    /// </remarks>
    private static List<Marker> FindDelimiters(string text)
    {
        var markers = new List<Marker>();
        var position = 0;

        while (position < text.Length)
        {
            if (text[position] != Delimiter)
            {
                position++;
                continue;
            }

            var end = position;

            while (end < text.Length && text[end] == Delimiter)
            {
                end++;
            }

            if (end - position <= MaxDelimiterLength)
            {
                markers.Add(new Marker(position, end - position));
            }

            position = end;
        }

        return markers;
    }

    /// <summary>Casa aberturas com fechamentos, e deixa literal o que não casou.</summary>
    /// <remarks>
    /// Pilha com busca do topo para baixo: um fechamento procura a abertura do mesmo comprimento
    /// mais próxima, e o que ficou acima dela nunca abriu nada — <c>**a *b**</c> fecha o negrito e
    /// devolve o asterisco solto ao texto. O que sobra na pilha no fim é literal, pelo mesmo
    /// motivo. Isso também garante que os pares saiam <b>aninhados</b>, que é o que permite ao
    /// estilo ser uma pilha na montagem.
    /// </remarks>
    private static void Pair(string text, List<Marker> markers)
    {
        var open = new List<int>();

        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index];

            if (CanClose(text, marker) && TryClose(markers, open, marker))
            {
                marker.Role = Role.Close;
                continue;
            }

            if (CanOpen(text, marker))
            {
                open.Add(index);
            }
        }
    }

    private static bool TryClose(List<Marker> markers, List<int> open, Marker marker)
    {
        for (var candidate = open.Count - 1; candidate >= 0; candidate--)
        {
            var opener = markers[open[candidate]];

            // Ênfase vazia não é ênfase: '** **' tem um espaço dentro e vale, '****' não chega
            // aqui, e um par colado seria marcação que desaparece sem deixar nada no lugar.
            if (opener.Length != marker.Length || marker.Start <= opener.Start + opener.Length)
            {
                continue;
            }

            opener.Role = Role.Open;
            open.RemoveRange(candidate, open.Count - candidate);

            return true;
        }

        return false;
    }

    // Um delimitador seguido de branco não abre nada, e um precedido de branco não fecha nada. São
    // as duas condições que impedem 'a * b * c' — asterisco usado como multiplicação — de virar
    // itálico, e são o miolo da regra de flanqueamento do CommonMark sem o resto dela.
    private static bool CanOpen(string text, Marker marker) =>
        marker.End < text.Length && !char.IsWhiteSpace(text[marker.End]);

    private static bool CanClose(string text, Marker marker) =>
        marker.Start > 0 && !char.IsWhiteSpace(text[marker.Start - 1]);

    private static List<InlineRun> Build(
        string text,
        int sourceStart,
        TextStyle baseStyle,
        List<Marker> markers)
    {
        var runs = new List<InlineRun>();
        var enclosing = new Stack<TextStyle>();
        var style = baseStyle;
        var position = 0;

        foreach (var marker in markers)
        {
            if (marker.Role == Role.Literal)
            {
                continue;
            }

            if (marker.Start > position)
            {
                runs.Add(new InlineRun(text[position..marker.Start], sourceStart + position, style));
            }

            // A marcação sai com o MESMO estilo do texto que ela envolve — a abertura já dentro,
            // o fechamento ainda dentro. É a decisão do '## ' do heading, pelo mesmo motivo:
            // revelar muda a largura da linha, nunca a altura dela.
            if (marker.Role == Role.Open)
            {
                enclosing.Push(style);
                style = Apply(style, marker.Length);
            }

            runs.Add(new InlineRun(
                text.Substring(marker.Start, marker.Length),
                sourceStart + marker.Start,
                style,
                IsMarkup: true));

            if (marker.Role == Role.Close)
            {
                // Volta ao estilo de fora, e não a "normal": dentro de um título, fechar um
                // **negrito** não pode tirar o negrito do resto do título.
                style = enclosing.Pop();
            }

            position = marker.End;
        }

        if (position < text.Length || runs.Count == 0)
        {
            runs.Add(new InlineRun(text[position..], sourceStart + position, style));
        }

        return runs;
    }

    private static TextStyle Apply(TextStyle style, int length) => length switch
    {
        1 => style with { Italic = true },
        2 => style with { Weight = FontWeightKind.Bold },
        _ => style with { Italic = true, Weight = FontWeightKind.Bold },
    };

    private sealed class Marker(int start, int length)
    {
        public int Start { get; } = start;

        public int Length { get; } = length;

        public int End => Start + Length;

        public Role Role { get; set; } = Role.Literal;
    }
}
