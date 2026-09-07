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

    // Uma linha da fonte é uma linha na página: o parser não junta linhas consecutivas num
    // parágrafo só, como faria o CommonMark. Num editor paginado, o autor tem de ver o que digitou
    // — e a única quebra automática que resta é a da largura da página, que é do LineBreaker.
    [Fact]
    public void Cada_linha_da_fonte_vira_um_bloco()
    {
        var document = MarkupParser.Parse("Primeira linha\nsegunda linha");

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Primeira linha", SingleRunText(block)),
            block => Assert.Equal("segunda linha", SingleRunText(block)));
    }

    // A linha em branco não é separador descartável: ela ocupa espaço na folha e é onde o caret
    // fica depois de um Enter. Sem bloco, não há linha; sem linha, o caret some da tela.
    [Fact]
    public void Linha_em_branco_vira_bloco_com_run_vazio_no_offset_dela()
    {
        var document = MarkupParser.Parse("Primeiro.\n\nSegundo.");

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Primeiro.", SingleRunText(block)),
            block =>
            {
                var run = Assert.Single(block.Runs);

                Assert.Equal("", run.Text);

                // No offset da linha em branco, não em zero: é o que o line breaker usa como
                // SourceStart da linha, e é o offset onde o caret vai pousar.
                Assert.Equal(10, run.SourceStart);
                Assert.Equal(TextStyle.Body, run.Style);
            },
            block => Assert.Equal("Segundo.", SingleRunText(block)));
    }

    [Fact]
    public void Linhas_em_branco_consecutivas_viram_um_bloco_cada()
    {
        var document = MarkupParser.Parse("Primeiro.\n\n\nSegundo.");

        Assert.Equal(4, document.Blocks.Count);
        Assert.All(document.Blocks, block => Assert.Single(block.Runs));
    }

    // Uma linha só de espaços mantém os espaços: eles estão no arquivo, e o caret anda por dentro
    // deles. Apagá-los seria o parser reescrevendo o documento do autor.
    [Fact]
    public void Linha_so_de_espacos_preserva_os_espacos()
    {
        var document = MarkupParser.Parse("a\n   \nb");

        Assert.Equal("   ", SingleRunText(document.Blocks[1]));
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

    // Um heading recém-começado ("## " ainda sem título) precisa manter o estilo do nível, senão
    // a linha seria medida com a altura do corpo e saltaria de tamanho na primeira letra digitada.
    [Fact]
    public void Heading_sem_texto_mantem_um_run_vazio_com_o_estilo_do_nivel()
    {
        var document = MarkupParser.Parse("## ");

        var heading = Assert.IsType<HeadingNode>(Assert.Single(document.Blocks));
        var run = Assert.Single(heading.Runs);

        Assert.Equal("", run.Text);
        Assert.Equal(FontWeightKind.Bold, run.Style.Weight);
        Assert.Equal(3, run.SourceStart);
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
            block => Assert.Equal("", SingleRunText(block)),
            block => Assert.Equal("Segundo.", SingleRunText(block)),
            block => Assert.Equal("", SingleRunText(block)));
    }

    // Terminar em '\n' significa que existe uma linha depois — vazia, mas existe, e é exatamente
    // onde o caret está logo após o Enter. Sem este bloco, o caret cai fora de qualquer linha,
    // Locate devolve altura zero e a barra desaparece até outra tecla trazê-la de volta.
    [Fact]
    public void Quebra_de_linha_final_cria_a_linha_vazia_onde_o_caret_fica()
    {
        const string source = "Só um parágrafo.\n";

        var document = MarkupParser.Parse(source);

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("", SingleRunText(document.Blocks[1]));
        Assert.Equal(source.Length, Assert.Single(document.Blocks[1].Runs).SourceStart);
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
