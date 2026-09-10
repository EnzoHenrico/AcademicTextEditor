namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Sumário automático (<c>\toc</c> numa linha isolada). Não tem texto próprio: o que aparece na
/// folha é uma entrada por título do documento, montada pelo layout.
/// </summary>
/// <remarks>
/// Irmão do <see cref="PageBreakNode"/>, e de propósito: os dois são linhas atômicas que o autor
/// digita como comando, e por isso o caret pousa neles como unidade e as teclas de apagar removem o
/// marcador inteiro. O que os separa é o que o motor faz depois — um fecha a folha, o outro abre
/// espaço para as entradas.
/// </remarks>
public sealed class TocNode : BlockNode
{
    public TocNode(int sourceStart, int sourceLength)
        : base(sourceStart, sourceLength, [])
    {
    }
}
