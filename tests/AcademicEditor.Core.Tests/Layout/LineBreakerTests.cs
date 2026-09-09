using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Layout;

public sealed class LineBreakerTests
{
    // 10pt por caractere do corpo, 20pt de altura: uma linha de 100pt cabe 10 caracteres.
    private const double MaxWidthPt = 100.0;
    private const double BodyLineHeightPt = 20.0;

    private static readonly FakeTextMeasurer Measurer = new();

    private static readonly TextStyle Large = TextStyle.Body with { FontSizePt = 22.0, Weight = FontWeightKind.Bold };

    [Fact]
    public void Texto_que_cabe_fica_numa_linha_so()
    {
        var lines = Break("abc def");

        var line = Assert.Single(lines);
        Assert.Equal("abc def", TextOf(line));
        Assert.Equal(BodyLineHeightPt, line.HeightPt);
        Assert.Equal(BodyLineHeightPt * FakeTextMeasurer.BaselineRatio, line.BaselinePt);
    }

    [Fact]
    public void Quebra_na_palavra_que_nao_cabe()
    {
        var lines = Break("aaa bbb ccc ddd");

        Assert.Collection(
            lines,
            line => Assert.Equal("aaa bbb ", TextOf(line)),
            line => Assert.Equal("ccc ddd", TextOf(line)));
    }

    // A tolerância de um branco. Sem ela o espaço desceria e a linha de baixo nasceria indentada
    // por um caractere que não desenha nada — o que acontece em uma quebra a cada seis.
    [Fact]
    public void Um_espaco_pode_passar_da_margem()
    {
        var lines = Break("abcdefghij ");

        var line = Assert.Single(lines);
        Assert.Equal("abcdefghij ", TextOf(line));
    }

    // O mesmo com o espaço dentro da margem — é o que mantém a fronteira compartilhada de que a
    // CaretAffinity depende, e é a esmagadora maioria dos casos.
    [Fact]
    public void Espaco_que_cabe_fica_no_fim_da_linha()
    {
        var lines = Break("aaa bbbbbbbb");

        Assert.Collection(
            lines,
            line => Assert.Equal("aaa ", TextOf(line)),
            line => Assert.Equal("bbbbbbbb", TextOf(line)));
    }

    // A tolerância é de UM caractere, não do grupo: dois espaços cabem, o terceiro é a tolerância,
    // o quarto desce. Brancos além disso o autor digitou de propósito, e têm lugar na tela.
    [Fact]
    public void Grupo_de_espacos_desce_o_que_passa_da_tolerancia()
    {
        var lines = Break("aaaaaaaa    bbb");

        Assert.Collection(
            lines,
            line => Assert.Equal("aaaaaaaa   ", TextOf(line)),
            line => Assert.Equal(" bbb", TextOf(line)));
    }

    // Espelho da palavra impartível: o grupo de brancos mais largo que a página é o outro jeito de
    // travar o laço, porque ali o corte que cabe é zero — quem faz progredir é a tolerância.
    [Fact]
    public void Espacos_mais_largos_que_a_pagina_progridem()
    {
        var spaces = new string(' ', 25);

        var lines = Break(spaces);

        Assert.Equal(3, lines.Count);
        Assert.Equal(spaces, string.Concat(lines.Select(TextOf)));
    }

    // A asserção do produto: nenhum glifo passa da margem — o espaço final não conta, porque não
    // desenha nada — e nenhum caractere se perde no caminho.
    [Theory]
    [InlineData("abcdefghij ")]
    [InlineData("aaaaaaaa    bbb")]
    [InlineData("aaa bbb ccc ddd")]
    [InlineData("aaaaaaaaa aaaaaaaaa aaaaaaaaa")]
    [InlineData("abcdefghijklm")]
    [InlineData("          ")]
    public void Nenhum_glifo_passa_da_margem(string text)
    {
        var lines = Break(text);

        Assert.All(lines, line => Assert.True(
            InkExtentPt(line) <= MaxWidthPt,
            $"linha '{TextOf(line)}' põe tinta até {InkExtentPt(line)}pt, além dos {MaxWidthPt}pt da margem"));

        Assert.Equal(text, string.Concat(lines.Select(TextOf)));
    }

    // A outra metade da regra, e a que a tolerância existe para dar: o branco pendura na margem,
    // mas um só. Este teste fica vermelho se alguém voltar a pendurar o grupo inteiro.
    [Theory]
    [InlineData("abcdefghij ")]
    [InlineData("aaaaaaaa    bbb")]
    [InlineData("aaa bbb ccc ddd")]
    [InlineData("aaaaaaaaa aaaaaaaaa aaaaaaaaa")]
    [InlineData("abcdefghijklm")]
    [InlineData("          ")]
    public void Nenhuma_linha_passa_da_margem_por_mais_de_um_branco(string text)
    {
        var tolerance = MaxWidthPt + Measurer.CharWidthPt;

        Assert.All(Break(text), line => Assert.True(
            ExtentPt(line) <= tolerance,
            $"linha '{TextOf(line)}' chega a {ExtentPt(line)}pt, além dos {tolerance}pt tolerados"));
    }

    [Fact]
    public void Palavra_mais_larga_que_a_pagina_e_partida_no_que_cabe()
    {
        var lines = Break("abcdefghijklm");

        Assert.Collection(
            lines,
            line => Assert.Equal("abcdefghij", TextOf(line)),
            line => Assert.Equal("klm", TextOf(line)));
    }

