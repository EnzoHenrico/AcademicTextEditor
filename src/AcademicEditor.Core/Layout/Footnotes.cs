using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// As notas de rodapé de um documento, já quebradas em linhas e prontas para o page breaker.
/// </summary>
/// <param name="Notes">
/// As linhas de cada nota chamada, por identificador. <c>null</c> num documento sem nota alguma —
/// que é o caminho que não paga nada.
/// </param>
/// <param name="RuleGapPt">A folga entre o texto e as notas, com o filete no meio dela.</param>
/// <param name="Placed">
/// Um bit por bloco: quais definições saíram do fluxo. Por <b>bloco</b> e não por identificador,
/// senão uma segunda definição com o mesmo rótulo sumiria da tela — e nenhum texto pode sumir.
/// </param>
public readonly record struct FootnoteLayout(
    IReadOnlyDictionary<string, IReadOnlyList<LaidOutLine>>? Notes,
    double RuleGapPt,
    bool[]? Placed)
{
    public bool IsPlaced(int blockIndex) => Placed is not null && Placed[blockIndex];
}

/// <summary>
/// Quebra as definições de nota antes de a paginação começar.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o que dispensa o laço de convergência.</b> A nota encolhe a altura útil da folha em que
/// cai, o que pode empurrar a própria chamada para a folha seguinte, que leva a nota junto — um
/// ponto fixo, se a nota fosse descoberta depois de a linha estar posta. Quebrando as definições
/// primeiro, a altura de cada uma é conhecida <i>antes</i>, e o page breaker decide com
/// antecedência: se a linha mais as notas que ela estreia não cabem no que resta, as duas descem.
/// </para>
/// <para>
/// <b>Só conta a chamada feita fora de uma definição.</b> Uma nota chamada apenas de dentro de
/// outra nota seria assentada na folha onde aquela foi desenhada, que é uma dependência circular
/// disfarçada. Aqui ela simplesmente continua sendo parágrafo comum, no lugar em que foi escrita.
/// </para>
/// </remarks>
internal static class Footnotes
{
    /// <param name="reuse">
    /// Dadas as linhas que um bloco produziu no layout anterior, devolve-as prontas para servirem
    /// de novo — ou <c>null</c> quando aquela definição tem de ser requebrada. <c>null</c> aqui é o
    /// caminho completo, que quebra todas.
    /// </param>
    public static FootnoteLayout Gather(
        DocumentNode document,
        PageSettings settings,
        ITextMeasurer measurer,
        int caretOffset,
        TypographyPreset typography,
        Func<int, IReadOnlyList<LaidOutLine>?>? reuse = null)
    {
        var called = CalledIds(document);

        if (called is null)
        {
            return default;
        }

        Dictionary<string, IReadOnlyList<LaidOutLine>>? notes = null;
        bool[]? placed = null;

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            if (document.Blocks[index] is not FootnoteNode note || !called.Contains(note.Id))
            {
                continue;
            }

            notes ??= [];

            // A primeira definição vence. A segunda com o mesmo rótulo não é assentada no pé — e
            // por isso continua no fluxo, visível onde foi escrita, em vez de sumir.
            if (notes.ContainsKey(note.Id))
            {
                continue;
            }

            notes[note.Id] = reuse?.Invoke(index) ?? Break(note, settings, measurer, caretOffset, typography);

            placed ??= new bool[document.Blocks.Count];
            placed[index] = true;
        }

        if (notes is null)
        {
            return default;
        }

        // A folga é uma linha de corpo: metade acima do filete, metade abaixo. Sai da mesma
        // métrica que a linha de texto usa, então acompanha o preset sem uma constante nova.
        var gapPt = typography.Apply(measurer.GetLineMetrics(typography.Body)).HeightPt;

        return new FootnoteLayout(notes, gapPt, placed);
    }

    private static IReadOnlyList<LaidOutLine> Break(
        FootnoteNode note,
        PageSettings settings,
        ITextMeasurer measurer,
        int caretOffset,
        TypographyPreset typography)
    {
        var reveals = caretOffset >= note.SourceStart
            && caretOffset <= note.SourceStart + note.SourceLength;

        var lines = LineBreaker.BreakIntoLines(
            note.Runs,
            settings.ContentWidthPt,
            measurer,
            reveals,
            typography,
            note.Alignment,
            note.FootnoteCalls);

        for (var line = 0; line < lines.Count; line++)
        {
            lines[line] = lines[line] with { Kind = LineKind.Footnote };
        }

        return lines;
    }

    /// <summary>Os identificadores chamados de fora de uma definição, ou <c>null</c> se não há.</summary>
    private static HashSet<string>? CalledIds(DocumentNode document)
    {
        HashSet<string>? called = null;

        foreach (var block in document.Blocks)
        {
            if (block.FootnoteCalls.Count == 0 || block is FootnoteNode)
            {
                continue;
            }

            called ??= [];

            foreach (var call in block.FootnoteCalls)
            {
                called.Add(call.Id);
            }
        }

        return called;
    }
}
