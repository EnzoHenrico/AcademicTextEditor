using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.State;

/// <summary>
/// Um retângulo de destaque, em pontos, relativo ao canto da área de conteúdo da página.
/// </summary>
/// <remarks>Mesma convenção do <see cref="CaretPosition"/> — quem desenha um, desenha o outro.</remarks>
public readonly record struct SelectionRect(
    int PageIndex,
    double XPt,
    double YPt,
    double WidthPt,
    double HeightPt);

/// <summary>
/// Onde o destaque da seleção é pintado: um retângulo por linha visual que o trecho cruza.
/// </summary>
/// <remarks>
/// <para>
/// Mora no Core pelo mesmo motivo do <see cref="CaretGeometry"/>: só o layout sabe onde as linhas
/// caíram, a função é pura sobre <c>(Selection, PaginatedDocument, ITextMeasurer)</c>, e um
/// exportador PDF a herda junto com o motor.
/// </para>
/// <para>
/// <b>As duas pontas resolvem a fronteira compartilhada para dentro do trecho:</b> o começo é
/// <see cref="CaretAffinity.Downstream"/> e o fim é <see cref="CaretAffinity.Upstream"/>. Não é
/// heurística — é o que faz o destaque sair contíguo. Numa quebra por largura o fim de uma linha
/// e o começo da seguinte são o mesmo offset; resolver ao contrário produziria um retângulo de
/// largura zero pendurado numa ponta.
/// </para>
/// </remarks>
public static class SelectionGeometry
{
    public static IReadOnlyList<SelectionRect> RectsFor(
        Selection selection,
        PaginatedDocument document,
        ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        if (selection.IsEmpty)
        {
            return [];
        }

        var range = selection.Range;
        var (page, line) = CaretGeometry.FindLine(range.Start, document, CaretAffinity.Downstream);
        var (lastPage, lastLine) = CaretGeometry.FindLine(range.End, document, CaretAffinity.Upstream);

        if (page < 0 || lastPage < 0)
        {
            return [];
        }

        var rects = new List<SelectionRect>();

        while (page >= 0 && (page < lastPage || (page == lastPage && line <= lastLine)))
        {
            if (RectFor(document.Pages[page].Lines[line], page, range, measurer) is { } rect)
            {
                rects.Add(rect);
            }

            (page, line) = CaretGeometry.NextLine(document, page, line);
        }

        return rects;
    }

    private static SelectionRect? RectFor(
        LaidOutLine line,
        int pageIndex,
        TextRange range,
        ITextMeasurer measurer)
    {
        var start = Math.Max(range.Start, line.SourceStart);
        var end = Math.Min(range.End, line.SourceEnd);

        // Linhas inteiramente dentro do trecho não medem nada: a esquerda é a origem da linha e a
        // direita é a tinta que já está posicionada. Num Ctrl+A de 300 páginas, é a diferença
        // entre dezesseis mil medições e nenhuma.
        var leftPt = start <= line.SourceStart ? 0.0 : CaretGeometry.ColumnPt(line, start, measurer);
        var rightPt = end >= line.SourceEnd ? InkEndPt(line) : CaretGeometry.ColumnPt(line, end, measurer);

        if (rightPt > leftPt)
        {
            return new SelectionRect(pageIndex, leftPt, line.YPt, rightPt - leftPt, line.HeightPt);
        }

        // Largura zero. Se a quebra de linha está dentro do trecho, o que se selecionou foi ela —
        // e uma linha em branco sem destaque nenhum pareceria um buraco no meio da seleção. Uma
        // lasca da largura de um espaço é o que diz "esta também está aqui".
        //
        // Só a quebra vale: quando o trecho apenas termina no começo desta linha, nada dela entrou.
        return range.End > line.SourceEnd
            ? new SelectionRect(
                pageIndex,
                leftPt,
                line.YPt,
                measurer.MeasureWidthPt(" ", TextStyle.Body),
                line.HeightPt)
            : null;
    }

    /// <summary>Onde a tinta da linha termina. Sem medir: os runs já estão posicionados.</summary>
    private static double InkEndPt(LaidOutLine line) =>
        line.Runs.Count == 0 ? 0.0 : line.Runs[^1].XPt + line.Runs[^1].WidthPt;
}
