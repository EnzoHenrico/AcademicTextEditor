using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Junta os dois passos — quebrar em linhas, empilhar em páginas — e produz o documento paginado.
/// </summary>
/// <remarks>
/// Sem estado e sem I/O: recebe AST, geometria e um medidor, devolve um resultado imutável. É o
/// que permite chamá-lo de uma thread de background a cada tecla e publicar o resultado na UI
/// com uma troca de referência.
/// </remarks>
public static class LayoutEngine
{
    /// <param name="caretOffset">
    /// Onde o caret está. O bloco que o contém tem a marcação revelada — é o que faz um título
    /// mostrar o <c>## </c> enquanto se escreve nele e escondê-lo ao sair. Fora dele nada muda.
    /// </param>
    /// <param name="reuse">
    /// O layout anterior e o que mudou desde ele. Quando serve, as linhas dos blocos intocados são
    /// reaproveitadas e só um bloco é requebrado; quando não serve, este método pagina do zero sem
    /// que o chamador precise saber a diferença.
    /// </param>
    /// <param name="cancellationToken">
    /// Verificado entre blocos. Paginar um documento longo custa, e a tecla seguinte já torna o
    /// resultado obsoleto — abandonar cedo devolve a thread em vez de terminar um cálculo que
    /// ninguém vai publicar.
    /// </param>
    public static PaginatedDocument Layout(
        DocumentNode document,
        PageSettings settings,
        ITextMeasurer measurer,
        int caretOffset = -1,
        LayoutReuse? reuse = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ContentWidthPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ContentHeightPt);

        if (reuse is not null
            && Reuse(document, settings, measurer, caretOffset, reuse, cancellationToken) is { } incremental)
        {
            return incremental;
        }

        var breaker = new PageBreaker(settings.ContentHeightPt);

        // Documento sem bloco nenhum ainda tem uma linha: é onde o caret fica depois que o autor
        // apaga tudo. Uma folha sem linha alguma não daria ao caret altura nem posição, e ele
        // simplesmente sumiria da tela.
        if (document.Blocks.Count == 0)
        {
            foreach (var line in LineBreaker.BreakIntoLines([], settings.ContentWidthPt, measurer))
            {
                breaker.AddLine(line);
            }

            return new PaginatedDocument(breaker.Build(), settings);
        }

        var revealed = TextRange.Empty;

        foreach (var block in document.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reveals = caretOffset >= 0
                && caretOffset >= block.SourceStart
                && caretOffset <= block.SourceStart + block.SourceLength;

            if (reveals)
            {
                revealed = new TextRange(block.SourceStart, block.SourceLength);
            }

            if (block is PageBreakNode)
            {
                // A linha entra ANTES da quebra: o marcador fica no rodapé da folha que ele
                // encerra, como no Word. Cobre o trecho real do bloco — que não é sempre "\page",
                // porque espaços em volta continuam valendo — e quem o trata como unidade
                // indivisível é o CaretNavigator, pelo Kind.
                var metrics = measurer.GetLineMetrics(TextStyle.Body);

                breaker.AddLine(new LaidOutLine(
                    YPt: 0.0,
                    metrics.HeightPt,
                    metrics.BaselinePt,
                    [],
                    block.SourceStart,
                    block.SourceLength,
                    LineKind.PageBreak));

                breaker.ForcePageBreak();
                continue;
            }

            foreach (var line in LineBreaker.BreakIntoLines(
                block.Runs,
                settings.ContentWidthPt,
                measurer,
                includeMarkup: reveals))
            {
                breaker.AddLine(line);
            }
        }

