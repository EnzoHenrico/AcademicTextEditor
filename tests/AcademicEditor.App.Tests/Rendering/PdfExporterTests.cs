using System.Text;

using AcademicEditor.App.Rendering;

using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

using SkiaSharp;

namespace AcademicEditor.App.Tests.Rendering;

/// <summary>
/// A exportação PDF, verificada pela estrutura do arquivo gerado.
/// </summary>
/// <remarks>
/// Não há biblioteca de leitura de PDF aqui, e não precisa haver: o que a fatia promete —
/// tamanho de folha, contagem de páginas, fonte embutida e texto pesquisável — são objetos
/// nomeados no arquivo, e procurá-los pelo nome é uma verificação direta, não um proxy.
/// </remarks>
public sealed class PdfExporterTests
{
    private static readonly TextStyle Style = TypographyPreset.Abnt.Body;

    [Fact]
    public void O_arquivo_gerado_e_um_PDF_completo()
    {
        var pdf = Export(Document(pages: 3));

        Assert.StartsWith("%PDF-", pdf, StringComparison.Ordinal);
        Assert.Contains("%%EOF", pdf, StringComparison.Ordinal);
    }

    /// <remarks>
    /// 595 × 842pt é A4, e é a folha do motor chegando ao papel sem conversão nenhuma: o canvas de
    /// PDF do Skia já é em pontos tipográficos, que é a unidade interna do layout. O
    /// <c>PageRenderer</c> multiplica por 96/72 porque a tela é que tem outra unidade.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void Cada_folha_do_documento_vira_uma_pagina_A4(int pages)
    {
        var pdf = Export(Document(pages));

        Assert.Equal(pages, Occurrences(pdf, "/MediaBox [0 0 595 842]"));
        Assert.Contains($"/Count {pages}", pdf, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <c>/FontFile2</c> é a fonte TrueType embutida e <c>/ToUnicode</c> é o mapa que devolve os
    /// caracteres a quem seleciona ou pesquisa. São as duas metades da promessa da fatia: sem a
    /// primeira o PDF depende das fontes da máquina que o abrir; sem a segunda ele desenha texto
    /// que não se copia — e um PDF de tese que não se pesquisa não serve.
    /// </remarks>
    [Fact]
    public void A_fonte_vai_embutida_e_o_texto_continua_pesquisavel()
    {
        var pdf = Export(Document(pages: 1));

        Assert.Contains("/FontFile2", pdf, StringComparison.Ordinal);
        Assert.Contains("/ToUnicode", pdf, StringComparison.Ordinal);
    }

    /// <remarks>
    /// A família do preset traz alternativas separadas por vírgula, e percorrer a lista é
    /// obrigatório: passá-la inteira como um nome não casaria com fonte nenhuma, o Skia cairia na
    /// padrão dele, e o PDF sairia numa fonte diferente da que está na tela. O teste é independente
    /// de máquina porque a família de verdade sai do próprio gerenciador de fontes.
    /// </remarks>
    [Fact]
    public void A_lista_de_familias_e_percorrida_ate_achar_uma_instalada()
    {
        var installed = SKFontManager.Default.GetFontFamilies().First();
        var style = Style with { FontFamily = $"Fonte Que Nao Existe, {installed}" };

        // O PDF escreve o nome da família sem espaços, atrás do prefixo do subconjunto.
        Assert.Contains(installed.Replace(" ", string.Empty, StringComparison.Ordinal), Export(Document(1, style)), StringComparison.Ordinal);
    }

    /// <remarks>
    /// <b>O PDF desenha o documento, não o editor.</b> O filete tracejado do <c>\page</c> existe
    /// para quem está escrevendo — o marcador já fez o trabalho dele quando a folha terminou ali.
    /// Uma folha só com ele não tem glifo nenhum a desenhar, e a prova disso é o arquivo não
    /// precisar embutir fonte alguma.
    /// </remarks>
    [Fact]
    public void O_marcador_de_quebra_de_pagina_nao_vai_para_o_papel()
    {
        var marker = new LaidOutLine(0.0, 20.0, 16.0, [], 0, 5, LineKind.PageBreak);
        var onlyMarker = new PaginatedDocument([new PageLayout([marker])], PageSettings.A4)
        {
            Typography = TypographyPreset.Abnt,
        };

        Assert.DoesNotContain("/FontFile2", Export(onlyMarker), StringComparison.Ordinal);
    }

    /// <remarks>
    /// O espelho do teste do marcador, e é o que prova que a faixa chega ao papel: uma folha
    /// <b>sem linha alguma</b> e só com cabeçalho ainda tem glifo a desenhar, então o arquivo
    /// precisa embutir fonte. Se o exportador ignorasse <c>PageLayout.Header</c>, não precisaria —
    /// e o número da página sumiria do PDF sem sumir da tela.
    /// </remarks>
    [Fact]
    public void A_faixa_de_cabecalho_chega_ao_papel()
    {
        var page = new PageLayout([])
        {
            Header = [new BandRun("1", Style, XPt: 400.0, BaselinePt: 80.0)],
        };

        var document = new PaginatedDocument([page], PageSettings.A4)
        {
            Typography = TypographyPreset.Abnt,
        };

        Assert.Contains("/FontFile2", Export(document), StringComparison.Ordinal);
    }

    /// <remarks>
    /// Escrever num temporário e mover por cima, como o save do <c>.md</c>: morrer no meio da
    /// escrita deixaria um PDF pela metade no lugar do anterior, que já não existiria para
    /// recuperar. O que este teste guarda é o outro lado — que o temporário não fica para trás.
    /// </remarks>
    [Fact]
    public void Exportar_substitui_o_arquivo_e_nao_deixa_temporario()
    {
        var directory = Directory.CreateTempSubdirectory("academiceditor-pdf");

        try
        {
            var path = Path.Combine(directory.FullName, "tese.pdf");

            File.WriteAllText(path, "conteúdo anterior");
            PdfExporter.Export(Document(pages: 2), path, "Tese");

            Assert.StartsWith("%PDF-", File.ReadAllText(path, Encoding.Latin1), StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static PaginatedDocument Document(int pages, TextStyle? style = null)
    {
        var used = style ?? Style;
        var line = new LaidOutLine(
            0.0,
            20.0,
            16.0,
            [new LaidOutRun("Documento de teste", used, 0.0, 100.0, 0)],
            0,
            18);

        return new PaginatedDocument(
            Enumerable.Range(0, pages).Select(_ => new PageLayout([line])).ToArray(),
            PageSettings.A4)
        {
            Typography = TypographyPreset.Abnt,
        };
    }

    /// <summary>
    /// Título e autor do cabeçalho de metadados chegam às propriedades do PDF.
    /// </summary>
    /// <remarks>
    /// É o que faz um leitor mostrar o nome do trabalho na barra do título em vez de
    /// "Dissertacao_v3_FINAL". Objetos nomeados no arquivo, procurados pelo nome — verificação
    /// direta, e não proxy, que é a política destes testes desde a Fatia 3.
    /// </remarks>
    [Fact]
    public void O_titulo_e_o_autor_entram_nas_propriedades_do_documento()
    {
        using var buffer = new MemoryStream();

        PdfExporter.Export(Document(1), buffer, "Uma tese paginada", "Alguem");

        var pdf = Encoding.Latin1.GetString(buffer.ToArray());

        Assert.Contains("/Title (Uma tese paginada)", pdf, StringComparison.Ordinal);
        Assert.Contains("/Author (Alguem)", pdf, StringComparison.Ordinal);
    }

    // Latin1 porque um PDF mistura estrutura em ASCII com fluxos binários: é a codificação que
    // mapeia cada byte a um caractere sem perder nenhum, e é só a estrutura que se procura aqui.
    private static string Export(PaginatedDocument document)
    {
        using var buffer = new MemoryStream();

        PdfExporter.Export(document, buffer, "Teste");

        return Encoding.Latin1.GetString(buffer.ToArray());
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
