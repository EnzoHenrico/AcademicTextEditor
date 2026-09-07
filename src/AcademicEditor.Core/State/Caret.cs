namespace AcademicEditor.Core.State;

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
public readonly record struct Caret(int Offset, double DesiredColumnPt);
