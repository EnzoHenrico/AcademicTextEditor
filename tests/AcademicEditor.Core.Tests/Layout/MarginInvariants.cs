using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// As duas garantias da margem, afirmadas num lugar só.
/// </summary>
/// <remarks>
/// <para>
/// Ficavam escritas dentro do <c>LineBreakerTests</c>, e só ali — que é o teste do caminho
/// <see cref="AcademicEditor.Core.Parsing.Ast.TextAlignment.Left"/>. Quando a Fase 6, Fatia 4
/// acrescentou um passe de alinhamento <b>depois</b> da quebra, as duas continuaram verdes medindo
/// um caminho que o aplicativo não usa: ele roda justificado.
/// </para>
/// <para>
/// <b>A extensão crua é conferida contra um número, não contra <c>LineExtents.AlignExtentPt</c>.</b>
/// O alinhamento agora <i>usa</i> aquela conta para decidir onde parar; afirmar a garantia com ela
/// esconderia um erro dela. No <see cref="FakeTextMeasurer"/> um branco mede o mesmo que qualquer
/// caractere, e é daí que sai a tolerância.
/// </para>
/// </remarks>
internal static class MarginInvariants
{
    // Folga de arredondamento: a justificação divide a sobra e soma os pedaços de volta, e a
    // última casa do double não fecha exatamente.
    private const double Epsilon = 1e-9;

    /// <param name="blankWidthPt">A largura de um branco — a tolerância, que é de <b>um</b> deles.</param>
    public static void AssertHolds(
        IReadOnlyList<LaidOutLine> lines,
        double maxWidthPt,
        ITextMeasurer measurer,
        double blankWidthPt)
    {
        AssertNoGlyphPastMargin(lines, maxWidthPt, measurer);
        AssertNoLinePastOneBlank(lines, maxWidthPt, blankWidthPt);
    }

    /// <summary>Primeira garantia: nenhum glifo além da margem.</summary>
    public static void AssertNoGlyphPastMargin(
        IReadOnlyList<LaidOutLine> lines,
        double maxWidthPt,
        ITextMeasurer measurer)
    {
        foreach (var line in lines)
        {
            var inkPt = LineExtents.InkExtentPt(line, measurer);

            Assert.True(
                inkPt <= maxWidthPt + Epsilon,
                $"'{TextOf(line)}' põe tinta até {inkPt:N1}pt, além da margem de {maxWidthPt:N1}pt");
        }
    }

    /// <summary>Segunda garantia: nenhuma linha além da margem por mais de um branco.</summary>
    public static void AssertNoLinePastOneBlank(
        IReadOnlyList<LaidOutLine> lines,
        double maxWidthPt,
        double blankWidthPt)
    {
        var tolerated = maxWidthPt + blankWidthPt;

        foreach (var line in lines)
        {
            var extentPt = LineExtents.ExtentPt(line);

            Assert.True(
                extentPt <= tolerated + Epsilon,
                $"'{TextOf(line)}' chega a {extentPt:N1}pt, além dos {tolerated:N1}pt "
                    + "que a tolerância de um branco permite");
        }
    }

    private static string TextOf(LaidOutLine line) => string.Concat(line.Runs.Select(run => run.Text));
}
