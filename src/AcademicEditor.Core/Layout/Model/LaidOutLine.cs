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

    /// <summary>
    /// Linha de uma nota de rodapé, assentada no pé da folha da chamada.
    /// </summary>
    /// <remarks>
    /// Texto para todos os efeitos do caret — o autor edita a nota como edita qualquer parágrafo.
    /// O <c>Kind</c> existe para quem desenha (o filete vai acima da primeira delas) e para quem
    /// reaproveita o layout (elas não voltam para o fluxo).
    /// </remarks>
    Footnote,
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

    /// <summary>O caret anda por <b>dentro</b> desta linha?</summary>
    /// <remarks>
    /// <para>
    /// Falso só nos marcadores atômicos, hoje o <c>\page</c>: ali o caret pousa na linha como
    /// unidade e as teclas de apagar removem o marcador inteiro. Todo o resto é texto que o autor
    /// edita, <b>a nota de rodapé inclusive</b>.
    /// </para>
    /// <para>
    /// <b>Existe porque a pergunta errada custou uma entrega.</b> Enquanto só havia
    /// <c>Text</c> e <c>PageBreak</c>, "é texto?" e "não é marcador?" eram a mesma pergunta, e o
    /// caret perguntava a primeira. A nota chegou como um <c>Kind</c> novo e caiu do lado errado:
    /// clicar nela devolvia o começo da linha, e ← e → não andavam por dentro. Perguntar pelo
    /// marcador inverte o padrão para o lado seguro — num editor de texto, linha nova é editável
    /// até dizer o contrário.
    /// </para>
    /// </remarks>
    public bool IsEditable => Kind != LineKind.PageBreak;

    /// <summary>Os identificadores das notas que esta linha chama. Vazia quase sempre.</summary>
    /// <remarks>
    /// É o que o page breaker pergunta para saber quanto de folha reservar antes de assentar a
    /// linha: uma nota estreia na folha onde a <b>chamada</b> é desenhada, e a chamada está numa
    /// linha, não num bloco — um parágrafo longo cai em várias folhas.
    /// </remarks>
    public IReadOnlyList<string> FootnoteCalls { get; init; } = [];
}
