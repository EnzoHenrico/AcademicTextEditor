using AcademicEditor.Core.Layout;

namespace AcademicEditor.App;

/// <summary>
/// A configuração de documento que o aplicativo <b>executa</b>: geometria da folha, norma
/// tipográfica e faixas de cabeçalho e rodapé.
/// </summary>
/// <remarks>
/// <para>
/// <b>Um dono só, e é esta a lição que custou uma entrega.</b> A PR #6 passou pelo gate com o
/// aplicativo quebrado porque teste e medição descreviam uma configuração que ninguém abre —
/// alinhado à esquerda, corpo 11, corpus sem marcação. Quem quiser conferir o que o autor vê pede
/// daqui, e não monta a própria.
/// </para>
/// <para>
/// O <c>LayoutBenchmark</c> é a exceção deliberada: ele fixa <c>TypographyPreset.Default</c> porque
/// os números históricos do roadmap saíram de lá, e trocá-lo os tornaria incomparáveis em silêncio.
/// Lá a divergência é o ponto, e está escrita; aqui ela seria o defeito.
/// </para>
/// </remarks>
public static class DocumentConfiguration
{
    /// <summary>
    /// A4 com uma faixa reservada no alto para o cabeçalho. 24pt ≈ 8,5mm: cabe a linha do número da
    /// página — corpo 12 mede cerca de 14pt de altura — com folga até a primeira linha do texto.
    /// </summary>
    /// <remarks>
    /// A reserva mora junto do que vai ser escrito nela, porque as duas decisões são uma só: sem
    /// reserva o <c>PageBands</c> não desenha nada, e reserva sem texto é papel em branco. A
    /// geometria continua tendo um dono só, que é o <see cref="PageSettings"/>.
    /// </remarks>
    public static PageSettings Page { get; } = PageSettings.A4 with { HeaderReservedHeightPt = 24.0 };

    /// <summary>Times 12pt com entrelinhamento 1,5 e texto justificado.</summary>
    public static TypographyPreset Typography => TypographyPreset.Abnt;

    /// <summary>Número da página no canto superior direito.</summary>
    public static HeaderFooterSettings Bands => HeaderFooterSettings.Abnt;
}
