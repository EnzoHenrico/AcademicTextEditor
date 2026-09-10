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
    /// <param name="preset">
    /// A norma tipográfica com que estas linhas são medidas. <c>null</c> usa o
    /// <see cref="TypographyPreset.Default"/>. Sai carimbada no resultado, porque reaproveitar
    /// linhas medidas com outra fonte não quebra o desenho — quebra o caret.
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
        TypographyPreset? preset = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ContentWidthPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ContentHeightPt);

        var typography = preset ?? TypographyPreset.Default;

        if (reuse is not null
            && Reuse(document, settings, measurer, caretOffset, reuse, typography, cancellationToken)
                is { } incremental)
        {
            return incremental;
        }

        // As definições são quebradas ANTES de qualquer linha ser assentada, e é isso que dispensa
        // o laço de convergência: o page breaker sabe a altura de uma nota antes de decidir se a
        // linha que a chama cabe na folha.
        var footnotes = Footnotes.Gather(document, settings, measurer, caretOffset, typography);

        var breaker = new PageBreaker(settings.ContentHeightPt, footnotes.Notes, footnotes.RuleGapPt);

        // Documento sem bloco nenhum ainda tem uma linha: é onde o caret fica depois que o autor
        // apaga tudo. Uma folha sem linha alguma não daria ao caret altura nem posição, e ele
        // simplesmente sumiria da tela.
        if (document.Blocks.Count == 0)
        {
            foreach (var line in LineBreaker.BreakIntoLines(
                [], settings.ContentWidthPt, measurer, includeMarkup: false, typography, typography.Alignment))
            {
                breaker.AddLine(line);
            }

            return new PaginatedDocument(breaker.Build(), settings) { Typography = typography };
        }

        var revealed = TextRange.Empty;

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var block = document.Blocks[index];

            var reveals = caretOffset >= 0
                && caretOffset >= block.SourceStart
                && caretOffset <= block.SourceStart + block.SourceLength;

            if (reveals)
            {
                revealed = new TextRange(block.SourceStart, block.SourceLength);
            }

            // A definição de uma nota chamada não entra no fluxo: as linhas dela já estão no
            // acervo, e o page breaker as assenta no pé da folha da chamada. A que ninguém chamou
            // continua sendo parágrafo comum, no lugar em que foi escrita.
            if (footnotes.IsPlaced(index))
            {
                continue;
            }

            if (block is PageBreakNode)
            {
                // A linha entra ANTES da quebra: o marcador fica no rodapé da folha que ele
                // encerra, como no Word. Cobre o trecho real do bloco — que não é sempre "\page",
                // porque espaços em volta continuam valendo — e quem o trata como unidade
                // indivisível é o CaretNavigator, pelo Kind.
                var metrics = typography.Apply(measurer.GetLineMetrics(typography.Body));

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
                includeMarkup: reveals,
                typography,
                block.Alignment,
                block.FootnoteCalls))
            {
                breaker.AddLine(line);
            }
        }

        return new PaginatedDocument(breaker.Build(), settings, revealed) { Typography = typography };
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
        TypographyPreset typography,
        CancellationToken cancellationToken)
    {
        // Geometria e tipografia são as duas metades da norma, e nenhuma das duas sobrevive a uma
        // mudança: as linhas anteriores foram quebradas noutra largura ou medidas noutra fonte.
        if (reuse.Previous.Settings != settings || reuse.Previous.Typography != typography)
        {
            return null;
        }

        // O bloco que o caret revela agora — a mesma regra do caminho completo, e por isso o último
        // que contém o offset quando dois se encostam.
        var revealIndex = BlockContaining(document, caretOffset);
        var revealed = revealIndex < 0
            ? TextRange.Empty
            : RangeOf(document.Blocks[revealIndex]);

        // O texto não mudou: é uma travessia de bloco, um clique ou uma seta que sai de um bloco e
        // entra noutro. Nenhum offset se moveu — o que muda é qual bloco mostra a marcação, e são
        // dois: o que a perde e o que a ganha.
        //
        // Sem este caminho, atravessar bloco recusava o reaproveitamento e repaginava tudo. O custo
        // foi aceito quando "bloco" queria dizer parágrafo; desde que uma linha da fonte é uma
        // linha na página, um bloco É uma linha — e o preço virou uma repaginação completa por ↑/↓.
        if (reuse.LengthDelta == 0 && reuse.DirtyOldEnd == reuse.DirtyStart)
        {
            var losingIndex = IndexOfBlock(document, reuse.Previous.RevealedBlock);

            // Havia marcação revelada e ela não casa com bloco nenhum: as linhas publicadas mostram
            // uma marcação que já não deviam mostrar, e não há como saber quais. Pagina do zero.
            if (reuse.Previous.RevealedBlock != TextRange.Empty && losingIndex < 0)
            {
                return null;
            }

            return Rebuild(
                document,
                settings,
                measurer,
                caretOffset,
                typography,
                reuse,
                shiftAfter: -1,
                dirtyOldEnd: -1,
                rebreakFirst: Math.Min(losingIndex, revealIndex),
                rebreakSecond: Math.Max(losingIndex, revealIndex),
                revealIndex,
                revealed,
                cancellationToken);
        }

        var dirtyIndex = DirtyBlock(document, reuse.DirtyStart);

        if (dirtyIndex < 0 || document.Blocks[dirtyIndex] is PageBreakNode)
        {
            return null;
        }

        var dirty = document.Blocks[dirtyIndex];
        var dirtyWas = new TextRange(dirty.SourceStart, dirty.SourceLength - reuse.LengthDelta);
        var dirtyIs = new TextRange(dirty.SourceStart, dirty.SourceLength);

        // Revelar a marcação muda a largura da linha, então um bloco que entrou ou saiu do caret
        // mudou de aparência sem ter mudado de texto. Reaproveitar só vale quando o bloco revelado
        // é o mesmo de antes E é o bloco sujo — que é o que acontece enquanto se digita.
        //
        // As duas metades comparam coisas diferentes de propósito, e confundi-las custava caro:
        // RevealedBlock veio do layout anterior e só case com o intervalo ANTIGO; o caretOffset
        // chega DEPOIS da edição — quem digita move o caret e só então pede a repaginação —, então
        // ele só cabe no intervalo NOVO. Comparar o caret novo com o intervalo antigo recusava toda
        // tecla digitada no fim de um parágrafo, que é como se escreve: "abc" mais um "d" dá um
        // intervalo antigo de 0..3 e um caret em 4, e o motor paginava as 300 folhas do zero.
        if (!dirtyIs.Contains(caretOffset) || reuse.Previous.RevealedBlock != dirtyWas)
        {
            return null;
        }

        return Rebuild(
            document,
            settings,
            measurer,
            caretOffset,
            typography,
            reuse,
            shiftAfter: dirtyIndex,
            dirtyOldEnd: dirtyWas.End,
            rebreakFirst: revealIndex < 0 ? dirtyIndex : Math.Min(dirtyIndex, revealIndex),
            rebreakSecond: Math.Max(dirtyIndex, revealIndex),
            revealIndex,
            revealed,
            cancellationToken);
    }

    /// <summary>
    /// Monta o documento novo aproveitando as linhas do anterior, requebrando <b>no máximo dois</b>
    /// blocos: o que mudou de texto e o que mudou de revelação.
    /// </summary>
    /// <remarks>
    /// Dois, e não um, porque revelar a marcação é uma troca: ela sai de um bloco e aparece noutro.
    /// Um caminho que só soubesse requebrar um deles teria de recusar a travessia — que é o que ele
    /// fazia, e o que tornava cada ↑/↓ uma repaginação completa.
    /// </remarks>
    /// <param name="shiftAfter">
    /// Blocos depois deste andaram <c>LengthDelta</c> no buffer. <c>-1</c> quando o texto não mudou
    /// e ninguém andou.
    /// </param>
    /// <param name="dirtyOldEnd">Fim antigo do bloco sujo; ignorado quando não há um.</param>
    /// <param name="revealIndex">
    /// O bloco que mostra a marcação agora. É <b>ele</b> que decide o <c>includeMarkup</c> de cada
    /// requebra — passar <c>true</c> sempre, como este caminho fazia, deixaria o bloco que
    /// <i>perdeu</i> a revelação requebrado com a marcação ainda à mostra.
    /// </param>
    private static PaginatedDocument? Rebuild(
        DocumentNode document,
        PageSettings settings,
        ITextMeasurer measurer,
        int caretOffset,
        TypographyPreset typography,
        LayoutReuse reuse,
        int shiftAfter,
        int dirtyOldEnd,
        int rebreakFirst,
        int rebreakSecond,
        int revealIndex,
        TextRange revealed,
        CancellationToken cancellationToken)
    {
        // O passeio é pelo ÍNDICE, que está em ordem de fonte — que é a ordem em que este laço
        // casa linha com bloco. Achatar as folhas dava a ordem de DESENHO: hoje elas divergem, e o
        // reaproveitamento passaria a mapear bloco errado para linha errada. Isso não quebra o
        // desenho, quebra o caret. De quebra some o array de dezesseis mil linhas que era
        // materializado a cada tecla, e antes das guardas.
        var previous = reuse.Previous.Index;
        var pages = reuse.Previous.Pages;

        // Primeiro passe: que linhas do layout anterior pertencem a cada bloco. Sai daqui e não do
        // laço de baixo porque as notas precisam do mapa ANTES de a paginação começar — a
        // definição está no fim do arquivo e a folha em que ela é desenhada, no meio.
        var first = new int[document.Blocks.Count];
        var count = new int[document.Blocks.Count];
        var cursor = 0;

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var block = document.Blocks[index];

            // Antes do bloco sujo os offsets não se moveram; depois dele, todos andaram o mesmo
            // tanto. É o que torna o reaproveitamento uma soma, e não um recálculo.
            var oldEnd = index == shiftAfter
                ? dirtyOldEnd
                : block.SourceStart - ShiftOf(index) + block.SourceLength;

            first[index] = cursor;

            while (cursor < previous.Count && previous[cursor].SourceStart <= oldEnd)
            {
                cursor++;
            }

            // Todo bloco produziu ao menos uma linha da outra vez. Nenhuma aqui significa que a
            // premissa do mapa um-para-um não valeu, e insistir produziria offsets errados.
            if (cursor == first[index])
            {
                return null;
            }

            count[index] = cursor - first[index];
        }

        if (cursor != previous.Count)
        {
            return null;
        }

        // As notas reaproveitam as linhas de antes, deslocadas — requebrar as duzentas definições
        // de uma tese a cada tecla é o que o guard de desempenho pegou. Só a definição que mudou de
        // texto ou de revelação é requebrada, que é a mesma regra do fluxo.
        var footnotes = Footnotes.Gather(
            document,
            settings,
            measurer,
            caretOffset,
            typography,
            index => index == rebreakFirst || index == rebreakSecond ? null : Notes(index));

        var breaker = new PageBreaker(settings.ContentHeightPt, footnotes.Notes, footnotes.RuleGapPt);

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var block = document.Blocks[index];

            // As linhas desta definição já estão no acervo: o page breaker as assenta no pé da
            // folha da chamada, e elas não voltam para o fluxo.
            if (footnotes.IsPlaced(index))
            {
                continue;
            }

            // O marcador de quebra de página não tem marcação a revelar, e o texto dele não muda
            // numa travessia: a linha anterior serve, e reaproveitá-la preserva o ForcePageBreak.
            if ((index == rebreakFirst || index == rebreakSecond) && block is not PageBreakNode)
            {
                foreach (var line in LineBreaker.BreakIntoLines(
                    block.Runs,
                    settings.ContentWidthPt,
                    measurer,
                    includeMarkup: index == revealIndex,
                    typography,
                    block.Alignment,
                    block.FootnoteCalls))
                {
                    breaker.AddLine(line);
                }

                continue;
            }

            var shift = ShiftOf(index);

            for (var line = first[index]; line < first[index] + count[index]; line++)
            {
                var reused = shift == 0 ? LineAt(line) : Shift(LineAt(line), shift);

                breaker.AddLine(reused);

                // O caminho completo força a quebra logo depois do marcador. Reaproveitar tem de
                // repetir isso, senão a folha que ele encerra passa a receber o texto de baixo.
                if (reused.Kind == LineKind.PageBreak)
                {
                    breaker.ForcePageBreak();
                }
            }
        }

        return new PaginatedDocument(breaker.Build(), settings, revealed) { Typography = typography };

        int ShiftOf(int index) => shiftAfter >= 0 && index > shiftAfter ? reuse.LengthDelta : 0;

        LaidOutLine LineAt(int position)
        {
            var found = previous[position];

            return pages[found.PageIndex].Lines[found.LineIndex];
        }

        // As linhas que esta definição produziu da outra vez, deslocadas e carimbadas como nota —
        // o carimbo importa porque a definição pode não ter sido chamada antes.
        LaidOutLine[] Notes(int index)
        {
            var moved = new LaidOutLine[count[index]];
            var shift = ShiftOf(index);

            for (var line = 0; line < moved.Length; line++)
            {
                var original = LineAt(first[index] + line);

                moved[line] = original.Kind == LineKind.Footnote && shift == 0
                    ? original
                    : original with { SourceStart = original.SourceStart + shift, Kind = LineKind.Footnote };
            }

            return moved;
        }
    }

    private static TextRange RangeOf(BlockNode block) => new(block.SourceStart, block.SourceLength);

    /// <summary>
    /// O bloco cuja extensão contém o offset, ou -1. O <b>último</b> deles quando dois se encostam,
    /// que é a mesma regra do caminho completo — lá o laço sobrescreve e o último vence.
    /// </summary>
    private static int BlockContaining(DocumentNode document, int offset)
    {
        if (offset < 0)
        {
            return -1;
        }

        var found = -1;

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];

            // Os blocos saem do tokenizer em ordem: passado o offset, nenhum dos seguintes o contém.
            if (block.SourceStart > offset)
            {
                break;
            }

            if (offset <= block.SourceStart + block.SourceLength)
            {
                found = index;
            }
        }

        return found;
    }

    /// <summary>O bloco com exatamente esta extensão, ou -1 quando nenhum tem.</summary>
    private static int IndexOfBlock(DocumentNode document, TextRange range)
    {
        if (range == TextRange.Empty)
        {
            return -1;
        }

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];

            if (block.SourceStart > range.Start)
            {
                break;
            }

            if (block.SourceStart == range.Start && block.SourceLength == range.Length)
            {
                return index;
            }
        }

        return -1;
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
    /// <b>Uma cópia de record, e nada mais.</b> Os offsets dos runs são relativos ao começo da
    /// linha, então mover a linha move todos eles de graça. Com offset absoluto isto reescrevia run
    /// a run — um ou dois por linha à esquerda, mas ~20 numa linha justificada, porque a
    /// justificação parte a linha em cada fronteira de branco. Digitar no meio de uma tese desloca
    /// metade do documento, e era essa a metade cara de uma tecla.
    /// </remarks>
    private static LaidOutLine Shift(LaidOutLine line, int delta) =>
        line with { SourceStart = line.SourceStart + delta };
}
