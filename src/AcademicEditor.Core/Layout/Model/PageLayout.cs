namespace AcademicEditor.Core.Layout.Model;

/// <summary>
/// Uma página: as linhas que couberam nela, já posicionadas em relação ao topo da área de conteúdo.
/// </summary>
/// <remarks>
/// <see cref="Header"/> e <see cref="Footer"/> ficam <b>fora</b> de <see cref="Lines"/>, e não é
/// detalhe de arrumação: aquela lista é o mapa de offset para linha que o caret, a seleção e o
/// reaproveitamento de layout percorrem. Uma linha sem offset no buffer capturaria o caret e
/// derrubaria o reflow incremental. Ver <see cref="BandRun"/>.
/// </remarks>
public sealed record PageLayout(IReadOnlyList<LaidOutLine> Lines)
{
    /// <summary>A faixa desenhada acima da área de conteúdo. Vazia quando não há cabeçalho.</summary>
    public IReadOnlyList<BandRun> Header { get; init; } = [];

    /// <summary>A faixa desenhada abaixo da área de conteúdo. Vazia quando não há rodapé.</summary>
    public IReadOnlyList<BandRun> Footer { get; init; } = [];

    /// <summary>
    /// Onde desenhar o filete que separa o texto das notas de rodapé, ou <c>null</c> quando esta
    /// folha não tem nota nenhuma.
    /// </summary>
    /// <remarks>
    /// Sai do page breaker em vez de ser deduzido da primeira linha de nota, porque ele é a folga
    /// e não a linha: o filete fica no <b>meio</b> do vão que separa os dois, e só quem reservou o
    /// vão sabe onde ele começou. As linhas das notas em si continuam em <see cref="Lines"/>, com
    /// offset no buffer como qualquer outra — o autor as edita como edita um parágrafo.
    /// </remarks>
    public double? FootnoteRulePt { get; init; }
}
