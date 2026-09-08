namespace AcademicEditor.Core.Layout.Model;

/// <summary>O que a linha é, para quem desenha e para quem edita.</summary>
/// <remarks>
/// Enum e não booleano: filete horizontal e outros marcadores de bloco entram aqui depois, e cada
/// um deles é desenhado de um jeito e apagado como uma unidade.
/// </remarks>
public enum LineKind
{
    /// <summary>Linha de texto comum, inclusive a vazia.</summary>
    Text,

    /// <summary>Marcador de quebra de página. Desenhado como um filete, apagado inteiro.</summary>
    PageBreak,
}

/// <summary>
/// Uma linha visual já quebrada e posicionada. Imutável: a publicação do layout para a UI é uma
/// troca de referência atômica, sem lock.
/// </summary>
/// <remarks>
/// O line breaker emite a linha com <see cref="YPt"/> = 0 e o page breaker a reposiciona com
/// <c>line with { YPt = ... }</c> ao assentá-la numa página. <see cref="SourceStart"/> e
/// <see cref="SourceLength"/> cobrem o trecho do buffer que caiu nesta linha — é por eles que a
/// navegação do caret encontra a linha corrente sem varrer o texto.
/// </remarks>
/// <param name="YPt">Topo da linha, relativo ao topo da área de conteúdo da página.</param>
/// <param name="BaselinePt">Distância do topo da linha até a baseline.</param>
public sealed record LaidOutLine(
    double YPt,
    double HeightPt,
    double BaselinePt,
    IReadOnlyList<LaidOutRun> Runs,
    int SourceStart,
    int SourceLength,
    LineKind Kind = LineKind.Text)
{
    public int SourceEnd => SourceStart + SourceLength;
}
