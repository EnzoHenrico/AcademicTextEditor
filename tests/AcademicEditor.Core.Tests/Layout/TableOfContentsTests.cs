using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;
using AcademicEditor.Core.State;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// O sumário automático. Duas famílias de asserção, e a segunda é a que arrisca a fatia:
/// <b>o que se vê na folha</b> — entradas, recuo, condutor, número — e <b>o que o caret enxerga</b>,
/// que é nada: a entrada é a única linha do motor sem posição na fonte.
/// </summary>
public sealed class TableOfContentsTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 320.0, heightPt: 200.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Os_titulos_saem_na_ordem_sem_a_marcacao()
    {
        var headings = TableOfContents.Collect(
            MarkupParser.Parse("# Um\n\ncorpo\n\n### **Dois**\n\n:-: ## Tres"));

        Assert.Equal(3, headings.Count);
        Assert.Equal((1, "Um"), (headings[0].Level, headings[0].Title));

        // Sem os asteriscos e sem o ":-: ": o sumário mostra o título, não a marcação dele.
        Assert.Equal((3, "Dois"), (headings[1].Level, headings[1].Title));
        Assert.Equal((2, "Tres"), (headings[2].Level, headings[2].Title));
    }

    /// <remarks>
    /// Um <c>##&#160;</c> recém-aberto ainda não é título: a entrada seria uma linha só de pontinhos
    /// e um número, e sumiria na tecla seguinte.
    /// </remarks>
    [Fact]
    public void Titulo_vazio_nao_vira_entrada()
    {
        Assert.Empty(TableOfContents.Collect(MarkupParser.Parse("## \n\ncorpo")));
    }

    [Fact]
    public void O_marcador_ocupa_uma_linha_e_as_entradas_vem_depois()
    {
        var lines = Lines(Layout("\\toc\n\n# Um\n\n# Dois"));

        Assert.Equal(LineKind.TableOfContents, lines[0].Kind);
        Assert.Equal(0, lines[0].SourceStart);
        Assert.Empty(lines[0].Runs);

        Assert.Equal(LineKind.TocEntry, lines[1].Kind);
        Assert.Equal(LineKind.TocEntry, lines[2].Kind);
        Assert.Equal(LineKind.Text, lines[3].Kind);
    }

    [Fact]
    public void Sem_marcador_nao_ha_sumario()
    {
        Assert.DoesNotContain(Lines(Layout("# Um\n\ncorpo")), line => line.IsGenerated);
    }

    /// <summary>
    /// O número é o da folha em que o título caiu.
    /// </summary>
    [Fact]
    public void O_numero_e_o_da_folha_do_titulo()
    {
        var document = Layout("\\toc\n\n# Um\n\n\\page\n\n# Dois\n\n\\page\n\n# Tres");
        var entries = Lines(document).Where(line => line.IsGenerated).ToArray();

        Assert.Equal(["1", "2", "3"], entries.Select(entry => entry.Runs[^1].Text));
    }

    /// <summary>
    /// <b>O segundo passe não muda a geometria</b> — é a garantia que desfaz o ponto fixo.
    /// </summary>
    /// <remarks>
    /// Inserir o sumário empurra o texto e muda os números de página que o próprio sumário mostra.
    /// O roadmap previa dois passes e um erro de uma folha; ele não acontece porque a coluna do
    /// número tem largura <b>reservada</b>, e por isso escrever o número não muda a quebra de nada.
    /// Este teste fica vermelho no dia em que alguém trocar a reserva pela largura do número real.
    /// </remarks>
    [Theory]
    [InlineData("\\toc\n\n# Um\n\ncorpo")]
    [InlineData("\\toc\n\n# Um titulo bem comprido que enrola em mais de uma linha na entrada\n\ncorpo")]
    [InlineData("\\toc\n\n# Um\n\n\\page\n\n## Dois\n\n\\page\n\n### Tres")]
    // Título na fronteira exata da quebra: 26 caracteres, contra 24 que cabem com a reserva de
    // quatro dígitos e 27 que caberiam com a de um. É o caso em que trocar a reserva pela largura
    // do número real muda a CONTAGEM de linhas do sumário — e, com ela, a paginação.
    [InlineData("\\toc\n\n# abcde fghij klmno pqrstuvw\n\ncorpo")]
    public void Escrever_os_numeros_nao_move_folha_nenhuma(string source)
    {
        var ast = MarkupParser.Parse(source);
        var paginated = LayoutEngine.Layout(ast, Settings, Measurer);
        var filled = TableOfContents.Apply(paginated, ast, Measurer);

        // Metade da propriedade é que nada se moveu; a outra é que os números FORAM escritos. Sem
        // esta asserção, um Apply que recusasse — e recusar é o que ele faz quando as contagens não
        // batem — passaria verde devolvendo o documento intacto.
        Assert.Contains(
            Lines(filled).Where(line => line.IsGenerated),
            line => line.Runs.Count > 0 && line.Runs[^1].Text.All(char.IsAsciiDigit));

        Assert.Equal(paginated.Pages.Count, filled.Pages.Count);

        for (var page = 0; page < paginated.Pages.Count; page++)
        {
            var before = paginated.Pages[page].Lines;
            var after = filled.Pages[page].Lines;

            Assert.Equal(before.Count, after.Count);

            for (var line = 0; line < before.Count; line++)
            {
                Assert.Equal(before[line].YPt, after[line].YPt);
                Assert.Equal(before[line].HeightPt, after[line].HeightPt);
                Assert.Equal(before[line].Kind, after[line].Kind);
            }
        }
    }

    /// <summary>
    /// As entradas ficam <b>fora</b> do índice — nenhuma reivindica offset nenhum.
    /// </summary>
    /// <remarks>
    /// A forma forte da decisão: não basta a entrada não ter offset, ela não pode aparecer no mapa
    /// que traduz offset em linha. Uma entrada com <c>SourceStart</c> zero — que é o que um array
    /// dimensionado a mais deixaria no fim — capturaria o caret no começo do documento.
    /// </remarks>
    [Fact]
    public void Nenhuma_entrada_entra_no_indice()
    {
        var document = Layout("\\toc\n\n# Um\n\n# Dois\n\ncorpo");

        Assert.Contains(Lines(document), line => line.IsGenerated);

        foreach (var reference in document.Index)
        {
            Assert.False(document.Pages[reference.PageIndex].Lines[reference.LineIndex].IsGenerated);
        }

        // E o índice tem exatamente as linhas que não são geradas — nem uma sobrando no fim.
        Assert.Equal(Lines(document).Count(line => !line.IsGenerated), document.Index.Count);
    }

    /// <summary>
    /// <b>O caret nunca pousa numa entrada</b>, venha de onde vier.
    /// </summary>
    /// <remarks>
    /// Teste de propriedade, e não de exemplo: são três caminhos independentes até uma linha — o
    /// offset (Locate), a tecla (↑/↓) e o clique (AtPoint) —, e cada um deles chegou a uma linha
    /// gerada por um caminho diferente enquanto esta fatia era escrita.
    /// </remarks>
    [Theory]
    [InlineData("\\toc\n\n# Um\n\n# Dois\n\ncorpo")]
    [InlineData("# Antes\n\n\\toc\n\n## Depois\n\ncorpo")]
    public void O_caret_nunca_pousa_numa_entrada(string source)
    {
        var document = Layout(source);

        for (var offset = 0; offset <= source.Length; offset++)
        {
            Assert.False(
                LineAt(document, CaretGeometry.Locate(offset, document, Measurer)).IsGenerated,
                $"o offset {offset} pousou numa entrada do sumário");
        }
    }

    [Fact]
    public void A_seta_para_baixo_atravessa_o_sumario_sem_parar_nele()
    {
        var source = "\\toc\n\n# Um\n\n# Dois\n\ncorpo";
        var document = Layout(source);
        var caret = new Caret(0, 0.0);

        // Uma tecla por linha desenhada seria mais que suficiente para varrer o documento; o que
        // interessa é que nenhuma delas pare numa entrada.
        for (var step = 0; step < Lines(document).Count; step++)
        {
            caret = CaretNavigator.MoveDown(caret, document, Measurer);

            Assert.False(LineAt(document, CaretGeometry.Locate(caret.Offset, document, Measurer)).IsGenerated);
        }

        // E chegou ao fim: uma travessia que parasse no sumário não teria saído dele.
        Assert.Equal(source.Length, caret.Offset);
    }

    /// <summary>
    /// O clique numa entrada pousa no marcador, que é o que o autor tem para editar.
    /// </summary>
    [Fact]
    public void Clicar_numa_entrada_pousa_no_marcador()
    {
        var document = Layout("\\toc\n\n# Um\n\n# Dois");
        var lines = document.Pages[0].Lines;
        var entry = lines.First(line => line.IsGenerated);

        var caret = CaretNavigator.AtPoint(0, xPt: 5.0, entry.YPt + 1.0, document, Measurer);

        Assert.Equal(0, caret.Offset);
    }

    /// <summary>
    /// A margem vale para o sumário, nos dois presets.
    /// </summary>
    /// <remarks>
    /// O condutor de pontos e o número são texto novo chegando à margem direita, que é exatamente a
    /// família de bug da Fatia 4.1 — lá foi o alinhamento que desfez a garantia da 5.3, e nada ficou
    /// vermelho porque ninguém tinha afirmado a garantia naquele caminho.
    /// </remarks>
    [Theory]
    [InlineData("\\toc\n\n# Um\n\n## Dois\n\n### Um titulo comprido que vai ter de enrolar aqui\n\ncorpo")]
    [InlineData("\\toc\n\n###### Seis niveis de recuo com um titulo que tambem enrola bastante\n\ncorpo")]
    public void A_margem_vale_para_o_sumario(string source)
    {
        foreach (var preset in new[] { TypographyPreset.Default, TypographyPreset.Abnt })
        {
            var ast = MarkupParser.Parse(source, preset);
            var document = LayoutEngine.Publish(
                ast, Settings, Measurer, HeaderFooterSettings.None, preset: preset);

            MarginInvariants.AssertHolds(
                [.. Lines(document).Where(line => line.IsGenerated)],
                Settings.ContentWidthPt,
                Measurer,
                Measurer.MeasureWidthPt(" ", preset.Body));
        }
    }

    private static PaginatedDocument Layout(string source)
    {
        var ast = MarkupParser.Parse(source);

        return LayoutEngine.Publish(ast, Settings, Measurer, HeaderFooterSettings.None);
    }

    private static List<LaidOutLine> Lines(PaginatedDocument document) =>
        [.. document.Pages.SelectMany(page => page.Lines)];

    /// <summary>
    /// A linha em que o caret foi desenhado, achada pela <b>geometria</b>.
    /// </summary>
    /// <remarks>
    /// Pela geometria, e não pelo índice da linha, porque é assim que o autor a vê: a barra
    /// aparece dentro de uma caixa, e a pergunta é de quem é a caixa. Também é o que mantém o teste
    /// do lado de fora do <c>internal</c> do Core.
    /// </remarks>
    private static LaidOutLine LineAt(PaginatedDocument document, CaretPosition position) =>
        document.Pages[position.PageIndex].Lines.First(line =>
            position.YPt >= line.YPt && position.YPt < line.YPt + line.HeightPt);
}
