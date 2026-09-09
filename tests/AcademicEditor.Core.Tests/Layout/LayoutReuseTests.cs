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

    // O caret atravessou a fronteira do bloco: a marcação revelada mudou de lugar, então dois
    // blocos mudaram de aparência sem ter mudado de texto. Aqui o motor tem de recusar — e recusar
    // significa medir tudo de novo, como se não houvesse layout anterior.
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
