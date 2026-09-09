using AcademicEditor.Core.Layout;

namespace AcademicEditor.Core.Tests.Layout;

public sealed class PageSettingsTests
{
    [Fact]
    public void A4_mede_210_por_297_milimetros_em_pontos()
    {
        Assert.Equal(595.28, PageSettings.A4.WidthPt, precision: 2);
        Assert.Equal(841.89, PageSettings.A4.HeightPt, precision: 2);
    }

    [Fact]
    public void Letter_mede_8_5_por_11_polegadas_em_pontos()
    {
        Assert.Equal(612.0, PageSettings.Letter.WidthPt);
        Assert.Equal(792.0, PageSettings.Letter.HeightPt);
    }

    [Fact]
    public void Area_de_conteudo_desconta_margens()
    {
        var settings = PageSettings.Uniform(widthPt: 500.0, heightPt: 800.0, marginPt: 50.0);

        Assert.Equal(400.0, settings.ContentWidthPt);
        Assert.Equal(700.0, settings.ContentHeightPt);
        Assert.Equal(50.0, settings.ContentLeftPt);
        Assert.Equal(50.0, settings.ContentTopPt);
    }

    // Cabeçalho e rodapé são Fase 6, mas o espaço que reservam já sai da altura útil: é o que
    // evita repaginar o motor inteiro quando eles entrarem.
    [Fact]
    public void Espaco_reservado_para_cabecalho_e_rodape_sai_da_altura_util()
    {
        var settings = PageSettings.Uniform(500.0, 800.0, 50.0) with
        {
            HeaderReservedHeightPt = 30.0,
            FooterReservedHeightPt = 20.0,
        };

        Assert.Equal(650.0, settings.ContentHeightPt);
        Assert.Equal(80.0, settings.ContentTopPt);
        Assert.Equal(400.0, settings.ContentWidthPt);
    }
}
