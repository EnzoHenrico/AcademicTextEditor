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

    /// <summary>Marcador de sumário. Como o de quebra de página: uma linha, apagado inteiro.</summary>
    TableOfContents,

    /// <summary>
    /// Entrada do sumário: título, condutor de pontos e número da folha.
    /// </summary>
    /// <remarks>
    /// <b>A única linha que não tem posição na fonte</b>, e é isso que a separa das outras. Ver
    /// <see cref="LaidOutLine.IsGenerated"/>.
    /// </remarks>
    TocEntry,

    /// <summary>
    /// O cabeçalho de metadados do topo do arquivo: cobre o trecho e não desenha nada.
    /// </summary>
    /// <remarks>
    /// <b>Tem offset e tem altura zero</b>, que é a combinação que nenhuma outra linha tem. O
    /// offset mantém o mapa <c>offset → linha</c> total; a altura zero a faz não ocupar papel. O
    /// caret não pousa nela — ver <see cref="LaidOutLine.AcceptsCaret"/> — porque uma barra de
    /// altura zero é uma barra que sumiu da tela.
    /// </remarks>
    FrontMatter,
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

    /// <summary>
    /// A linha foi <b>gerada</b> pelo motor e não corresponde a texto nenhum do buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É a forma forte da decisão que o <c>BandRun</c> já carrega: não basta a linha gerada não ter
    /// offset, ela não pode nem <i>fingir</i> ter um. Um <see cref="SourceStart"/> falso mais cedo
    /// ou mais tarde é consumido como conteúdo — e falha de offset não quebra o desenho, quebra o
    /// caret, longe de onde se errou.
    /// </para>
    /// <para>
    /// A diferença em relação à faixa de cabeçalho é que esta linha <b>ocupa espaço no fluxo</b>:
    /// empurra o texto e mora em <c>PageLayout.Lines</c>. Isso só é possível porque a ordem de
    /// desenho e a ordem de fonte são coisas separadas desde o índice — <c>Lines</c> é desenho,
    /// <c>PaginatedDocument.Index</c> é fonte, e a linha gerada entra no primeiro e fica fora do
    /// segundo.
    /// </para>
    /// <para>
    /// <b>Não confundir com marcador.</b> Um <c>\page</c> é atômico mas <i>tem</i> offset: o caret
    /// pousa nele como unidade e as teclas de apagar o removem inteiro. Uma linha gerada não tem
    /// onde o caret pousar, e a navegação a atravessa sem parar.
    /// </para>
    /// </remarks>
    public bool IsGenerated => Kind == LineKind.TocEntry;

    /// <summary>
    /// O caret pode pousar nesta linha?
    /// </summary>
    /// <remarks>
    /// <b>Pergunta diferente de <see cref="IsGenerated"/>, e por isso um nome diferente.</b> Aquela
    /// decide quem entra no índice; esta, quem recebe o caret — e o cabeçalho de metadados responde
    /// <i>sim</i> à primeira e <i>não</i> à segunda: ele tem posição na fonte, mas não tem altura
    /// onde desenhar uma barra. Um marcador de bloco (<c>\page</c>, <c>\toc</c>) responde sim às
    /// duas: é atômico, não invisível.
    /// </remarks>
    public bool AcceptsCaret => Kind is not (LineKind.TocEntry or LineKind.FrontMatter);
}
