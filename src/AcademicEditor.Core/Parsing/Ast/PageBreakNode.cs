namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Quebra de página explícita (<c>\page</c> numa linha isolada). Não tem texto: o page breaker
/// apenas fecha a página corrente ao encontrá-la.
/// </summary>
public sealed class PageBreakNode : BlockNode
{
    public PageBreakNode(int sourceStart, int sourceLength)
        : base(sourceStart, sourceLength, [])
    {
    }
}
