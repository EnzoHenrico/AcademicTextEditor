using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class LineBreaksTests
{
    // 100pt de largura útil e 10pt por caractere: dez caracteres por linha. "aaaaa " fica na
    // primeira, "bbbbbbbbb" não cabe atrás dele e abre a segunda — o offset 6 é o fim de uma e o
    // começo da outra.
    private const string Wrapped = "aaaaa bbbbbbbbb";

    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Fora_de_uma_fronteira_o_enter_e_um_n_so()
    {
        var edit = LineBreaks.ForEnter(new Caret(4, 0.0), Layout("aaa\nbbb"));

        Assert.Equal("\n", edit.Text);
        Assert.Equal(1, edit.CaretDelta);
    }

    // A razão de existir de tudo isto: na fronteira, um \n só torna explícita a quebra que a
    // margem já impunha, e a tela fica exatamente como estava.
    [Fact]
    public void Um_n_so_na_fronteira_devolveria_a_mesma_tela()
    {
        Assert.Equal(2, Lines(Layout(Wrapped)).Count);
        Assert.Equal(2, Lines(Layout(Wrapped.Insert(6, "\n"))).Count);
    }

    [Fact]
    public void Na_fronteira_o_enter_materializa_a_quebra_da_margem()
    {
        var edit = LineBreaks.ForEnter(new Caret(6, 0.0), Layout(Wrapped));

        Assert.Equal("\n\n", edit.Text);
        Assert.Equal(2, edit.CaretDelta);
    }

    [Fact]
    public void Na_fronteira_vista_de_cima_o_caret_fica_na_linha_nova()
    {
        var edit = LineBreaks.ForEnter(new Caret(6, 0.0, CaretAffinity.Upstream), Layout(Wrapped));

        Assert.Equal("\n\n", edit.Text);
        Assert.Equal(1, edit.CaretDelta);
    }

    // O defeito relatado, fixado: Enter no início de uma linha que a margem criou tem de deixar
    // uma linha em branco visível e empurrar o texto para a de baixo, num toque só.
    [Fact]
    public void Enter_na_fronteira_abre_uma_linha_em_branco()
    {
        var caret = new Caret(6, 0.0);
        var edit = LineBreaks.ForEnter(caret, Layout(Wrapped));
        var document = Layout(Wrapped.Insert(caret.Offset, edit.Text));

        var lines = Lines(document);

        Assert.Equal(3, lines.Count);
        Assert.Equal(0, lines[1].SourceLength);

        // E o caret desce junto com o texto: terceira linha, 20pt de altura cada.
        Assert.Equal(40.0, CaretGeometry.Locate(caret.Offset + edit.CaretDelta, document, Measurer).YPt);
    }

    [Fact]
    public void Enter_na_fronteira_vista_de_cima_deixa_o_caret_na_linha_em_branco()
    {
        var caret = new Caret(6, 0.0, CaretAffinity.Upstream);
        var edit = LineBreaks.ForEnter(caret, Layout(Wrapped));
        var document = Layout(Wrapped.Insert(caret.Offset, edit.Text));

        Assert.Equal(20.0, CaretGeometry.Locate(caret.Offset + edit.CaretDelta, document, Measurer).YPt);
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static List<LaidOutLine> Lines(PaginatedDocument document) =>
        [.. document.Pages.SelectMany(page => page.Lines)];
}
