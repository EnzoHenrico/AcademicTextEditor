namespace AcademicEditor.Core.Text;

/// <summary>
/// O que uma edição fez com a lista de peças: um trecho dela virou outro. É a unidade do undo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não guarda texto.</b> Apagar no piece table nunca toca nos buffers — o texto removido
/// continua lá, e as peças antigas ainda apontam para ele. Desfazer é recolocar as peças no
/// lugar. Colar dez páginas e desfazer custa uma emenda na lista, não uma cópia de dez páginas.
/// </para>
/// <para>
/// Simétrico de propósito: refazer é o mesmo movimento com <see cref="Before"/> e
/// <see cref="After"/> trocados, então não há um segundo caminho de código para manter correto.
/// </para>
/// </remarks>
/// <param name="Index">Onde o trecho começa na lista de peças.</param>
/// <param name="Before">As peças que estavam lá.</param>
/// <param name="After">As que ficaram no lugar delas.</param>
/// <param name="LengthDelta">Quanto o documento cresceu (negativo se encolheu).</param>
public sealed record PieceEdit(
    int Index,
    IReadOnlyList<Piece> Before,
    IReadOnlyList<Piece> After,
    int LengthDelta)
{
    /// <summary>Edição que não mexeu em nada — o retorno de um insert ou delete vazio.</summary>
    public static readonly PieceEdit Empty = new(0, [], [], 0);

    public bool IsEmpty => Before.Count == 0 && After.Count == 0 && LengthDelta == 0;
}