    // A palavra impartível é o caso que trava um wrap ingênuo: ou o laço não progride, ou o
    // trecho que não coube é descartado silenciosamente.
    [Fact]
    public void Palavra_muito_longa_progride_sem_perder_texto()
    {
        var word = new string('x', 35);

        var lines = Break(word);

        Assert.Equal(4, lines.Count);
        Assert.Equal(word, string.Concat(lines.Select(TextOf)));
    }

    [Fact]
    public void Par_substituto_nao_e_partido_ao_meio()
    {
        // Cada emoji ocupa 2 chars, logo 20pt. Numa linha de 90pt cabem 4 emojis e meio — o
        // corte por largura pura cairia exatamente entre as duas metades do quinto.
        var text = string.Concat(Enumerable.Repeat("😀", 11));

        var lines = LineBreaker.BreakIntoLines([new InlineRun(text, 0, TextStyle.Body)], 90.0, Measurer);

        Assert.All(lines, line => Assert.Equal(0, TextOf(line).Length % 2));

        Assert.Equal(text, string.Concat(lines.Select(TextOf)));
        Assert.All(lines, line =>
        {
            var lineText = TextOf(line);
            Assert.False(char.IsLowSurrogate(lineText[0]), "linha começa com metade de um par substituto");
            Assert.False(char.IsHighSurrogate(lineText[^1]), "linha termina com metade de um par substituto");
        });
    }

    [Fact]
    public void Bloco_vazio_produz_uma_linha_com_a_altura_do_estilo()
    {
        var lines = LineBreaker.BreakIntoLines([new InlineRun("", 7, Large)], MaxWidthPt, Measurer);

        var line = Assert.Single(lines);
        Assert.Empty(line.Runs);
        Assert.Equal(BodyLineHeightPt * 2.0, line.HeightPt);
        Assert.Equal(7, line.SourceStart);
        Assert.Equal(0, line.SourceLength);
    }

    // O motor já quebra sobre a sequência de runs, não sobre um run isolado: é o que deixa
    // **negrito** no meio da frase ser só trabalho de parser mais tarde.
    [Fact]
    public void Runs_de_estilos_diferentes_convivem_na_mesma_linha()
    {
        var lines = LineBreaker.BreakIntoLines(
            [new InlineRun("aa ", 0, TextStyle.Body), new InlineRun("bb", 3, Large)],
            MaxWidthPt,
            Measurer);

        var line = Assert.Single(lines);

        Assert.Collection(
            line.Runs,
            run =>
            {
                Assert.Equal("aa ", run.Text);
                Assert.Equal(0.0, run.XPt);
                Assert.Equal(30.0, run.WidthPt);
            },
            run =>
            {
                Assert.Equal("bb", run.Text);
                Assert.Equal(30.0, run.XPt);
                Assert.Equal(40.0, run.WidthPt);
            });

        // A linha acompanha o maior estilo que a compõe, senão os glifos grandes se sobrepõem.
        Assert.Equal(BodyLineHeightPt * 2.0, line.HeightPt);
    }

    // Emitir um LaidOutRun por palavra multiplicaria as chamadas de desenho sem necessidade.
    [Fact]
    public void Palavras_do_mesmo_run_viram_um_unico_LaidOutRun()
    {
        var lines = Break("aaa bbb");

        Assert.Single(Assert.Single(lines).Runs);
    }

    [Fact]
    public void Linhas_cobrem_a_fonte_de_forma_contigua()
    {
        var lines = Break("aaa bbb ccc ddd");

        Assert.Equal(0, lines[0].SourceStart);
        Assert.Equal(8, lines[0].SourceLength);
        Assert.Equal(8, lines[1].SourceStart);
        Assert.Equal(15, lines[1].SourceEnd);
    }

    [Fact]
    public void Largura_nao_positiva_e_erro_de_programacao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LineBreaker.BreakIntoLines([new InlineRun("a", 0, TextStyle.Body)], 0.0, Measurer));
    }

    private static List<LaidOutLine> Break(string text) =>
        LineBreaker.BreakIntoLines([new InlineRun(text, 0, TextStyle.Body)], MaxWidthPt, Measurer);

    private static string TextOf(LaidOutLine line) => string.Concat(line.Runs.Select(run => run.Text));

    /// <summary>Até onde a linha chega, medida como foi assentada — o branco final incluído.</summary>
    private static double ExtentPt(LaidOutLine line) =>
        line.Runs.Count == 0 ? 0.0 : line.Runs[^1].XPt + line.Runs[^1].WidthPt;

    /// <summary>
    /// Até onde a linha desenha alguma coisa. O espaço final é descontado de propósito: ele ocupa
    /// largura e não põe glifo nenhum no papel, e é essa distinção que a regra da margem usa.
    /// </summary>
    private static double InkExtentPt(LaidOutLine line)
    {
        for (var index = line.Runs.Count - 1; index >= 0; index--)
        {
            var run = line.Runs[index];
            var ink = run.Text.AsSpan().TrimEnd();

            if (ink.Length > 0)
            {
                return run.XPt + Measurer.MeasureWidthPt(ink, run.Style);
            }
        }

        return 0.0;
    }
}
