using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.State;

/// <summary>A troca de marcação de alinhamento no início de uma linha.</summary>
/// <param name="Length">Quanto da marcação atual sai; zero quando a linha não tem nenhuma.</param>
/// <param name="Replacement">A marcação que entra; vazia quando o alvo é o padrão da norma.</param>
public readonly record struct AlignmentEdit(int Start, int Length, string Replacement)
{
    public int LengthDelta => Replacement.Length - Length;
}

/// <summary>
/// O rodízio de alinhamento do <c>Ctrl+J</c>: quais linhas ele toca e o que troca em cada uma.
/// </summary>
/// <remarks>
/// <para>
/// <b>Alinhar é editar texto</b>, e é o que faz esta feature caber no motor sem nada novo: a
/// marcação vai para o arquivo, o undo sai de graça porque é edição como outra qualquer, o reflow
/// incremental já trata, e ela é revelada na linha do caret como o <c>## </c> de um título.
/// </para>
/// <para>
/// <b>Alinhamento é propriedade de bloco</b>, não de trecho: centralizar meio parágrafo não quer
/// dizer nada. Por isso a troca vale para toda linha que a seleção cruza, inteira — e o estado de
/// destino sai da <b>primeira</b> delas, para que uma seleção com alinhamentos mistos convirja
/// para um só em vez de cada linha seguir o seu próprio rodízio.
/// </para>
/// <para>
/// Mora no Core, e não no ViewModel, pelo mesmo motivo do <see cref="BlockMarkers"/> e do
/// <see cref="LineBreaks"/>: é regra, e regra precisa de teste.
/// </para>
/// </remarks>
public static class BlockAlignment
{
    /// <summary>
    /// Os quatro estados do rodízio, na ordem em que o <c>Ctrl+J</c> os percorre.
    /// </summary>
    /// <remarks>
    /// O primeiro é <b>sem marcação</b>, que é o padrão da norma — justificado na ABNT. É por isso
    /// que não existe marcação para justificado: ela serviria para pedir o que já está valendo.
    /// </remarks>
    private static readonly string[] Markers =
    [
        string.Empty,
        MarkupTokenizer.LeftMarker + " ",
        MarkupTokenizer.CenterMarker + " ",
        MarkupTokenizer.RightMarker + " ",
    ];

    /// <summary>
    /// As trocas que avançam o alinhamento das linhas que <paramref name="range"/> cruza.
    /// </summary>
    /// <remarks>
    /// Devolvidas <b>de trás para a frente</b>, que é a ordem em que precisam ser aplicadas: assim
    /// cada troca acontece num texto que as anteriores ainda não deslocaram, e nenhum offset
    /// precisa ser recalculado no meio do caminho.
    /// </remarks>
    public static IReadOnlyList<AlignmentEdit> Next(string source, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length == 0)
        {
            return [];
        }

        var start = Math.Clamp(range.Start, 0, source.Length);
        var end = Math.Clamp(range.End, start, source.Length);

        // Seleção que termina exatamente no começo de uma linha não pegou nada dela — é a regra de
        // qualquer editor, e sem ela um Ctrl+A alinharia uma linha a mais do que o autor destacou.
        var lastTouched = end > start && source[end - 1] == '\n' ? end - 1 : end;

        var edits = new List<AlignmentEdit>();
        var target = -1;

        for (var lineStart = LineStart(source, start); ; )
        {
            var lineEnd = LineEnd(source, lineStart);
            var line = source.AsSpan(lineStart, lineEnd - lineStart);

            // Linha em branco e quebra de página ficam de fora: pôr marcação numa faz dela texto, e
            // na outra desfaz a quebra. Nenhuma das duas é o que o autor pediu ao apertar Ctrl+J.
            if (!IsSkipped(line))
            {
                var current = MarkupTokenizer.ReadAlignment(line, out var alignment);

                if (target < 0)
                {
                    target = (IndexOf(alignment) + 1) % Markers.Length;
                }

                var replacement = Markers[target];

                // Linha que já está no destino não vira edição: apagar e reinserir o mesmo texto
                // encheria o undo de passos que não mudam nada.
                if (current != replacement.Length || !line.StartsWith(replacement, StringComparison.Ordinal))
                {
                    edits.Add(new AlignmentEdit(lineStart, current, replacement));
                }
            }

            if (lineEnd >= lastTouched || lineEnd >= source.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        edits.Reverse();

        return edits;
    }

    /// <summary>Onde um offset vai parar depois das trocas.</summary>
    /// <remarks>
    /// É o que mantém caret e âncora sobre o mesmo texto: as marcações mudam de tamanho, e sem
    /// isto a seleção escorregaria alguns caracteres a cada Ctrl+J. Um offset que estava
    /// <i>dentro</i> da marcação trocada vai para o fim da nova — ele apontava para um texto que
    /// deixou de existir.
    /// </remarks>
    public static int Reposition(int offset, IReadOnlyList<AlignmentEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var result = offset;

        foreach (var edit in edits)
        {
            if (result >= edit.Start + edit.Length)
            {
                result += edit.LengthDelta;
            }
            else if (result > edit.Start)
            {
                result = edit.Start + edit.Replacement.Length;
            }
        }

        return result;
    }

    private static bool IsSkipped(ReadOnlySpan<char> line) =>
        line.IsWhiteSpace() || BlockTags.IsMarker(line);

    private static int IndexOf(TextAlignment? alignment) => alignment switch
    {
        TextAlignment.Left => 1,
        TextAlignment.Center => 2,
        TextAlignment.Right => 3,
        _ => 0,
    };

    private static int LineStart(string source, int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        // A busca começa em offset-1: um caret parado sobre o '\n' que fecha a linha ainda pertence
        // a ela, e procurar a partir dele devolveria o começo da linha seguinte.
        var index = source.LastIndexOf('\n', Math.Min(offset, source.Length) - 1);

        return index < 0 ? 0 : index + 1;
    }

    private static int LineEnd(string source, int offset)
    {
        var index = source.IndexOf('\n', Math.Min(offset, source.Length));

        return index < 0 ? source.Length : index;
    }
}
