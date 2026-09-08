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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ContentWidthPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.ContentHeightPt);

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
}
