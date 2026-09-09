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

    /// <remarks>
    /// A mesma garantia da Fatia 5.3, agora com as linhas esticadas: nenhum glifo passa da margem.
    /// Distribuir a sobra só pode encostar a tinta nela — nunca empurrá-la para fora.
    /// </remarks>
    [Theory]
    [InlineData("ab cd ef ghijklmno")]
    [InlineData("um dois tres quatro cinco seis sete oito nove dez onze doze")]
    [InlineData("a  b   c    dddddddddddd")]
    [InlineData("palavra")]
    public void Nenhum_glifo_passa_da_margem_numa_linha_justificada(string text)
    {
        foreach (var line in Break(text, TextAlignment.Justify))
        {
            foreach (var run in line.Runs)
            {
                var inkEndPt = run.XPt + Measurer.MeasureWidthPt(run.Text.AsSpan().TrimEnd(), run.Style);

                Assert.True(
                    inkEndPt <= MaxWidthPt + 1e-9,
                    $"'{run.Text}' termina em {inkEndPt}pt, além da margem de {MaxWidthPt}pt");
            }
        }
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
