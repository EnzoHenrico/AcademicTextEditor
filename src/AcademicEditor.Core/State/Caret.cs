namespace AcademicEditor.Core.State;

/// <summary>
/// De que lado de uma fronteira de linha o caret está, quando as duas linhas compartilham o mesmo
/// offset.
/// </summary>
/// <remarks>
/// Numa quebra por largura o espaço fica com a linha de cima, então o fim de uma linha e o começo
/// da seguinte são <b>o mesmo offset</b> — um offset, duas posições na tela. A afinidade é o que
/// desempata, e ela não pode ser deduzida do offset: é estado, e por isso mora no caret. Numa
/// quebra de linha explícita não há ambiguidade, porque o <c>\n</c> ocupa uma posição entre as
/// duas, e aí a afinidade não muda nada.
/// </remarks>
public enum CaretAffinity
{
    /// <summary>Começo da linha de baixo. É o caso comum.</summary>
    Downstream,

    /// <summary>Fim da linha de cima — onde End e ← pousam.</summary>
    Upstream,
}

/// <summary>
/// Onde o próximo caractere entra, e para onde ↑/↓ miram.
/// </summary>
/// <remarks>
/// <see cref="DesiredColumnPt"/> é a coluna que a navegação vertical persegue, e ela sobrevive à
/// travessia de linhas curtas. Sem isso, descer de uma linha longa para uma curta e voltar a
/// subir grudaria o caret no fim da linha curta — todo editor guarda esta coluna, e é a primeira
/// coisa que se sente faltando quando não guarda.
/// </remarks>
/// <param name="Offset">Posição no buffer, em caracteres UTF-16.</param>
/// <param name="DesiredColumnPt">Coluna alvo em pontos, medida do início da linha.</param>
/// <param name="Affinity">Qual das duas linhas, quando o offset serve às duas.</param>
public readonly record struct Caret(
    int Offset,
    double DesiredColumnPt,
    CaretAffinity Affinity = CaretAffinity.Downstream);
