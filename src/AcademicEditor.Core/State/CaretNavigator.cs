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

    /// <summary>Caret no ponto onde o autor clicou.</summary>
    /// <remarks>
    /// <para>
    /// O caminho que faltava: todo movimento daqui parte de um caret e chega a outro, e não havia
    /// como <b>entrar</b> por um ponto da folha. É o inverso exato de
    /// <see cref="CaretGeometry.Locate"/>, e recebe o ponto na mesma convenção que aquele devolve
    /// — pontos, relativos ao canto da área de conteúdo da página. A conversão de DIP para ponto
    /// é da camada de renderização, e é lá que ela fica.
    /// </para>
    /// <para>
    /// <b>Todo clique pousa em algum lugar.</b> Acima da primeira linha e abaixo da última, nos
    /// extremos da folha; numa folha em branco — que uma quebra de página dupla produz —, na
    /// vizinha com conteúdo. Um clique que não movesse o caret pareceria o editor ignorando o
    /// mouse.
    /// </para>
    /// </remarks>
    /// <param name="xPt">Distância da margem esquerda da área de conteúdo.</param>
    /// <param name="yPt">Distância do topo da área de conteúdo.</param>
    public static Caret AtPoint(
        int pageIndex,
        double xPt,
        double yPt,
        PaginatedDocument document,
        ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        var (page, line) = LineAtPoint(pageIndex, yPt, document);

        if (page < 0)
        {
            // Documento sem linha alguma: só acontece quando o conteúdo inteiro é quebra de
            // página. Não há onde pousar, e o caret fica onde o documento começa.
            return new Caret(0, 0.0);
        }

        var target = document.Pages[page].Lines[line];

        // Um marcador de bloco é indivisível — a mesma regra que o Stop() aplica às setas. Clicar
        // no meio do filete tracejado não descreve nada que o autor possa editar.
        var offset = target.Kind == LineKind.Text
            ? CaretGeometry.OffsetAtColumn(target, xPt, measurer)
            : target.SourceStart;

        return new Caret(
            offset,
            CaretGeometry.ColumnPt(target, offset, measurer),
            AffinityFor(document, page, line, offset));
    }

    /// <summary>A linha que o ponto atinge, grampeada nos extremos da folha.</summary>
    private static (int PageIndex, int LineIndex) LineAtPoint(
        int pageIndex,
        double yPt,
        PaginatedDocument document)
    {
        var page = Math.Clamp(pageIndex, 0, document.Pages.Count - 1);
        var lines = document.Pages[page].Lines;

        if (lines.Count == 0)
        {
            // Folha em branco. Para trás primeiro: o caret pertence ao texto que a quebra
            // encerrou, não ao que ainda não começou.
            var previous = CaretGeometry.PreviousLine(document, page, 0);

            return previous.PageIndex >= 0 ? previous : CaretGeometry.NextLine(document, page, -1);
        }

        for (var index = 0; index < lines.Count; index++)
        {
            if (yPt < lines[index].YPt + lines[index].HeightPt)
            {
                return Land(document, page, index);
            }
        }

        // Abaixo da última linha: a última. Acima da primeira o laço já devolve a primeira,
        // porque o topo dela é o topo da área de conteúdo.
        return Land(document, page, lines.Count - 1);
    }

    /// <summary>
    /// A linha onde o caret de fato pode pousar, partindo da que o ponto atingiu.
    /// </summary>
    /// <remarks>
    /// Clicar numa entrada de sumário atinge uma linha <b>sem offset nenhum</b>. Para trás
    /// primeiro, pela mesma razão da folha em branco: o caret pertence ao texto que veio antes, e
    /// antes de toda entrada está o marcador <c>\toc</c>, que é o que o autor tem para editar. Se
    /// não houver nada atrás — é o caso do cabeçalho de metadados, que é a primeira linha do
    /// documento —, para a frente.
    /// </remarks>
    private static (int PageIndex, int LineIndex) Land(PaginatedDocument document, int page, int line)
    {
        if (document.Pages[page].Lines[line].AcceptsCaret)
        {
            return (page, line);
        }

        var previous = CaretGeometry.PreviousLine(document, page, line);

        return previous.PageIndex >= 0 ? previous : CaretGeometry.NextLine(document, page, line);
    }

    public static Caret MoveLeft(Caret caret, PaginatedDocument document, ITextMeasurer measurer)
    {
        var (page, line) = Locate(caret, document);

        if (page < 0)
        {
            return caret;
        }

        var current = document.Pages[page].Lines[line];

        if (current.Kind == LineKind.Text && caret.Offset > current.SourceStart)
        {
            return At(caret.Offset - StepBefore(current, caret.Offset), document, measurer);
        }

        var (previousPage, previousLine) = CaretGeometry.PreviousLine(document, page, line);

        if (previousPage < 0)
        {
            return caret;
        }

        // Numa quebra por largura este offset é o mesmo de onde o caret saiu, e é a afinidade que
        // o desenha no fim da linha de cima em vez de deixá-lo parado.
        var previous = document.Pages[previousPage].Lines[previousLine];
        var end = Stop(previous, atEnd: true);

        return At(end, document, measurer, AffinityFor(document, previousPage, previousLine, end));
    }

    public static Caret MoveRight(Caret caret, PaginatedDocument document, ITextMeasurer measurer)
    {
        var (page, line) = Locate(caret, document);

        if (page < 0)
        {
            return caret;
        }

        var current = document.Pages[page].Lines[line];

        if (current.Kind == LineKind.Text && caret.Offset < current.SourceEnd)
        {
            return At(caret.Offset + StepAfter(current, caret.Offset), document, measurer);
        }

        var (nextPage, nextLine) = CaretGeometry.NextLine(document, page, line);

        return nextPage < 0
            ? caret
            : At(Stop(document.Pages[nextPage].Lines[nextLine], atEnd: false), document, measurer);
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

        if (page < 0)
        {
            return caret;
        }

        var end = Stop(document.Pages[page].Lines[line], atEnd: true);

        return At(end, document, measurer, AffinityFor(document, page, line, end));
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

        // Na borda do documento a tecla não pode ficar sem efeito: ↑ na primeira linha vai para o
        // começo dela, ↓ na última para o fim. É o que o macOS faz, e é melhor que congelar — a
        // tecla morta parece o editor travado.
        if (targetPage < 0)
        {
            return up
                ? MoveToLineStart(caret, document, measurer)
                : MoveToLineEnd(caret, document, measurer);
        }

        var target = document.Pages[targetPage].Lines[targetLine];
        var offset = CaretGeometry.OffsetAtColumn(target, caret.DesiredColumnPt, measurer);

        // A coluna alvo é preservada, e é isso que faz ↑↓ atravessarem uma linha curta e voltarem
        // à coluna original em vez de grudarem no fim dela. A afinidade, não: ela descreve uma
        // fronteira da linha de onde o caret saiu. Escolhe-se a que resolve para a linha de
        // destino — do contrário, cair no fim dela (o que End torna provável, porque a coluna alvo
        // vira a largura cheia) desenharia o caret na linha seguinte, e ↑ pareceria não subir.
        return caret with
        {
            Offset = offset,
            Affinity = AffinityFor(document, targetPage, targetLine, offset),
        };
    }

    /// <summary>
    /// A afinidade que faz <paramref name="offset"/> resolver para esta linha.
    /// </summary>
    /// <remarks>
    /// <c>Upstream</c> só quando ela significa alguma coisa: o offset é o fim desta linha <b>e</b> o
    /// começo da seguinte, que é o que acontece numa quebra por largura. Reivindicá-la numa linha
    /// comum não mudaria onde o caret é desenhado, mas deixaria dois carets iguais diferentes para
    /// o record — e cada tecla viraria um redesenho a mais.
    /// </remarks>
    private static CaretAffinity AffinityFor(PaginatedDocument document, int page, int line, int offset)
    {
        if (offset != document.Pages[page].Lines[line].SourceEnd)
        {
            return CaretAffinity.Downstream;
        }

        var (nextPage, nextLine) = CaretGeometry.NextLine(document, page, line);

        return nextPage >= 0 && document.Pages[nextPage].Lines[nextLine].SourceStart == offset
            ? CaretAffinity.Upstream
            : CaretAffinity.Downstream;
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

        var offset = CaretGeometry.OffsetAtColumn(lines[targetLine], caret.DesiredColumnPt, measurer);

        return caret with
        {
            Offset = offset,
            Affinity = AffinityFor(document, targetPage, targetLine, offset),
        };
    }

    private static (int PageIndex, int LineIndex) Locate(Caret caret, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return CaretGeometry.FindLine(caret.Offset, document, caret.Affinity);
    }

    /// <summary>
    /// Onde o caret pousa nesta linha. Numa linha de texto, no extremo pedido; num marcador de
    /// bloco, sempre no início.
    /// </summary>
    /// <remarks>
    /// Um marcador é indivisível: ele só significa quebra de página enquanto estiver sozinho na
    /// linha, então uma posição no meio dele não descreve nada que o autor possa editar. As setas
    /// o atravessam de uma tecla, como fazem com um par substituto.
    /// </remarks>
    private static int Stop(LaidOutLine line, bool atEnd) =>
        line.Kind == LineKind.Text && atEnd ? line.SourceEnd : line.SourceStart;

    // Um par substituto é um caractere só para quem escreveu. As setas atravessam os dois code
    // units de uma vez, senão metade das teclas não moveria o caret lugar nenhum visível.
    private static int StepAfter(LaidOutLine line, int offset)
    {
        var local = offset - line.SourceStart;

        foreach (var run in line.Runs)
        {
            var index = local - run.LineOffset;

            if (index >= 0 && index < run.Text.Length && char.IsHighSurrogate(run.Text[index]))
            {
                return 2;
            }
        }

        return 1;
    }

    private static int StepBefore(LaidOutLine line, int offset)
    {
        var local = offset - line.SourceStart;

        foreach (var run in line.Runs)
        {
            var index = local - run.LineOffset - 1;

            if (index > 0 && index < run.Text.Length && char.IsLowSurrogate(run.Text[index]))
            {
                return 2;
            }
        }

        return 1;
    }
}
