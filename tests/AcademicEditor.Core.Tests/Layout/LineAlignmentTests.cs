using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;
using AcademicEditor.Core.State;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Alinhamento e justificação das linhas já quebradas.
/// </summary>
/// <remarks>
/// Com o <see cref="FakeTextMeasurer"/> cada caractere mede 10pt, então uma largura útil de 100pt
/// cabe exatamente dez caracteres e todas as contas fecham em números redondos.
/// </remarks>
public sealed class LineAlignmentTests
{
    private const double MaxWidthPt = 100.0;

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void A_esquerda_e_o_que_o_line_breaker_ja_produz()
    {
        var line = Assert.Single(Break("abc", TextAlignment.Left));

        Assert.Equal(0.0, line.Runs[0].XPt);
    }

    [Theory]
    // "abc" mede 30pt: centrado sobra 35 de cada lado, à direita começa em 70.
    [InlineData(TextAlignment.Center, 35.0)]
    [InlineData(TextAlignment.Right, 70.0)]
    public void Centralizar_e_alinhar_a_direita_deslocam_a_linha(TextAlignment alignment, double expectedXPt)
    {
        var line = Assert.Single(Break("abc", alignment));

        Assert.Equal(expectedXPt, line.Runs[0].XPt, precision: 9);
    }

    /// <remarks>
    /// O corolário da tolerância de margem da Fatia 5.3: uma linha quebrada pela largura quase
    /// sempre termina num branco pendurado, invisível. Centralizar pela extensão crua deslocaria a
    /// linha meio espaço para a esquerda — visível, e errado.
    /// </remarks>
    [Fact]
    public void Centralizar_mede_a_tinta_e_nao_o_branco_do_fim()
    {
        var withSpace = Assert.Single(Break("abc ", TextAlignment.Center));
        var without = Assert.Single(Break("abc", TextAlignment.Center));

        Assert.Equal(without.Runs[0].XPt, withSpace.Runs[0].XPt, precision: 9);
    }

    /// <remarks>
    /// "ab cd ef ghijklmno" quebra em "ab cd ef " (tinta 80pt, mais o branco pendurado) e
    /// "ghijklmno". A primeira sobra 20pt para dois vãos, então cada um ganha 10pt: os segmentos
    /// passam a começar em 0, 20, 40, 60, 80 e 100, e a tinta termina exatamente na margem.
    /// </remarks>
    [Fact]
    public void Justificar_distribui_a_sobra_entre_os_vaos()
    {
        var lines = Break("ab cd ef ghijklmno", TextAlignment.Justify);
        var first = lines[0];

        Assert.Equal([0.0, 20.0, 40.0, 60.0, 80.0, 100.0], first.Runs.Select(run => run.XPt));
        Assert.Equal(["ab", " ", "cd", " ", "ef", " "], first.Runs.Select(run => run.Text));

        // A tinta encosta na margem: é isso que "as duas margens retas" quer dizer.
        Assert.Equal(MaxWidthPt, first.Runs[4].XPt + first.Runs[4].WidthPt, precision: 9);
    }

    /// <remarks>
    /// Ele já está fora da margem por decisão da Fatia 5.3; esticá-lo levaria a linha adiante do
    /// papel, que é exatamente o que aquela fatia fechou.
    /// </remarks>
    [Fact]
    public void O_branco_pendurado_no_fim_da_linha_nao_estica()
    {
        var first = Break("ab cd ef ghijklmno", TextAlignment.Justify)[0];
        var hanging = first.Runs[^1];

        Assert.Equal(" ", hanging.Text);
        Assert.Equal(Measurer.CharWidthPt, hanging.WidthPt, precision: 9);
    }

    [Fact]
    public void A_ultima_linha_do_bloco_nao_e_justificada()
    {
        var lines = Break("ab cd ef ghijklmno", TextAlignment.Justify);
        var last = lines[^1];

        Assert.Equal(0.0, last.Runs[0].XPt);
        Assert.Equal("ghijklmno", Assert.Single(last.Runs).Text);
    }

    [Fact]
    public void Uma_palavra_sozinha_nao_tem_onde_distribuir_e_fica_como_esta()
    {
        // Sem vão nenhum, esticar exigiria mexer no texto do run — que é o que este passe não faz.
        var lines = Break("abcdefghijklmnopqrst", TextAlignment.Justify);

        Assert.Equal(0.0, lines[0].Runs[0].XPt);
    }

