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

        Assert.Equal(expectedLevel, heading.Level);
        Assert.Collection(
            heading.Runs,
            markup =>
            {
                // A marcação é um run como outro qualquer, e sai com o mesmo estilo do título:
                // revelá-la muda a largura da linha, nunca a altura.
                Assert.True(markup.IsMarkup);
                Assert.Equal(source[..(expectedLevel + 1)], markup.Text);
                Assert.Equal(0, markup.SourceStart);
                Assert.Equal(FontWeightKind.Bold, markup.Style.Weight);
            },
            text =>
            {
                Assert.False(text.IsMarkup);
                Assert.Equal("Título", text.Text);
                Assert.Equal(expectedLevel + 1, text.SourceStart);
                Assert.Equal(FontWeightKind.Bold, text.Style.Weight);
            });
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
        var text = heading.Runs.Single(run => !run.IsMarkup);

        Assert.Equal("", text.Text);
        Assert.Equal(FontWeightKind.Bold, text.Style.Weight);
        Assert.Equal(3, text.SourceStart);
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

    // Um heading tem dois runs: marcação e texto. O que interessa aos testes de conteúdo é o texto.
    // ------------------------------------------------------------------------------------------
    // Marcação inline.
    // ------------------------------------------------------------------------------------------

    private static readonly TextStyle Bold = TextStyle.Body with { Weight = FontWeightKind.Bold };
    private static readonly TextStyle Italic = TextStyle.Body with { Italic = true };
    private static readonly TextStyle BoldItalic = TextStyle.Body with
    {
        Weight = FontWeightKind.Bold,
        Italic = true,
    };

    [Fact]
    public void Negrito_vira_tres_runs_com_a_marcacao_no_mesmo_estilo()
    {
        var runs = RunsOf("a **b** c");

        Assert.Collection(
            runs,
            run => AssertRun(run, "a ", 0, TextStyle.Body, markup: false),
            run => AssertRun(run, "**", 2, Bold, markup: true),
            run => AssertRun(run, "b", 4, Bold, markup: false),
            run => AssertRun(run, "**", 5, Bold, markup: true),
            run => AssertRun(run, " c", 7, TextStyle.Body, markup: false));
    }

    [Fact]
    public void Italico_e_um_asterisco_so()
    {
        Assert.Collection(
            RunsOf("*x*"),
            run => AssertRun(run, "*", 0, Italic, markup: true),
            run => AssertRun(run, "x", 1, Italic, markup: false),
            run => AssertRun(run, "*", 2, Italic, markup: true));
    }

    [Fact]
    public void Tres_asteriscos_sao_negrito_e_italico()
    {
        Assert.Collection(
            RunsOf("***x***"),
            run => AssertRun(run, "***", 0, BoldItalic, markup: true),
            run => AssertRun(run, "x", 3, BoldItalic, markup: false),
            run => AssertRun(run, "***", 4, BoldItalic, markup: true));
    }

    [Fact]
    public void Enfase_aninha()
    {
        Assert.Collection(
            RunsOf("**a *b* c**"),
            run => AssertRun(run, "**", 0, Bold, markup: true),
            run => AssertRun(run, "a ", 2, Bold, markup: false),
            run => AssertRun(run, "*", 4, BoldItalic, markup: true),
            run => AssertRun(run, "b", 5, BoldItalic, markup: false),
            run => AssertRun(run, "*", 6, BoldItalic, markup: true),
            run => AssertRun(run, " c", 7, Bold, markup: false),
            run => AssertRun(run, "**", 9, Bold, markup: true));
    }

    // O que não casa é texto. O autor escreveu asteriscos, e asteriscos é o que ele vê — inclusive
    // no caso em que um fechamento acha a abertura de baixo e deixa a de cima solta.
    [Theory]
    [InlineData("a * b")]
    [InlineData("**sem par")]
    [InlineData("****")]
    [InlineData("2 * 3 * 4")]
    [InlineData("x_1 e y_2")]
    public void Sem_par_e_texto_literal(string source)
    {
        var run = Assert.Single(RunsOf(source));

        AssertRun(run, source, 0, TextStyle.Body, markup: false);
    }

    [Fact]
    public void Fechamento_devolve_ao_texto_a_abertura_que_ficou_por_cima()
    {
        Assert.Collection(
            RunsOf("**a *b**"),
            run => AssertRun(run, "**", 0, Bold, markup: true),
            run => AssertRun(run, "a *b", 2, Bold, markup: false),
            run => AssertRun(run, "**", 6, Bold, markup: true));
    }

    // Ênfase vazia sumiria da tela e deixaria o autor sem entender para onde foi o que digitou.
    [Fact]
    public void Enfase_vazia_nao_e_enfase()
    {
        var run = Assert.Single(RunsOf("a****b"));

        AssertRun(run, "a****b", 0, TextStyle.Body, markup: false);
    }

    // Não há regra de flanqueamento além do branco: colado em palavra, o asterisco enfatiza, que
    // é o que o CommonMark também faz.
    [Fact]
    public void Asterisco_colado_em_palavra_enfatiza()
    {
        Assert.Collection(
            RunsOf("pre*meio*pos"),
            run => AssertRun(run, "pre", 0, TextStyle.Body, markup: false),
            run => AssertRun(run, "*", 3, Italic, markup: true),
            run => AssertRun(run, "meio", 4, Italic, markup: false),
            run => AssertRun(run, "*", 8, Italic, markup: true),
            run => AssertRun(run, "pos", 9, TextStyle.Body, markup: false));
    }

    // Fechar um **negrito** dentro de um título não pode tirar o negrito do resto do título: o
    // estilo volta ao de fora, e não a "normal".
    [Fact]
    public void Enfase_dentro_de_titulo_volta_ao_estilo_do_titulo()
    {
        var heading = Assert.IsType<HeadingNode>(Assert.Single(MarkupParser.Parse("# a *b* c").Blocks));
        var title = heading.Runs[1].Style;

        Assert.Collection(
            heading.Runs,
            run => AssertRun(run, "# ", 0, title, markup: true),
            run => AssertRun(run, "a ", 2, title, markup: false),
            run => AssertRun(run, "*", 4, title with { Italic = true }, markup: true),
            run => AssertRun(run, "b", 5, title with { Italic = true }, markup: false),
            run => AssertRun(run, "*", 6, title with { Italic = true }, markup: true),
            run => AssertRun(run, " c", 7, title, markup: false));
    }

    // A invariante que mantém o caret funcionando: todo caractere da linha entra em exatamente um
    // run, na ordem, cópia literal.
    [Theory]
    [InlineData("a **b** c")]
    [InlineData("***x***")]
    [InlineData("**a *b* c**")]
    [InlineData("**a *b**")]
    [InlineData("a * b")]
    public void Os_runs_recobrem_a_linha_sem_buraco_nem_sobra(string source)
    {
        var runs = RunsOf(source);
        var position = 0;

        foreach (var run in runs)
        {
            Assert.Equal(position, run.SourceStart);
            position = run.SourceEnd;
        }

        Assert.Equal(source.Length, position);
        Assert.Equal(source, string.Concat(runs.Select(run => run.Text)));
    }

    private static IReadOnlyList<InlineRun> RunsOf(string source) =>
        Assert.Single(MarkupParser.Parse(source).Blocks).Runs;

    private static void AssertRun(InlineRun run, string text, int sourceStart, TextStyle style, bool markup)
    {
        Assert.Equal(text, run.Text);
        Assert.Equal(sourceStart, run.SourceStart);
        Assert.Equal(style, run.Style);
        Assert.Equal(markup, run.IsMarkup);
    }

    private static string SingleRunText(BlockNode block) =>
        Assert.Single(block.Runs, run => !run.IsMarkup).Text;
}
