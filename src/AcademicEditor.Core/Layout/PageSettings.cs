namespace AcademicEditor.Core.Layout;

/// <summary>
/// Geometria da página, inteiramente em <b>pontos</b> (1/72"). É a unidade interna do layout:
/// a conversão para DIP acontece só na camada de renderização, e é o que mantém o motor
/// independente de tela — o mesmo <c>PaginatedDocument</c> serve para um exportador PDF.
/// </summary>
/// <remarks>
/// <see cref="HeaderReservedHeightPt"/> e <see cref="FooterReservedHeightPt"/> são 0 no MVP, mas
/// existem desde já porque cabeçalho e rodapé (Fase 5) mudam a altura útil da página. Reservar
/// o campo agora evita repaginar todo o motor depois.
/// </remarks>
public readonly record struct PageSettings(
    double WidthPt,
    double HeightPt,
    double MarginTopPt,
    double MarginRightPt,
    double MarginBottomPt,
    double MarginLeftPt,
    double HeaderReservedHeightPt = 0.0,
    double FooterReservedHeightPt = 0.0)
{
    private const double PointsPerMillimeter = 72.0 / 25.4;
    private const double PointsPerInch = 72.0;

    /// <summary>A4: 210 × 297 mm, margens de 1".</summary>
    public static PageSettings A4 { get; } = Uniform(210.0 * PointsPerMillimeter, 297.0 * PointsPerMillimeter, PointsPerInch);

    /// <summary>US Letter: 8,5 × 11", margens de 1".</summary>
    public static PageSettings Letter { get; } = Uniform(8.5 * PointsPerInch, 11.0 * PointsPerInch, PointsPerInch);

    /// <summary>Largura útil para o texto.</summary>
    public double ContentWidthPt => WidthPt - MarginLeftPt - MarginRightPt;

    /// <summary>Altura útil para o texto, já descontando o que cabeçalho e rodapé reservam.</summary>
    public double ContentHeightPt =>
        HeightPt - MarginTopPt - MarginBottomPt - HeaderReservedHeightPt - FooterReservedHeightPt;

    /// <summary>Canto superior esquerdo da área de conteúdo, de onde as linhas são posicionadas.</summary>
    public double ContentLeftPt => MarginLeftPt;

    public double ContentTopPt => MarginTopPt + HeaderReservedHeightPt;

    public static PageSettings Uniform(double widthPt, double heightPt, double marginPt) =>
        new(widthPt, heightPt, marginPt, marginPt, marginPt, marginPt);
}
