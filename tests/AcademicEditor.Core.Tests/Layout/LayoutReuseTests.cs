using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// O reflow incremental. A asserção que importa não é o ganho — é a <b>identidade</b>: reaproveitar
/// tem de devolver exatamente o documento que a paginação completa devolveria, linha por linha e
/// offset por offset. Um reaproveitamento errado não quebra o desenho, quebra o caret, e isso
/// aparece longe daqui.
/// </summary>
public sealed class LayoutReuseTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Theory]
    // Digitar no meio de um parágrafo, que é o caso que a fatia existe para resolver.
    [InlineData("aaa bbb\n\nccc ddd\n\neee fff", "aaa bXbb\n\nccc ddd\n\neee fff", 5)]
    // No primeiro bloco: nada antes dele para reaproveitar, tudo depois desloca.
    [InlineData("aaa bbb\n\nccc ddd", "Xaaa bbb\n\nccc ddd", 0)]
    // No último: tudo antes é reaproveitado sem deslocar nada.
    [InlineData("aaa bbb\n\nccc ddd", "aaa bbb\n\nccc dddX", 16)]
    // Apagar encolhe o documento: o deslocamento é negativo.
    [InlineData("aaa bbb\n\nccc ddd\n\neee", "aaa bb\n\nccc ddd\n\neee", 6)]
    // Alteração que faz o bloco quebrar em mais linhas do que tinha.
    [InlineData("aaa\n\nbbb", "aaa aaa aaa aaa aaa\n\nbbb", 19)]
    // Num heading, onde a linha não começa no início do bloco quando a marcação está escondida.
    [InlineData("# Tít\n\ncorpo", "# Títu\n\ncorpo", 6)]
    // Sobre uma quebra de página, que é linha atômica e força folha nova.
    [InlineData("aaa\n\n\\page\n\nbbb ccc", "aaa\n\n\\page\n\nbbb Xccc", 16)]
    // Com sumário: as entradas dele não estão no índice, então o laço do reaproveitamento nem as
    // vê — quem as devolve é a remontagem do bloco \toc, e é isso que este caso guarda.
    [InlineData("\\toc\n\n# Tit\n\ncorpo", "\\toc\n\n# Tit\n\ncorpoX", 20)]
    // Editar o próprio título: o texto da entrada muda junto, na mesma tecla.
    [InlineData("\\toc\n\n# Tit\n\ncorpo", "\\toc\n\n# Titu\n\ncorpo", 11)]
    public void Reaproveitar_devolve_o_mesmo_documento_que_paginar_do_zero(string before, string after, int caret)
    {
        var previous = Layout(before, caret);
        var reuse = LayoutReuse.Between(before, after, previous);

        Assert.NotNull(reuse);

        var reused = Layout(after, caret, reuse);
        var full = Layout(after, caret);

        AssertSame(full, reused);
    }

    // Sem isto o reflow seria só rápido, não correto: se o caminho incremental nunca fosse tomado,
    // os testes de identidade acima passariam comparando a paginação completa com ela mesma. O
    // medidor é a testemunha — só o bloco requebrado chega até ele.
    [Fact]
    public void So_o_bloco_sujo_e_remedido()
    {
        var before = Paragraphs(count: 10);
        var after = before.Insert(1, "X");

        var full = MeasureCalls(after, caretOffset: 1, reuse: null);
        var incremental = MeasureCalls(after, caretOffset: 1, LayoutReuse.Between(before, after, Layout(before, 1)));

        Assert.Equal(30, full);
        Assert.Equal(3, incremental);
    }

    /// <summary>
    /// A tecla digitada no <b>fim</b> de um parágrafo reaproveita, que é como se escreve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O caret chega aqui <b>pós-edição</b> — quem digita move o caret e só então pede a
    /// repaginação. Comparar esse caret com o intervalo <i>antigo</i> do bloco recusava toda tecla
    /// no fim de um parágrafo, e o motor paginava o documento inteiro do zero a cada uma delas.
    /// </para>
    /// <para>
    /// Nenhum teste pegava porque todos passavam o <b>mesmo</b> caret ao layout anterior e ao novo,
    /// o que só descreve uma tecla digitada no meio do texto.
    /// </para>
    /// </remarks>
    [Fact]
    public void Tecla_no_fim_do_paragrafo_reaproveita()
    {
        var before = Paragraphs(count: 10);

        // Fim do primeiro bloco: "aa bb" ocupa 0..5, então o caret pré-edição está em 5.
        var after = before.Insert(5, "X");
        var reuse = LayoutReuse.Between(before, after, Layout(before, caretOffset: 5));

        Assert.NotNull(reuse);
        Assert.Equal(3, MeasureCalls(after, caretOffset: 6, reuse));
        AssertSame(Layout(after, caretOffset: 6), Layout(after, caretOffset: 6, reuse));
    }

    /// <summary>
    /// Com sumário, a tecla continua custando uma tecla — mais o sumário.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O guard conta medições, não milissegundos</b>, pela mesma razão da Fatia 4.2: um teto de
    /// tempo passa verde numa máquina rápida com o reflow recusando toda tecla.
    /// </para>
    /// <para>
    /// <b>E o número não é o mesmo de um documento sem sumário</b>, de propósito. As entradas são
    /// remontadas a cada publicação — é o que faz um título editado aparecer no sumário na mesma
    /// tecla —, e isso custa medir os títulos outra vez. O custo é proporcional ao número de
    /// títulos, não ao tamanho do documento, e é o que este teste pina: com o cache de medição do
    /// aplicativo por baixo, cada uma dessas medições é busca em dicionário.
    /// </para>
    /// </remarks>
    [Fact]
    public void Com_sumario_a_tecla_continua_reaproveitando()
    {
        var before = "\\toc\n\n# Tit\n\n" + Paragraphs(count: 10);
        var after = before.Insert(before.Length - 1, "X");
        var caret = after.Length - 1;

        var reuse = LayoutReuse.Between(before, after, Layout(before, caret));

        Assert.NotNull(reuse);

        var full = MeasureCalls(after, caret, reuse: null);
        var incremental = MeasureCalls(after, caret, reuse);

        // A conta fecha inteira, e é ela que diz de onde vem cada medição: o caminho completo mede
        // os dez parágrafos (3 cada), o título (1) e o sumário (4 — a reserva do número, o branco,
        // o ponto e o título da entrada). O incremental mede o bloco sujo (3) e o mesmo sumário.
        Assert.Equal(35, full);
        Assert.Equal(7, incremental);
    }

    /// <summary>
    /// Deslocar uma linha reaproveitada não copia os runs dela.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A propriedade que sustenta o custo de uma tecla, afirmada pela <b>estrutura</b> e não pelo
    /// relógio: o offset de cada run é relativo ao começo da linha, então mover a linha no buffer é
    /// uma cópia de record e o array de runs continua sendo o mesmo objeto.
    /// </para>
    /// <para>
    /// Com offset absoluto isto reescrevia run a run. À esquerda são um ou dois por linha, mas
    /// justificado são ~20 — a justificação parte a linha em cada fronteira de branco —, e digitar
    /// no meio de uma tese desloca metade do documento.
    /// </para>
    /// </remarks>
    [Fact]
    public void Deslocar_uma_linha_reaproveitada_nao_copia_os_runs()
    {
        var before = Paragraphs(count: 10);
        var after = before.Insert(1, "X");
        var previous = Layout(before, caretOffset: 1);
        var reused = Layout(after, caretOffset: 2, LayoutReuse.Between(before, after, previous));

        var was = previous.Pages.SelectMany(page => page.Lines).ToArray()[^1];
        var now = reused.Pages.SelectMany(page => page.Lines).ToArray()[^1];

        // A última linha veio depois do bloco sujo, então o offset dela andou um caractere.
        Assert.Equal(was.SourceStart + 1, now.SourceStart);
        Assert.Same(was.Runs, now.Runs);
    }

    /// <summary>
    /// Atravessar bloco sem editar requebra <b>dois</b> blocos e reaproveita o resto.
    /// </summary>
    /// <remarks>
    /// Revelar a marcação é uma troca: ela sai de um bloco e aparece noutro. O caminho incremental
    /// só sabia requebrar um, então recusava — e recusar, num editor em que <i>uma linha da fonte é
    /// uma linha na página</i>, custava uma repaginação completa a cada ↑/↓.
    /// </remarks>
    [Fact]
    public void Travessia_de_bloco_requebra_dois_e_reaproveita_o_resto()
    {
        var source = Paragraphs(count: 10);
        var previous = Layout(source, caretOffset: 0);
        var reuse = LayoutReuse.Between(source, source, previous);

        Assert.NotNull(reuse);

        // Três medições por bloco requebrado — as duas palavras e o espaço —, e são dois blocos.
        Assert.Equal(6, MeasureCalls(source, caretOffset: 7, reuse));
        AssertSame(Layout(source, caretOffset: 7), Layout(source, caretOffset: 7, reuse));
    }

    /// <summary>
    /// O bloco que <b>perde</b> a revelação é requebrado com a marcação escondida.
    /// </summary>
    /// <remarks>
    /// A armadilha do caminho de dois blocos: requebrar sempre com <c>includeMarkup: true</c> era
    /// correto enquanto o único bloco requebrado era o revelado. Com dois, o que perdeu a revelação
    /// sairia mostrando o <c>## </c> depois de o caret ter saído dele.
    /// </remarks>
    [Fact]
    public void O_bloco_que_perde_a_revelacao_esconde_a_marcacao()
    {
        const string Source = "# Tit\n\naa bb";

        var previous = Layout(Source, caretOffset: 0);
        var reuse = LayoutReuse.Between(Source, Source, previous);

        Assert.NotNull(reuse);

        var reused = Layout(Source, caretOffset: 9, reuse);

        Assert.Equal("# Tit", TextOf(previous.Pages[0].Lines[0]));
        Assert.Equal("Tit", TextOf(reused.Pages[0].Lines[0]));
        AssertSame(Layout(Source, caretOffset: 9), reused);
    }

    // Editar num bloco com o caret noutro: aqui o motor continua tendo de recusar, porque o bloco
    // sujo e o revelado são dois, e o revelado não é o que mudou de texto. Recusar significa medir
    // tudo de novo, como se não houvesse layout anterior.
    [Fact]
    public void Caret_em_outro_bloco_e_recusado_e_remede_tudo()
    {
        var before = Paragraphs(count: 10);
        var after = before.Insert(1, "X");

        // Editou no primeiro bloco, mas o caret está no último: o revelado saiu do bloco sujo.
        var caret = after.Length - 1;
        var reuse = LayoutReuse.Between(before, after, Layout(before, caret));

        Assert.NotNull(reuse);
        Assert.Equal(30, MeasureCalls(after, caret, reuse));
        AssertSame(Layout(after, caret), Layout(after, caret, reuse));
    }

    [Theory]
    // Enter e Backspace numa fronteira mudam quantos blocos existem: o mapa um-para-um cai.
    [InlineData("aaa\n\nbbb", "aaa\n\n\nbbb")]
    [InlineData("aaa\n\nbbb", "aaa\nbbb")]
    // Colar um parágrafo inteiro.
    [InlineData("aaa\n\nbbb", "aaa\n\nnovo\n\nbbb")]
    public void Alteracao_que_mexe_em_quebra_de_linha_recusa_o_reaproveitamento(string before, string after)
    {
        Assert.Null(LayoutReuse.Between(before, after, Layout(before, 0)));
    }

    [Fact]
    public void Sem_texto_anterior_nao_ha_o_que_reaproveitar()
    {
        Assert.Null(LayoutReuse.Between(string.Empty, "aaa", Layout("aaa", 0)));
    }

    [Fact]
    public void Geometria_diferente_cai_para_a_paginacao_completa()
    {
        const string Before = "aaa bbb\n\nccc";
        const string After = "aaa bXbb\n\nccc";

        var previous = Layout(Before, caretOffset: 5);
        var narrow = PageSettings.Uniform(widthPt: 80.0, heightPt: 120.0, marginPt: 10.0);

        var reused = LayoutEngine.Layout(
            MarkupParser.Parse(After), narrow, Measurer, 5, LayoutReuse.Between(Before, After, previous));

        AssertSame(LayoutEngine.Layout(MarkupParser.Parse(After), narrow, Measurer, 5), reused);
    }

    /// <summary>
    /// A mesma identidade, com o documento justificado.
    /// </summary>
    /// <remarks>
    /// O alinhamento é aplicado dentro do <c>LineBreaker</c>, então o bloco sujo é requebrado
    /// <b>já alinhado</b> enquanto os intocados vêm alinhados de antes. Se as duas metades
    /// discordassem, a linha requebrada sairia deslocada em relação às vizinhas — e o caret com
    /// ela. Este é o teste que prova que não discordam.
    /// </remarks>
    [Theory]
    [InlineData("aaa bbb ccc ddd\n\neee fff", "aaa bXbb ccc ddd\n\neee fff", 5)]
    [InlineData("aa bb cc dd ee ff\n\ngg hh", "aa bb cc dd ee ff\n\ngg hhX", 24)]
    public void Reaproveitar_devolve_o_mesmo_documento_com_o_texto_justificado(
        string before,
        string after,
        int caret)
    {
        var justified = TypographyPreset.Default with { Alignment = TextAlignment.Justify };

        var previous = LayoutEngine.Layout(
            MarkupParser.Parse(before, justified), Settings, Measurer, caret, preset: justified);

        var reuse = LayoutReuse.Between(before, after, previous);
        Assert.NotNull(reuse);

        var reused = LayoutEngine.Layout(
            MarkupParser.Parse(after, justified), Settings, Measurer, caret, reuse, justified);

        var full = LayoutEngine.Layout(
            MarkupParser.Parse(after, justified), Settings, Measurer, caret, preset: justified);

        AssertSame(full, reused);
    }

    private static void AssertSame(PaginatedDocument expected, PaginatedDocument actual)
    {
        Assert.Equal(expected.RevealedBlock, actual.RevealedBlock);
        Assert.Equal(expected.Pages.Count, actual.Pages.Count);

        for (var page = 0; page < expected.Pages.Count; page++)
        {
            Assert.Equal(expected.Pages[page].Lines.Count, actual.Pages[page].Lines.Count);

            for (var index = 0; index < expected.Pages[page].Lines.Count; index++)
            {
                var wanted = expected.Pages[page].Lines[index];
                var got = actual.Pages[page].Lines[index];

                Assert.Equal(wanted.SourceStart, got.SourceStart);
                Assert.Equal(wanted.SourceLength, got.SourceLength);
                Assert.Equal(wanted.YPt, got.YPt);
                Assert.Equal(wanted.HeightPt, got.HeightPt);
                Assert.Equal(wanted.Kind, got.Kind);
                Assert.Equal(wanted.Runs, got.Runs);
            }
        }
    }

    private static PaginatedDocument Layout(string source, int caretOffset, LayoutReuse? reuse = null) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer, caretOffset, reuse);

    private static string TextOf(LaidOutLine line) => string.Concat(line.Runs.Select(run => run.Text));

    /// <summary>Quantos trechos chegaram ao medidor — a testemunha de quanto foi refeito.</summary>
    private static int MeasureCalls(string source, int caretOffset, LayoutReuse? reuse)
    {
        var counting = new CountingMeasurer();

        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, counting, caretOffset, reuse);

        return counting.WidthCalls;
    }

    /// <summary>Parágrafos separados por linha em branco.</summary>
    /// <remarks>
    /// Curtos de propósito: cabem numa linha, então cada um custa exatamente três medições — as
    /// duas palavras e o espaço entre elas — e a conta fecha sem depender da quebra por largura.
    /// A linha em branco não custa nenhuma: bloco vazio não tem chunk.
    /// </remarks>
    private static string Paragraphs(int count) =>
        string.Join("\n\n", Enumerable.Repeat("aa bb", count));

    private sealed class CountingMeasurer : ITextMeasurer
    {
        private readonly FakeTextMeasurer _inner = new();

        public int WidthCalls { get; private set; }

        public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style)
        {
            WidthCalls++;
            return _inner.MeasureWidthPt(text, style);
        }

        public LineMetrics GetLineMetrics(TextStyle style) => _inner.GetLineMetrics(style);
    }
}
