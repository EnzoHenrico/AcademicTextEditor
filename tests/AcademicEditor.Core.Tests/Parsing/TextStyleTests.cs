using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Parsing;

/// <summary>
/// A aritmética do sobrescrito, que é lida em três lugares: o parser decide o corpo, e os
/// <b>dois</b> renderizadores levantam a baseline.
/// </summary>
public sealed class TextStyleTests
{
    [Fact]
    public void Texto_comum_nao_levanta_a_baseline()
    {
        Assert.Equal(0.0, TextStyle.Body.BaselineRisePt);
    }

    [Fact]
    public void Sobrescrito_encolhe_o_corpo_e_levanta_a_baseline()
    {
        var superscript = TextStyle.Body.AsSuperscript();

        Assert.True(superscript.Superscript);
        Assert.True(superscript.FontSizePt < TextStyle.Body.FontSizePt);
        Assert.Equal(superscript.FontSizePt * TextStyle.SuperscriptRise, superscript.BaselineRisePt, precision: 9);
    }

    /// <remarks>
    /// A subida é sobre o corpo <b>do sobrescrito</b>, não do texto que o envolve: um sobrescrito
    /// num título de 20pt sobe mais que um no corpo de 12, que é o que mantém a proporção.
    /// </remarks>
    [Fact]
    public void A_subida_acompanha_o_corpo_que_a_envolve()
    {
        var small = (TextStyle.Body with { FontSizePt = 10.0 }).AsSuperscript();
        var large = (TextStyle.Body with { FontSizePt = 20.0 }).AsSuperscript();

        Assert.Equal(2.0 * small.BaselineRisePt, large.BaselineRisePt, precision: 9);
    }

    [Fact]
    public void O_sobrescrito_herda_familia_peso_e_italico()
    {
        var bold = new TextStyle("Times", 12.0, FontWeightKind.Bold, Italic: true).AsSuperscript();

        Assert.Equal("Times", bold.FontFamily);
        Assert.Equal(FontWeightKind.Bold, bold.Weight);
        Assert.True(bold.Italic);
    }
}
