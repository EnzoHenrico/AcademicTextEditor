using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;

namespace AcademicEditor.Core.Tests.Parsing;

public sealed class DocumentMetadataTests
{
    private const string Header = """
        ---
        title: Uma tese
        author: Alguém
        bib: referencias.bib
        preset: abnt
        palavras-chave: uma chave que ninguém lê
        ---
        corpo
        """;

    [Fact]
    public void Le_as_quatro_chaves_conhecidas()
    {
        var metadata = DocumentMetadata.From(Header);

        Assert.Equal("Uma tese", metadata.Title);
        Assert.Equal("Alguém", metadata.Author);
        Assert.Equal("referencias.bib", metadata.Bibliography);
        Assert.Equal(TypographyPreset.Abnt, metadata.Typography);
    }

    [Fact]
    public void Sem_cabecalho_e_None()
    {
        Assert.Same(DocumentMetadata.None, DocumentMetadata.From("corpo comum"));
    }

    /// <summary>
    /// Chave desconhecida é silêncio; <b>valor</b> desconhecido é aviso.
    /// </summary>
    /// <remarks>
    /// Quem escreveu <c>preset: abtn</c> receberia o documento composto na norma errada sem nada na
    /// tela dizendo por quê. O nome cru sobrevive justamente para que a barra de status possa
    /// dizê-lo.
    /// </remarks>
    [Fact]
    public void Preset_que_nao_resolve_guarda_o_nome_e_nao_a_norma()
    {
        var metadata = DocumentMetadata.From("---\npreset: abtn\n---\ncorpo");

        Assert.Equal("abtn", metadata.PresetName);
        Assert.Null(metadata.Typography);
    }

    [Theory]
    [InlineData("abnt")]
    [InlineData("ABNT")]
    [InlineData("Abnt")]
    public void O_nome_da_norma_nao_e_sensivel_a_caixa(string name)
    {
        Assert.Equal(TypographyPreset.Abnt, DocumentMetadata.From($"---\npreset: {name}\n---\nc").Typography);
    }

    [Fact]
    public void A_norma_do_cabecalho_ganha_do_padrao()
    {
        var metadata = DocumentMetadata.From("---\npreset: abnt\n---\ncorpo");

        Assert.Equal(TypographyPreset.Abnt, metadata.NormOver(TypographyPreset.Default));
        Assert.Equal(TypographyPreset.Default, DocumentMetadata.None.NormOver(TypographyPreset.Default));
    }

    [Fact]
    public void O_titulo_do_cabecalho_resolve_o_marcador_da_faixa()
    {
        var bands = DocumentMetadata.From(Header).BandsOver(HeaderFooterSettings.Abnt);

        Assert.Equal("Uma tese", bands.DocumentTitle);

        // Sem título declarado, o que sobra é o que o aplicativo já punha lá: o nome do arquivo.
        var fallback = HeaderFooterSettings.Abnt with { DocumentTitle = "arquivo" };

        Assert.Equal("arquivo", DocumentMetadata.None.BandsOver(fallback).DocumentTitle);
    }
}
