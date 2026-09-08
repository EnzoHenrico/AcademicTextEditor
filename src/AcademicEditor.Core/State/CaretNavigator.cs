using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.State;

/// <summary>
/// Move o caret sobre o documento paginado. Funções puras: recebem o caret e o layout, devolvem
/// um caret novo — nada de estado, nada de UI.
/// </summary>
/// <remarks>
/// <para>
/// Mora no Core, e não no controle, porque mover para cima ou para baixo exige consultar o
/// <see cref="PaginatedDocument"/>: a "linha" que interessa é a linha visual depois da quebra,
/// não o parágrafo da fonte. Aqui, isso é testável com um medidor determinístico; dentro do
/// controle, exigiria subsistema gráfico.
/// </para>
/// <para>
/// <b>A fronteira de uma quebra por largura é uma posição, não duas.</b> O espaço em que a linha
/// quebrou fica com a linha de cima, então o fim dela e o começo da seguinte são o mesmo offset.
/// Cada movimento diz de que lado quer parar (<see cref="CaretAffinity"/>) — sem isso End cairia
/// na linha de baixo e ← no começo de uma linha quebrada não moveria nada visível.
/// </para>
/// <para>
/// <b>O caret anda sobre o que está desenhado.</b> As posições válidas são a união dos trechos
/// que as linhas cobrem — a marcação de um heading e a linha em branco entre parágrafos ficam de
/// fora, e ← / → passam por cima delas. Uma tecla que não movesse nada visível seria uma tecla
/// que o autor apertou à toa.
/// </para>
/// </remarks>
public static class CaretNavigator
{
    /// <summary>Caret num offset, com a coluna alvo recalculada. Use após uma edição.</summary>
    public static Caret At(
        int offset,
        PaginatedDocument document,
        ITextMeasurer measurer,
        CaretAffinity affinity = CaretAffinity.Downstream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        var (page, line) = CaretGeometry.FindLine(offset, document, affinity);

        return page < 0
            ? new Caret(offset, 0.0, affinity)
            : new Caret(
                offset,
                CaretGeometry.ColumnPt(document.Pages[page].Lines[line], offset, measurer),
                affinity);
    }

    public static Caret MoveLeft(Caret caret, PaginatedDocument document, ITextMeasurer measurer)
    {
        var (page, line) = Locate(caret, document);

        if (page < 0)
        {
            return caret;
        }

        var current = document.Pages[page].Lines[line];

        if (caret.Offset > current.SourceStart)
        {
            return At(caret.Offset - StepBefore(current, caret.Offset), document, measurer);
        }

        var (previousPage, previousLine) = CaretGeometry.PreviousLine(document, page, line);

        // Upstream: numa quebra por largura este offset é o mesmo de onde o caret saiu, e é a
        // afinidade que o desenha no fim da linha de cima em vez de deixá-lo parado.
        return previousPage < 0
            ? caret
            : At(
                document.Pages[previousPage].Lines[previousLine].SourceEnd,
                document,
                measurer,
                CaretAffinity.Upstream);
    }

    public static Caret MoveRight(Caret caret, PaginatedDocument document, ITextMeasurer measurer)
    {
        var (page, line) = Locate(caret, document);

        if (page < 0)
        {
            return caret;
        }

        var current = document.Pages[page].Lines[line];

        if (caret.Offset < current.SourceEnd)
        {
            return At(caret.Offset + StepAfter(current, caret.Offset), document, measurer);
        }

        var (nextPage, nextLine) = CaretGeometry.NextLine(document, page, line);

        return nextPage < 0
            ? caret
            : At(document.Pages[nextPage].Lines[nextLine].SourceStart, document, measurer);
    }

    public static Caret MoveUp(Caret caret, PaginatedDocument document, ITextMeasurer measurer) =>
        MoveToAdjacentLine(caret, document, measurer, up: true);

    public static Caret MoveDown(Caret caret, PaginatedDocument document, ITextMeasurer measurer) =>
        MoveToAdjacentLine(caret, document, measurer, up: false);

    /// <summary>Home: início da <b>linha visual</b>, não do parágrafo.</summary>
    public static Caret MoveToLineStart(Caret caret, PaginatedDocument document, ITextMeasurer measurer)
    {
        var (page, line) = Locate(caret, document);

        return page < 0 ? caret : At(document.Pages[page].Lines[line].SourceStart, document, measurer);
    }

