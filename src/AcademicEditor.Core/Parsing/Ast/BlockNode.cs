namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Bloco do documento: a unidade que o page breaker empilha numa página.
/// </summary>
/// <remarks>
/// A hierarquia é rasa de propósito. Todo bloco expõe <see cref="Runs"/> — vazio para os que não
/// têm texto, como <see cref="PageBreakNode"/> — para que o motor de layout não precise de um
/// switch de tipo só para chegar ao conteúdo. Ele testa o único caso especial que existe
/// (quebra explícita) e trata o resto uniformemente.
/// </remarks>
public abstract class BlockNode
{
    protected BlockNode(int sourceStart, int sourceLength, IReadOnlyList<InlineRun> runs)
    {
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        Runs = runs;
    }

    /// <summary>Offset do início do bloco no buffer, incluindo a marcação (o <c>#</c> de um heading).</summary>
    public int SourceStart { get; }

    /// <summary>Extensão do bloco no buffer, sem o separador de blocos que vem depois dele.</summary>
    public int SourceLength { get; }

    public IReadOnlyList<InlineRun> Runs { get; }
}
