using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.State;

/// <summary>
/// Posição do caret na folha, em pontos, relativa ao canto da área de conteúdo da página.
/// </summary>
public readonly record struct CaretPosition(int PageIndex, double XPt, double YPt, double HeightPt);

/// <summary>
/// Converte entre offset no buffer e geometria na página, nos dois sentidos. É o que liga o que
/// o autor digitou ao pixel onde a barra vertical aparece.
/// </summary>
/// <remarks>
/// <para>
/// A ponte é o par <see cref="LaidOutLine.SourceStart"/>/<see cref="LaidOutRun.SourceStart"/>:
/// como o texto de um run corresponde caractere a caractere ao trecho que ele cobre na fonte,
/// achar a coluna é medir um prefixo desse texto — sem tabela auxiliar e sem varrer o documento.
/// </para>
/// <para>
/// Recebe o <see cref="ITextMeasurer"/> em vez de interpolar dentro do run. Numa fonte
/// proporcional, interpolar poria o caret visivelmente fora do lugar no meio de uma palavra;
/// medir o prefixo dá a posição exata. Continua sendo função pura, e continua testável sem UI.
/// </para>
/// </remarks>
public static class CaretGeometry
{
    public static CaretPosition Locate(
        int offset,
        PaginatedDocument document,
        ITextMeasurer measurer,
        CaretAffinity affinity = CaretAffinity.Downstream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        var (pageIndex, lineIndex) = FindLine(offset, document, affinity);

        // Documento sem nenhuma linha: só acontece quando o conteúdo inteiro é quebra de página.
        if (pageIndex < 0)
        {
            return new CaretPosition(0, 0.0, 0.0, 0.0);
        }

        var line = document.Pages[pageIndex].Lines[lineIndex];

        return new CaretPosition(pageIndex, ColumnPt(line, offset, measurer), line.YPt, line.HeightPt);
    }

    /// <summary>Distância do início da linha até <paramref name="offset"/>, em pontos.</summary>
    public static double ColumnPt(LaidOutLine line, int offset, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(measurer);

        foreach (var run in line.Runs)
        {
            // Offset num vão que a linha cobre mas nenhum run ocupa (a marcação de um heading,
            // por exemplo): o caret encosta no começo do run seguinte.
            if (offset < run.SourceStart)
            {
                return run.XPt;
            }

            if (offset <= run.SourceEnd)
            {
                return run.XPt + measurer.MeasureWidthPt(run.Text.AsSpan(0, offset - run.SourceStart), run.Style);
            }
        }

        return line.Runs.Count == 0 ? 0.0 : line.Runs[^1].XPt + line.Runs[^1].WidthPt;
    }

    /// <summary>Offset cuja coluna é a mais próxima de <paramref name="columnPt"/> nessa linha.</summary>
    public static int OffsetAtColumn(LaidOutLine line, double columnPt, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(measurer);

        if (line.Runs.Count == 0)
        {
            return line.SourceStart;
        }

        foreach (var run in line.Runs)
        {
            if (columnPt < run.XPt + run.WidthPt)
            {
                return OffsetInRun(run, columnPt, measurer);
            }
        }

        return line.Runs[^1].SourceEnd;
    }

    /// <summary>O offset é o fim de uma linha e o começo da seguinte?</summary>
    /// <remarks>
    /// Só acontece em quebra por largura: o espaço em que a linha quebrou fica com a linha de
    /// cima, então as duas compartilham o offset — uma posição no buffer, duas na tela. Numa
    /// quebra explícita o <c>\n</c> ocupa uma posição entre elas e não há empate.
    /// <para>
    /// É o que distingue a quebra que o autor escreveu da que a margem impôs, e por isso decide
    /// quantos <c>\n</c> um Enter precisa inserir ali para abrir uma linha em branco visível.
    /// </para>
    /// </remarks>
    public static bool IsSharedBoundary(int offset, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var position = FindLineIndex(offset, document);

        return position > 0
            && document.Index[position].SourceStart == offset
            && document.Index[position - 1].SourceEnd == offset;
    }