    /// <summary>End: fim da linha visual. Num parágrafo com wrap, para no limite da quebra.</summary>
    /// <remarks>
    /// Pousa <b>depois</b> do espaço em que a linha quebrou, e é a afinidade que o mantém desenhado
    /// nesta linha. Ficar antes do espaço parece igual na tela — o espaço é invisível no fim da
    /// linha — mas um Enter ali empurraria o espaço para a linha de baixo, que nasceria indentada.
    /// </remarks>
    public static Caret MoveToLineEnd(Caret caret, PaginatedDocument document, ITextMeasurer measurer)
    {
        var (page, line) = Locate(caret, document);

        return page < 0
            ? caret
            : At(document.Pages[page].Lines[line].SourceEnd, document, measurer, CaretAffinity.Upstream);
    }

    public static Caret MovePageUp(Caret caret, PaginatedDocument document, ITextMeasurer measurer) =>
        MoveToAdjacentPage(caret, document, measurer, up: true);

    public static Caret MovePageDown(Caret caret, PaginatedDocument document, ITextMeasurer measurer) =>
        MoveToAdjacentPage(caret, document, measurer, up: false);

    private static Caret MoveToAdjacentLine(
        Caret caret,
        PaginatedDocument document,
        ITextMeasurer measurer,
        bool up)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        var (page, line) = Locate(caret, document);

        if (page < 0)
        {
            return caret;
        }

        var (targetPage, targetLine) = up
            ? CaretGeometry.PreviousLine(document, page, line)
            : CaretGeometry.NextLine(document, page, line);

        // Sem linha para onde ir: o caret fica onde está, incluindo a coluna alvo. Mandá-lo para
        // o começo ou o fim do documento seria um salto que ninguém pediu.
        if (targetPage < 0)
        {
            return caret;
        }

        var target = document.Pages[targetPage].Lines[targetLine];

        // A coluna alvo é preservada, e é isso que faz ↑↓ atravessarem uma linha curta e voltarem
        // à coluna original em vez de grudarem no fim dela. A afinidade, não: ela descreve uma
        // fronteira da linha de onde o caret saiu, e carregá-la adiante desenharia o caret na
        // linha errada quando a coluna alvo calhasse de cair numa outra fronteira.
        return caret with
        {
            Offset = CaretGeometry.OffsetAtColumn(target, caret.DesiredColumnPt, measurer),
            Affinity = CaretAffinity.Downstream,
        };
    }

    private static Caret MoveToAdjacentPage(
        Caret caret,
        PaginatedDocument document,
        ITextMeasurer measurer,
        bool up)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        var (page, line) = Locate(caret, document);

        if (page < 0)
        {
            return caret;
        }

        // Mesma altura na folha vizinha. Se não houver folha vizinha com conteúdo, o caret vai
        // para o extremo da folha atual — a tecla sempre faz alguma coisa.
        var targetPage = page;

        for (var candidate = up ? page - 1 : page + 1;
             candidate >= 0 && candidate < document.Pages.Count;
             candidate += up ? -1 : 1)
        {
            if (document.Pages[candidate].Lines.Count > 0)
            {
                targetPage = candidate;
                break;
            }
        }

        var lines = document.Pages[targetPage].Lines;

        var targetLine = targetPage == page
            ? (up ? 0 : lines.Count - 1)
            : Math.Min(line, lines.Count - 1);

        return caret with
        {
            Offset = CaretGeometry.OffsetAtColumn(lines[targetLine], caret.DesiredColumnPt, measurer),
            Affinity = CaretAffinity.Downstream,
        };
    }

    private static (int PageIndex, int LineIndex) Locate(Caret caret, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return CaretGeometry.FindLine(caret.Offset, document, caret.Affinity);
    }

    // Um par substituto é um caractere só para quem escreveu. As setas atravessam os dois code
    // units de uma vez, senão metade das teclas não moveria o caret lugar nenhum visível.
    private static int StepAfter(LaidOutLine line, int offset)
    {
        foreach (var run in line.Runs)
        {
            var index = offset - run.SourceStart;

            if (index >= 0 && index < run.Text.Length && char.IsHighSurrogate(run.Text[index]))
            {
                return 2;
            }
        }

        return 1;
    }

    private static int StepBefore(LaidOutLine line, int offset)
    {
        foreach (var run in line.Runs)
        {
            var index = offset - run.SourceStart - 1;

            if (index > 0 && index < run.Text.Length && char.IsLowSurrogate(run.Text[index]))
            {
                return 2;
            }
        }

        return 1;
    }
}
