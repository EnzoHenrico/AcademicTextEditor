using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Parsing;

public sealed class MarkupParserTests
{
    [Fact]
    public void Entrada_vazia_nao_produz_bloco()
    {
        var document = MarkupParser.Parse("");

        Assert.Empty(document.Blocks);
    }

    [Fact]
    public void Paragrafo_simples_vira_um_bloco_com_um_run()
    {
        var document = MarkupParser.Parse("Um parágrafo.");

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(document.Blocks));
        var run = Assert.Single(paragraph.Runs);

        Assert.Equal("Um parágrafo.", run.Text);
        Assert.Equal(0, run.SourceStart);
        Assert.Equal(TextStyle.Body, run.Style);
    }

    [Fact]
    public void Linha_em_branco_separa_paragrafos()
    {
        var document = MarkupParser.Parse("Primeiro.\n\nSegundo.");

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Primeiro.", SingleRunText(block)),
            block => Assert.Equal("Segundo.", SingleRunText(block)));
    }

    [Fact]
    public void Linhas_em_branco_consecutivas_nao_criam_paragrafo_vazio()
    {
        var document = MarkupParser.Parse("\n\n\nPrimeiro.\n\n\n\nSegundo.\n\n\n");

        Assert.Equal(2, document.Blocks.Count);
        Assert.All(document.Blocks, block => Assert.NotEmpty(block.Runs));
    }

    // O reflow é a razão de existir do editor: duas linhas seguidas na fonte são um parágrafo
    // só, e precisam quebrar de novo conforme a largura da página — não ficar presas ao \n.
    [Fact]
    public void Linhas_consecutivas_formam_um_paragrafo_com_um_run_por_linha()
    {
        var document = MarkupParser.Parse("Primeira linha\nsegunda linha");

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(document.Blocks));

        Assert.Collection(
            paragraph.Runs,
            run => Assert.Equal("Primeira linha ", run.Text),
            run => Assert.Equal("segunda linha", run.Text));
    }

    // O espaço de junção ocupa a posição do '\n', mantendo Text.Length igual ao trecho coberto
    // na fonte. É essa igualdade que deixa o caret ir da tela de volta ao offset sem tabela extra.
    [Fact]
    public void Espaco_de_juncao_ocupa_a_posicao_da_quebra_de_linha()
    {
        const string source = "abc\ndef";

        var document = MarkupParser.Parse(source);
        var runs = Assert.IsType<ParagraphNode>(Assert.Single(document.Blocks)).Runs;

        Assert.Equal(0, runs[0].SourceStart);
        Assert.Equal(4, runs[0].SourceEnd);
        Assert.Equal(4, runs[1].SourceStart);
        Assert.Equal(source.Length, runs[1].SourceEnd);
    }

    [Theory]
    [InlineData("# Título", 1)]
    [InlineData("## Título", 2)]
    [InlineData("### Título", 3)]
    [InlineData("#### Título", 4)]
    [InlineData("##### Título", 5)]
    [InlineData("###### Título", 6)]
    public void Heading_de_cada_nivel_e_reconhecido(string source, int expectedLevel)
    {
        var document = MarkupParser.Parse(source);

        var heading = Assert.IsType<HeadingNode>(Assert.Single(document.Blocks));
        var run = Assert.Single(heading.Runs);

        Assert.Equal(expectedLevel, heading.Level);
        Assert.Equal("Título", run.Text);
        Assert.Equal(expectedLevel + 1, run.SourceStart);
        Assert.Equal(FontWeightKind.Bold, run.Style.Weight);
    }

    [Theory]
    [InlineData("#hashtag")]                 // sem espaço depois do '#'
    [InlineData("#")]                        // '#' sozinho
    [InlineData("####### sete níveis")]      // além do ######
    [InlineData(" # com espaço antes")]      // a marcação vale só no início da linha
    public void Linha_que_nao_e_heading_vira_paragrafo(string source)
    {
        var document = MarkupParser.Parse(source);

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(document.Blocks));
        Assert.Equal(source, SingleRunText(paragraph));
    }

    [Fact]
    public void Heading_sem_texto_nao_produz_run()
    {
        var document = MarkupParser.Parse("# ");

        var heading = Assert.IsType<HeadingNode>(Assert.Single(document.Blocks));
        Assert.Empty(heading.Runs);
    }

    [Fact]
    public void Heading_encerra_o_paragrafo_anterior_sem_linha_em_branco()
    {
        var document = MarkupParser.Parse("Texto\n# Título\nMais texto");

        Assert.Collection(
            document.Blocks,
            block => Assert.IsType<ParagraphNode>(block),
            block => Assert.IsType<HeadingNode>(block),
            block => Assert.IsType<ParagraphNode>(block));
    }

    [Fact]
    public void Quebra_de_pagina_explicita_vira_bloco_proprio()
    {
        var document = MarkupParser.Parse("Antes\n\\page\nDepois");

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Antes", SingleRunText(block)),
            block => Assert.Empty(Assert.IsType<PageBreakNode>(block).Runs),
            block => Assert.Equal("Depois", SingleRunText(block)));
    }

    [Fact]
    public void Marcador_de_pagina_no_meio_da_linha_e_texto()
    {
        var document = MarkupParser.Parse("texto \\page texto");

        Assert.IsType<ParagraphNode>(Assert.Single(document.Blocks));
    }

    [Fact]
    public void Terminador_crlf_nao_entra_no_texto()
    {
        var document = MarkupParser.Parse("Primeiro.\r\n\r\nSegundo.\r\n");

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Primeiro.", SingleRunText(block)),
            block => Assert.Equal("Segundo.", SingleRunText(block)));
    }

    [Fact]
    public void Quebra_de_linha_final_nao_cria_bloco_extra()
    {
        var document = MarkupParser.Parse("Só um parágrafo.\n");

        Assert.Single(document.Blocks);
    }

    // Invariante estrutural do AST: os runs cobrem trechos válidos da fonte, em ordem e sem
    // sobreposição. Sem isso, a busca do caret pela linha corrente perde o sentido.
    [Fact]
    public void Runs_cobrem_a_fonte_em_ordem_e_sem_sobreposicao()
    {
        const string source = "# Título\n\nUm parágrafo\nque continua.\n\n\\page\n\n## Outro\nfim.";

        var runs = MarkupParser.Parse(source).Blocks.SelectMany(block => block.Runs).ToArray();

        Assert.NotEmpty(runs);

        var previousEnd = 0;
        foreach (var run in runs)
        {
            Assert.True(run.SourceStart >= previousEnd, $"run '{run.Text}' começa antes do fim do anterior");
            Assert.True(run.SourceEnd <= source.Length, $"run '{run.Text}' passa do fim da fonte");
            previousEnd = run.SourceEnd;
        }
    }

    private static string SingleRunText(BlockNode block) => Assert.Single(block.Runs).Text;
}
