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
/// A ponte é o par <see cref="LaidOutLine.SourceStart"/>/<see cref="LaidOutRun.LineOffset"/>:
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

        // Os runs são posicionados dentro da linha, então a comparação acontece toda em
        // coordenadas de linha — uma subtração aqui em vez de uma soma por run.
        var local = offset - line.SourceStart;

        for (var index = 0; index < line.Runs.Count; index++)
        {
            var run = line.Runs[index];

            // Offset num vão que a linha cobre mas nenhum run ocupa (a marcação de um heading,
            // por exemplo): o caret encosta no começo do run seguinte.
            if (local < run.LineOffset)
            {
                return run.XPt;
            }

            if (local > run.LineEnd)
            {
                continue;
            }

            // No fim de um run que encosta no seguinte, o caret pertence ao seguinte — é onde o
            // texto está desenhado. Numa linha justificada o run de branco carrega a sobra dentro
            // da própria largura, então terminar no run anterior deixaria o caret um vão atrás da
            // palavra que ele deveria preceder.
            if (local == run.LineEnd
                && index + 1 < line.Runs.Count
                && line.Runs[index + 1].LineOffset == local)
            {
                return line.Runs[index + 1].XPt;
            }

            return run.XPt + measurer.MeasureWidthPt(run.Text.AsSpan(0, local - run.LineOffset), run.Style);
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
                return line.SourceStart + OffsetInRun(run, columnPt, measurer);
            }
        }

        return line.SourceStart + line.Runs[^1].LineEnd;
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

        var position = FindIndex(offset, document);

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
        var position = FindIndex(offset, document);

        if (position < 0)
        {
            return (-1, -1);
        }

        var resolved = Resolve(position, offset, document, affinity);

        // Um offset do cabeçalho de metadados cai numa linha que não recebe caret, e devolvê-la
        // daria uma barra de altura zero — uma barra que sumiu da tela. Anda para a primeira que
        // recebe, que é a primeira linha do corpo. Quem edita já grampeia o caret no começo do
        // corpo; isto é a segunda tranca, para que nenhum caminho novo precise lembrar da primeira.
        while (resolved < document.Index.Count && !LineOf(document, resolved).AcceptsCaret)
        {
            resolved++;
        }

        if (resolved >= document.Index.Count)
        {
            return (-1, -1);
        }

        var found = document.Index[resolved];

        return (found.PageIndex, found.LineIndex);
    }

    /// <summary>
    /// Posição no <see cref="PaginatedDocument.Index"/> da linha que contém o offset, ou -1 num
    /// documento sem linha alguma.
    /// </summary>
    /// <remarks>
    /// <b>Busca binária sobre o índice em ordem de fonte</b>, e não varredura das folhas: aquela
    /// custava proporcional ao que existia antes do caret e — o que importa mais — dependia de as
    /// linhas estarem em ordem de fonte <i>dentro</i> das folhas. A conta mora no
    /// <see cref="PaginatedDocument"/>, que é onde o índice mora; aqui fica só a pergunta do caret.
    /// </remarks>
    private static int FindIndex(int offset, PaginatedDocument document) =>
        document.FindLineIndex(offset);

    private static LaidOutLine LineOf(PaginatedDocument document, int position)
    {
        var reference = document.Index[position];

        return document.Pages[reference.PageIndex].Lines[reference.LineIndex];
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

    /// <remarks>
    /// Duas coisas são puladas, e por motivos diferentes. <b>Página vazia</b> — que uma quebra
    /// explícita dupla produz — não tem onde pousar. <b>Linha que não aceita caret</b> — a entrada
    /// de sumário e o cabeçalho de metadados — ou não tem offset nenhum, ou não tem altura onde
    /// desenhar a barra. Parar em qualquer uma delas some com o caret da tela.
    /// </remarks>
    internal static (int PageIndex, int LineIndex) PreviousLine(PaginatedDocument document, int page, int line)
    {
        var candidatePage = page;
        var candidateLine = line - 1;

        while (candidatePage >= 0)
        {
            if (candidateLine < 0)
            {
                candidatePage--;
                candidateLine = candidatePage >= 0 ? document.Pages[candidatePage].Lines.Count - 1 : 0;
                continue;
            }

            if (document.Pages[candidatePage].Lines[candidateLine].AcceptsCaret)
            {
                return (candidatePage, candidateLine);
            }

            candidateLine--;
        }

        return (-1, -1);
    }

    /// <inheritdoc cref="PreviousLine"/>
    internal static (int PageIndex, int LineIndex) NextLine(PaginatedDocument document, int page, int line)
    {
        var candidatePage = page;
        var candidateLine = line + 1;

        while (candidatePage < document.Pages.Count)
        {
            if (candidateLine >= document.Pages[candidatePage].Lines.Count)
            {
                candidatePage++;
                candidateLine = 0;
                continue;
            }

            if (document.Pages[candidatePage].Lines[candidateLine].AcceptsCaret)
            {
                return (candidatePage, candidateLine);
            }

            candidateLine++;
        }

        return (-1, -1);
    }

    /// <summary>A posição, <b>dentro da linha</b>, mais próxima da coluna pedida.</summary>
    private static int OffsetInRun(LaidOutRun run, double columnPt, ITextMeasurer measurer)
    {
        var target = columnPt - run.XPt;

        if (target <= 0.0)
        {
            return run.LineOffset;
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

        return run.LineOffset + best;
    }
}
