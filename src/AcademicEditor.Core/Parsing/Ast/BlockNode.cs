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
    protected BlockNode(
        int sourceStart,
        int sourceLength,
        IReadOnlyList<InlineRun> runs,
        TextAlignment alignment = TextAlignment.Left)
    {
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        Runs = runs;
        Alignment = alignment;
    }

    /// <summary>Offset do início do bloco no buffer, incluindo a marcação (o <c>#</c> de um heading).</summary>
    public int SourceStart { get; }

    /// <summary>Extensão do bloco no buffer, sem o separador de blocos que vem depois dele.</summary>
    public int SourceLength { get; }

    public IReadOnlyList<InlineRun> Runs { get; }

    /// <summary>As notas que este bloco chama, em ordem. Vazia na esmagadora maioria deles.</summary>
    /// <remarks>
    /// Mora no bloco e não no run porque a pergunta que o layout faz é "que notas esta linha
    /// estreia" — e a linha sai da quebra do bloco. Guardar um identificador em cada
    /// <c>LaidOutRun</c> custaria memória em dezesseis mil linhas para uma coisa que aparece
    /// algumas dezenas de vezes numa tese.
    /// </remarks>
    public IReadOnlyList<FootnoteCall> FootnoteCalls { get; init; } = [];

    /// <summary>Como as linhas deste bloco se distribuem na largura útil.</summary>
    /// <remarks>
    /// Já vem resolvido: o parser aplica o padrão do preset quando a linha não traz marcação, e
    /// quem quebra as linhas recebe um valor concreto em vez de ter de consultar a norma de novo.
    /// </remarks>
    public TextAlignment Alignment { get; }
}
