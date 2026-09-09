using AcademicEditor.App.Rendering;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing.Ast;

using Avalonia.Media;

namespace AcademicEditor.App.Tests.Rendering;

/// <summary>
/// A tradução do estilo do Core para o typeface do toolkit — a única costura entre o motor de
/// layout e o Avalonia, vista do lado do Avalonia.
/// </summary>
/// <remarks>
/// Constrói <c>Typeface</c> e <c>FontFamily</c>, que são valores: nomear uma fonte não é resolvê-la
/// contra o sistema, então nada aqui precisa de gerenciador de fontes nem de plataforma
/// inicializada.
/// </remarks>
public sealed class AvaloniaTextMeasurerTests
{
    [Fact]
    public void Familia_vazia_e_a_fonte_padrao_do_sistema()
    {
        // O Core não tem como saber qual é a fonte padrão — é justamente por isso que a família
        // vazia existe, e é aqui que ela se resolve.
        var typeface = AvaloniaTextMeasurer.ToTypeface(TextStyle.Body);

        Assert.Equal(FontFamily.Default, typeface.FontFamily);
    }

    [Fact]
    public void Familia_nomeada_chega_ao_typeface_com_as_alternativas()
    {
        var typeface = AvaloniaTextMeasurer.ToTypeface(TypographyPreset.Abnt.Body);

        Assert.Equal("Times New Roman", typeface.FontFamily.Name);

        // As alternativas não são enfeite, e a terceira menos ainda: numa instalação WSL enxuta não
        // havia nem o Times nem a Liberation — só a DejaVu. Sem ela, o documento sairia ali em
        // fonte sem serifa, e nada na tela diria por quê.
        Assert.Contains("Liberation Serif", typeface.FontFamily.FamilyNames);
        Assert.Contains("DejaVu Serif", typeface.FontFamily.FamilyNames);
    }

    /// <remarks>
    /// A mesma <c>FontFamily</c>, e não uma igual. <c>ToTypeface</c> é chamado no desenho de cada
    /// run de cada linha visível, a cada quadro — e o caret repinta a superfície duas vezes por
    /// segundo, para sempre. Sem o cache seriam centenas de objetos de vida curtíssima na geração 0
    /// por repintura, para nada.
    /// </remarks>
    [Fact]
    public void A_familia_e_construida_uma_vez_so()
    {
        var first = AvaloniaTextMeasurer.ToTypeface(TypographyPreset.Abnt.Body);
        var second = AvaloniaTextMeasurer.ToTypeface(TypographyPreset.Abnt.Heading(1));

        Assert.Same(first.FontFamily, second.FontFamily);
    }

    [Theory]
    [InlineData(FontWeightKind.Normal, false)]
    [InlineData(FontWeightKind.Bold, false)]
    [InlineData(FontWeightKind.Normal, true)]
    [InlineData(FontWeightKind.Bold, true)]
    public void Peso_e_italico_atravessam_a_traducao(FontWeightKind weight, bool italic)
    {
        var typeface = AvaloniaTextMeasurer.ToTypeface(
            TextStyle.Body with { Weight = weight, Italic = italic });

        Assert.Equal(weight == FontWeightKind.Bold ? FontWeight.Bold : FontWeight.Normal, typeface.Weight);
        Assert.Equal(italic ? FontStyle.Italic : FontStyle.Normal, typeface.Style);
    }
}