        return new PaginatedDocument(breaker.Build(), settings, revealed);
    }

    /// <summary>
    /// Pagina reaproveitando as linhas do layout anterior, ou devolve <c>null</c> quando a
    /// estrutura não permite e a paginação completa tem de assumir.
    /// </summary>
    /// <remarks>
    /// Recusa em vez de arriscar. Um reaproveitamento errado não quebra o desenho — quebra o
    /// caret, porque os offsets das linhas deixam de descrever o buffer, e isso aparece longe
    /// daqui, como uma tecla que pousa no lugar errado.
    /// </remarks>
    private static PaginatedDocument? Reuse(
        DocumentNode document,
        PageSettings settings,
        ITextMeasurer measurer,
        int caretOffset,
        LayoutReuse reuse,
        CancellationToken cancellationToken)
    {
        if (reuse.Previous.Settings != settings)
        {
            return null;
        }

        var dirtyIndex = DirtyBlock(document, reuse.DirtyStart);

        if (dirtyIndex < 0 || document.Blocks[dirtyIndex] is PageBreakNode)
        {
            return null;
        }

        var dirty = document.Blocks[dirtyIndex];
        var dirtyWas = new TextRange(dirty.SourceStart, dirty.SourceLength - reuse.LengthDelta);

        // Revelar a marcação muda a largura da linha, então um bloco que entrou ou saiu do caret
        // mudou de aparência sem ter mudado de texto. Reaproveitar só vale quando o bloco revelado
        // é o mesmo de antes E é o bloco sujo — que é o que acontece enquanto se digita.
        if (!dirtyWas.Contains(caretOffset) || reuse.Previous.RevealedBlock != dirtyWas)
        {
            return null;
        }

        var previous = reuse.Previous.Pages.SelectMany(page => page.Lines).ToArray();
        var breaker = new PageBreaker(settings.ContentHeightPt);
        var cursor = 0;

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var block = document.Blocks[index];

            // Antes do bloco sujo os offsets não se moveram; depois dele, todos andaram o mesmo
            // tanto. É o que torna o reaproveitamento uma soma, e não um recálculo.
            var shift = index > dirtyIndex ? reuse.LengthDelta : 0;
            var oldEnd = index == dirtyIndex
                ? dirtyWas.End
                : block.SourceStart - shift + block.SourceLength;

            var first = cursor;

            while (cursor < previous.Length && previous[cursor].SourceStart <= oldEnd)
            {
                cursor++;
            }

            // Todo bloco produziu ao menos uma linha da outra vez. Nenhuma aqui significa que a
            // premissa do mapa um-para-um não valeu, e insistir produziria offsets errados.
            if (cursor == first)
            {
                return null;
            }

            if (index == dirtyIndex)
            {
                foreach (var line in LineBreaker.BreakIntoLines(
                    block.Runs,
                    settings.ContentWidthPt,
                    measurer,
                    includeMarkup: true))
                {
                    breaker.AddLine(line);
                }

                continue;
            }

            for (var line = first; line < cursor; line++)
            {
                var reused = shift == 0 ? previous[line] : Shift(previous[line], shift);

                breaker.AddLine(reused);

                // O caminho completo força a quebra logo depois do marcador. Reaproveitar tem de
                // repetir isso, senão a folha que ele encerra passa a receber o texto de baixo.
                if (reused.Kind == LineKind.PageBreak)
                {
                    breaker.ForcePageBreak();
                }
            }
        }

        return cursor == previous.Length
            ? new PaginatedDocument(breaker.Build(), settings, new TextRange(dirty.SourceStart, dirty.SourceLength))
            : null;
    }

    /// <summary>
    /// O último bloco que começa em ou antes do trecho alterado. Como a alteração não cria nem
    /// apaga uma quebra de linha, ela cabe inteira dentro dele.
    /// </summary>
    private static int DirtyBlock(DocumentNode document, int dirtyStart)
    {
        var found = -1;

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            if (document.Blocks[index].SourceStart > dirtyStart)
            {
                break;
            }

            found = index;
        }

        return found;
    }

    /// <summary>A mesma linha, algumas posições adiante no buffer.</summary>
    /// <remarks>
    /// Uma cópia de record por linha — o mesmo que o page breaker já paga ao assentá-la numa
    /// folha. O que se economiza é o caro: montar chunks, medir cada palavra e alocar o texto dos
    /// runs.
    /// </remarks>
    private static LaidOutLine Shift(LaidOutLine line, int delta)
    {
        if (line.Runs.Count == 0)
        {
            return line with { SourceStart = line.SourceStart + delta };
        }

        var runs = new LaidOutRun[line.Runs.Count];

        for (var index = 0; index < runs.Length; index++)
        {
            runs[index] = line.Runs[index] with { SourceStart = line.Runs[index].SourceStart + delta };
        }

        return line with { SourceStart = line.SourceStart + delta, Runs = runs };
    }
}
