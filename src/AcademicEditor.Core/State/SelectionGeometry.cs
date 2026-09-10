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

        // Percorre o ÍNDICE, em ordem de fonte: um trecho selecionado é um intervalo de texto, e a
        // linha seguinte dele é a seguinte no arquivo — não a de baixo na folha. Enquanto as duas
        // ordens coincidiam, comparar (folha, linha) dava no mesmo; com a nota de rodapé desenhada
        // na folha da chamada, a comparação passaria a saltar ou repetir linhas.
        var from = CaretGeometry.FindPosition(range.Start, document, CaretAffinity.Downstream);
        var to = CaretGeometry.FindPosition(range.End, document, CaretAffinity.Upstream);

        if (from < 0 || to < 0)
        {
            return [];
        }

        var rects = new List<SelectionRect>();

        for (var position = from; position <= to; position++)
        {
            var found = document.Index[position];

            if (RectFor(
                    document.Pages[found.PageIndex].Lines[found.LineIndex],
                    found.PageIndex,
                    range,
                    measurer,
                    document.Typography.Body)
                is { } rect)
            {
                rects.Add(rect);
            }
        }

        return rects;
    }

    /// <param name="body">
    /// O corpo de texto do documento, para a lasca da linha em branco. Vem do preset que paginou —
    /// medir um espaço num estilo que não é o do documento daria uma lasca de outra largura.
    /// </param>
    private static SelectionRect? RectFor(
        LaidOutLine line,
        int pageIndex,
        TextRange range,
        ITextMeasurer measurer,
        TextStyle body)
    {
        var start = Math.Max(range.Start, line.SourceStart);
        var end = Math.Min(range.End, line.SourceEnd);

        // Linhas inteiramente dentro do trecho não medem nada: a esquerda é a origem da linha e a
        // direita é a tinta que já está posicionada. Num Ctrl+A de 300 páginas, é a diferença
        // entre dezesseis mil medições e nenhuma.
        //
        // "Origem da linha" é onde o primeiro run começa, e não zero: numa linha centralizada ou
        // alinhada à direita o texto não encosta na margem esquerda, e um destaque que começasse
        // ali marcaria papel em branco antes da primeira letra.
        // À direita é a extensão CRUA, e não a tinta: o branco pendurado na margem faz parte do
        // trecho selecionado, e um destaque que parasse antes dele diria que ele não está lá. Com
        // a tolerância valendo para todo alinhamento, isso nunca passa da margem por mais de um
        // branco — antes, numa linha justificada, passava pelo grupo inteiro.
        var leftPt = start <= line.SourceStart ? StartPt(line) : CaretGeometry.ColumnPt(line, start, measurer);
        var rightPt = end >= line.SourceEnd ? LineExtents.ExtentPt(line) : CaretGeometry.ColumnPt(line, end, measurer);

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
                measurer.MeasureWidthPt(" ", body),
                line.HeightPt)
            : null;
    }

    /// <summary>Onde a linha começa. Sem medir: o primeiro run já está posicionado.</summary>
    private static double StartPt(LaidOutLine line) => line.Runs.Count == 0 ? 0.0 : line.Runs[0].XPt;

}