    /// <summary>
    /// Página e índice da linha que contém <paramref name="offset"/> — a última cujo início não
    /// passou dele, ou a anterior a ela quando a afinidade é
    /// <see cref="CaretAffinity.Upstream"/> e as duas dividem o offset.
    /// </summary>
    internal static (int PageIndex, int LineIndex) FindLine(
        int offset,
        PaginatedDocument document,
        CaretAffinity affinity = CaretAffinity.Downstream)
    {
        var position = FindLineIndex(offset, document);

        if (position < 0)
        {
            return (-1, -1);
        }

        var found = document.Index[Resolve(position, offset, document, affinity)];

        return (found.PageIndex, found.LineIndex);
    }

    /// <summary>
    /// Posição no <see cref="PaginatedDocument.Index"/> da linha que contém o offset, ou -1 num
    /// documento sem linha alguma.
    /// </summary>
    /// <remarks>
    /// <b>Busca binária sobre o índice em ordem de fonte</b>, e não mais varredura das folhas. A
    /// varredura custava proporcional ao que existia antes do caret e dependia de as linhas
    /// estarem em ordem de fonte dentro das folhas — premissa que a nota de rodapé quebra, porque
    /// ela é desenhada na folha da chamada e escrita onde o autor quis.
    /// </remarks>
    internal static int FindLineIndex(int offset, PaginatedDocument document)
    {
        var index = document.Index;

        if (index.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = index.Count - 1;
        var best = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (index[mid].SourceStart <= offset)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Offset antes da primeira linha do documento — o "# " de um heading inicial, com a
        // marcação escondida. O caret pousa nessa primeira linha.
        return best < 0 ? 0 : best;
    }

    // A linha achada começa exatamente onde a anterior terminou? Então o offset serve às duas, e
    // quem escolhe é a afinidade. Só acontece em quebra por largura: numa quebra explícita o \n
    // ocupa uma posição entre elas e não há empate.
    private static int Resolve(
        int position,
        int offset,
        PaginatedDocument document,
        CaretAffinity affinity) =>
        affinity == CaretAffinity.Upstream
        && position > 0
        && document.Index[position].SourceStart == offset
        && document.Index[position - 1].SourceEnd == offset
            ? position - 1
            : position;

    internal static (int PageIndex, int LineIndex) PreviousLine(PaginatedDocument document, int page, int line)
    {
        if (line > 0)
        {
            return (page, line - 1);
        }

        // Páginas vazias existem — uma quebra explícita dupla produz uma folha em branco — e a
        // navegação passa por cima delas em vez de parar numa página sem onde pousar.
        for (var candidate = page - 1; candidate >= 0; candidate--)
        {
            if (document.Pages[candidate].Lines.Count > 0)
            {
                return (candidate, document.Pages[candidate].Lines.Count - 1);
            }
        }

        return (-1, -1);
    }

    internal static (int PageIndex, int LineIndex) NextLine(PaginatedDocument document, int page, int line)
    {
        if (line + 1 < document.Pages[page].Lines.Count)
        {
            return (page, line + 1);
        }

        for (var candidate = page + 1; candidate < document.Pages.Count; candidate++)
        {
            if (document.Pages[candidate].Lines.Count > 0)
            {
                return (candidate, 0);
            }
        }

        return (-1, -1);
    }

    private static int OffsetInRun(LaidOutRun run, double columnPt, ITextMeasurer measurer)
    {
        var target = columnPt - run.XPt;

        if (target <= 0.0)
        {
            return run.SourceStart;
        }

        // Maior prefixo que ainda não passa do alvo. A largura cresce monotonicamente com o
        // prefixo, então bastam O(log n) medições.
        var low = 0;
        var high = run.Text.Length;
        var best = 0;
        var bestWidth = 0.0;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var width = measurer.MeasureWidthPt(run.Text.AsSpan(0, mid), run.Style);

            if (width <= target)
            {
                best = mid;
                bestWidth = width;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        // O caret pousa na fronteira mais próxima, não na anterior: clicar na metade direita de
        // um caractere põe o caret depois dele, que é o que a mão espera.
        if (best < run.Text.Length)
        {
            var next = best + (char.IsHighSurrogate(run.Text[best]) ? 2 : 1);
            var nextWidth = measurer.MeasureWidthPt(run.Text.AsSpan(0, next), run.Style);

            if (nextWidth - target < target - bestWidth)
            {
                best = next;
            }
        }

        // Nunca entre as duas metades de um par substituto: ali não há fronteira de caractere.
        if (best > 0 && best < run.Text.Length && char.IsLowSurrogate(run.Text[best]))
        {
            best--;
        }

        return run.SourceStart + best;
    }
}
