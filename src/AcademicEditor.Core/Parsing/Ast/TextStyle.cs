namespace AcademicEditor.Core.Parsing.Ast;

/// <summary>
/// Peso da fonte. Enum próprio do Core em vez de <c>Avalonia.Media.FontWeight</c>: o motor de
/// layout não pode conhecer o toolkit gráfico. A tradução acontece no <c>AvaloniaTextMeasurer</c>.
/// </summary>
public enum FontWeightKind
{
    Normal,
    Bold,
}

/// <summary>
/// Estilo tipográfico de um trecho de texto. Tamanho em <b>pontos</b> (1/72"), a unidade
/// interna do layout — a conversão para DIP só existe na camada de renderização.
/// </summary>
/// <remarks>
/// É <c>readonly record struct</c> por dois motivos: não aloca (cada <see cref="InlineRun"/>
/// carrega um), e a igualdade estrutural de graça serve como chave de cache de medição no
/// <c>ITextMeasurer</c>, onde medir duas vezes o mesmo par (texto, estilo) é desperdício.
/// </remarks>
public readonly record struct TextStyle(double FontSizePt, FontWeightKind Weight, bool Italic)
{
    /// <summary>Estilo do corpo de texto: 11pt, normal.</summary>
    public static readonly TextStyle Body = new(11.0, FontWeightKind.Normal, Italic: false);
}
