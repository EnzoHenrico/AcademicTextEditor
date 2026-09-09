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
}
