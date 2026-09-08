using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class BlockMarkersTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    // "abc\n\page\ndef": o marcador ocupa [4,9) e o '\n' que o isola está em 9.
    private const string Source = "abc\n\\page\ndef";

    [Fact]
    public void Marcador_vira_linha_propria_no_fim_da_folha_que_encerra()
    {
        var document = Layout(Source);

        Assert.Equal(2, document.Pages.Count);

        var marker = document.Pages[0].Lines[^1];

        Assert.Equal(LineKind.PageBreak, marker.Kind);
        Assert.Equal(4, marker.SourceStart);
        Assert.Equal(5, marker.SourceLength);
        Assert.Empty(marker.Runs);
        Assert.True(marker.HeightPt > 0.0, "o marcador ocupa altura na folha, como no Word");
    }

    // O bug relatado: sem isto o Backspace comia só o '\n', o \page grudava em "abc" e aparecia
    // escrito na folha em vez de quebrar a página.
    [Fact]
    public void Backspace_no_inicio_da_linha_seguinte_remove_o_marcador_inteiro()
    {
        var document = Layout(Source);

        var range = Assert.NotNull(BlockMarkers.BackspaceRange(10, document));

        Assert.Equal(4, range.Start);
        Assert.Equal(6, range.Length);
        Assert.Equal("abc\ndef", Source.Remove(range.Start, range.Length));
    }

    [Fact]
    public void Backspace_sobre_o_proprio_marcador_o_remove_inteiro()
    {
        var document = Layout(Source);

        var range = Assert.NotNull(BlockMarkers.BackspaceRange(4, document));

        Assert.Equal(new TextRange(4, 6), range);
    }

    [Fact]
    public void Delete_no_fim_da_linha_anterior_remove_o_marcador_inteiro()
    {
        var document = Layout(Source);

        var range = Assert.NotNull(BlockMarkers.DeleteRange(3, document));

        Assert.Equal(new TextRange(4, 6), range);
        Assert.Equal("abc\ndef", Source.Remove(range.Start, range.Length));
    }

    [Fact]
    public void Sem_marcador_em_jogo_nao_ha_trecho_especial()
    {
        var document = Layout(Source);

        Assert.Null(BlockMarkers.BackspaceRange(2, document));
        Assert.Null(BlockMarkers.DeleteRange(11, document));
    }

    [Fact]
    public void Documento_sem_marcador_nunca_devolve_trecho()
    {
        var document = Layout("apenas texto");

        Assert.Null(BlockMarkers.BackspaceRange(5, document));
        Assert.Null(BlockMarkers.DeleteRange(5, document));
    }

    // O marcador é indivisível: uma posição no meio dele não descreve nada que o autor possa
    // editar, então as setas o atravessam de uma tecla só.
    [Fact]
    public void Setas_atravessam_o_marcador_de_uma_vez()
    {
        var document = Layout(Source);

        // Fim de "abc", logo antes do marcador.
        var caret = CaretNavigator.At(3, document, Measurer);

        caret = CaretNavigator.MoveRight(caret, document, Measurer);
        Assert.Equal(4, caret.Offset);

        caret = CaretNavigator.MoveRight(caret, document, Measurer);
        Assert.Equal(10, caret.Offset);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);
        Assert.Equal(4, caret.Offset);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);
        Assert.Equal(3, caret.Offset);
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);
}
