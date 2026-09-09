namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Título de nível 1 a 6 (<c>#</c> a <c>######</c>).
/// </summary>
/// <remarks>
/// O estilo já vem resolvido nos <see cref="BlockNode.Runs"/>; <see cref="Level"/> permanece
/// porque é informação semântica, não visual — é dela que o sumário automático (Fase 6) vive.
/// </remarks>
public sealed class HeadingNode : BlockNode
{
    public HeadingNode(int level, int sourceStart, int sourceLength, IReadOnlyList<InlineRun> runs)
        : base(sourceStart, sourceLength, runs)
    {
        Level = level;
    }

    /// <summary>Nível de 1 a 6.</summary>
    public int Level { get; }
}
