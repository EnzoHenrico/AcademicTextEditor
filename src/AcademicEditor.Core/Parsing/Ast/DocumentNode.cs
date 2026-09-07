namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Raiz da AST: a sequência de blocos do documento, na ordem da fonte.
/// </summary>
public sealed class DocumentNode
{
    public DocumentNode(IReadOnlyList<BlockNode> blocks) => Blocks = blocks;

    public IReadOnlyList<BlockNode> Blocks { get; }
}