    /// <remarks>
    /// <b>A garantia que a fatia arrisca.</b> Justificar reposiciona os pedaços da linha, e caret,
    /// seleção e hit test acham a coluna medindo um <i>prefixo dentro do run</i>. Se o passe
    /// tivesse esticado o texto em vez de partir a linha nos brancos, a posição desenhada deixaria
    /// de bater com o prefixo medido e o caret pousaria fora do lugar — longe de onde se errou.
    /// Aqui, todo offset que a linha cobre volta de onde foi.
    /// </remarks>
    [Theory]
    [InlineData("ab cd ef ghijklmno")]
    [InlineData("um dois tres quatro cinco seis sete oito")]
    [InlineData("a  b   c    dddddddddddd")]
    public void Coluna_e_offset_continuam_sendo_o_inverso_um_do_outro(string text)
    {
        foreach (var line in Break(text, TextAlignment.Justify))
        {
            for (var offset = line.SourceStart; offset <= line.SourceEnd; offset++)
            {
                var columnPt = CaretGeometry.ColumnPt(line, offset, Measurer);

                Assert.Equal(offset, CaretGeometry.OffsetAtColumn(line, columnPt, Measurer));
            }
        }
    }

    /// <summary>
    /// As duas garantias da margem valem em <b>todo</b> alinhamento, não só à esquerda.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Este é o teste que faltava, e o que a Fatia 4 deveria ter escrito. As garantias da Fatia 5.3
    /// moravam no <c>LineBreakerTests</c>, que quebra sempre à esquerda — e à esquerda
    /// <c>LineAlignment.Apply</c> retorna na primeira linha. O passe inteiro ficou sem cobertura de
    /// margem, e voltou a pendurar grupos de branco fora do papel.
    /// </para>
    /// <para>
    /// Os textos com <b>vários brancos seguidos</b> são o que expõe o defeito, e por isso estão
    /// aqui em vez de num caso feliz: <c>"a  b   c    dddddddddddd"</c> saía a 130pt e
    /// <c>"ab cd    efghijklmno"</c> a 140pt, contra uma margem de 100pt.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TodoAlinhamentoDeCadaTexto))]
    public void A_margem_vale_em_todo_alinhamento(string text, TextAlignment alignment) =>
        MarginInvariants.AssertHolds(
            Break(text, alignment), MaxWidthPt, Measurer, Measurer.CharWidthPt);

    /// <summary>
    /// Numa linha justificada, o caret entre duas palavras encosta na palavra <b>seguinte</b>.
    /// </summary>
    /// <remarks>
    /// O run de branco carrega a sobra da justificação dentro da própria <c>WidthPt</c>, mas
    /// <c>ColumnPt</c> mede o texto — um espaço. Terminar no run do branco deixava o caret um vão
    /// inteiro atrás da palavra que ele deveria preceder. Com a sobra normal isso é fração de
    /// ponto; era visível porque a margem furada da Fatia 4 inflava a sobra.
    /// </remarks>
    [Fact]
    public void O_caret_entre_palavras_encosta_na_palavra_seguinte()
    {
        var first = Break("ab cd ef ghijklmno", TextAlignment.Justify)[0];

        // Os segmentos ficam em 0, 20, 40, 60, 80: o branco em 20 tem 20pt de largura (10 medidos
        // mais 10 de sobra), e o offset 3 é o começo de "cd".
        var blank = first.Runs[1];
        var word = first.Runs[2];

        Assert.Equal(" ", blank.Text);
        Assert.Equal("cd", word.Text);
        Assert.Equal(blank.SourceEnd, word.SourceStart);

        Assert.Equal(word.XPt, CaretGeometry.ColumnPt(first, word.SourceStart, Measurer), precision: 9);
    }

    public static TheoryData<string, TextAlignment> TodoAlinhamentoDeCadaTexto()
    {
        string[] texts =
        [
            "ab cd ef ghijklmno",
            "um dois tres quatro cinco seis sete oito nove dez onze doze",

            // Grupo de brancos que a tolerância parte: dois cabem, o terceiro pendura.
            "a  b   c    dddddddddddd",

            // Grupo que cabe inteiro dentro da margem — aqui a justificação sozinha o punha fora.
            "ab cd    efghijklmno",

            // Brancos no fim de um bloco de uma linha só, que é o caso de Center e Right.
            "abc    ",
            "palavra",
            "          ",
        ];

        var data = new TheoryData<string, TextAlignment>();

        foreach (var text in texts)
        {
            foreach (var alignment in Enum.GetValues<TextAlignment>())
            {
                data.Add(text, alignment);
            }
        }

        return data;
    }

    private static List<LaidOutLine> Break(string text, TextAlignment alignment) =>
        LineBreaker.BreakIntoLines(
            [new InlineRun(text, 0, TextStyle.Body)],
            MaxWidthPt,
            Measurer,
            includeMarkup: false,
            preset: null,
            alignment);
}
