using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.State;

/// <summary>O que a tecla de quebra de linha insere, e onde o caret pousa depois.</summary>
/// <param name="Text">Texto a inserir na posição do caret.</param>
/// <param name="CaretDelta">Quantos caracteres o caret avança a partir de onde estava.</param>
public readonly record struct LineBreakEdit(string Text, int CaretDelta);

/// <summary>
/// Quantos <c>\n</c> um Enter precisa, dado onde o caret está.
/// </summary>
/// <remarks>
/// <para>
/// Um Enter tem de abrir uma linha em branco visível. No meio do texto um <c>\n</c> basta, mas
/// na fronteira de uma quebra por largura ele não muda nada na tela: a quebra já está ali,
/// imposta pela margem, e o <c>\n</c> apenas a torna explícita. Daí a regra —
/// <b>materializar a quebra antes de editar</b>: o primeiro <c>\n</c> paga a quebra que a
/// largura já impunha, o segundo é o que o autor apertou.
/// </para>
/// <para>
/// Mora no Core, e não no ViewModel, porque a decisão depende do <see cref="PaginatedDocument"/>
/// — só o layout sabe onde a margem quebrou — e porque é a regra que precisa de teste. Mesmo
/// raciocínio de <see cref="BlockMarkers"/>.
/// </para>
/// </remarks>
public static class LineBreaks
{
    private const string ExplicitBreak = "\n";

    /// <summary>A quebra que a largura impôs, mais a que o autor pediu.</summary>
    private const string MaterializedBreak = "\n\n";

    public static LineBreakEdit ForEnter(Caret caret, PaginatedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!CaretGeometry.IsSharedBoundary(caret.Offset, document))
        {
            return new LineBreakEdit(ExplicitBreak, 1);
        }

        // Onde o caret fica depende de que lado da fronteira ele estava desenhado. Upstream é o
        // fim da linha de cima (onde End e ← pousam): ali o autor abriu uma linha abaixo de si, e
        // é nela que ele fica. Downstream é o começo da linha de baixo: ali ele empurrou o próprio
        // texto para baixo, e desce junto com ele.
        return new LineBreakEdit(MaterializedBreak, caret.Affinity == CaretAffinity.Upstream ? 1 : 2);
    }
}
