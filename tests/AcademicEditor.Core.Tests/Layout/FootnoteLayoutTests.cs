using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// A nota de rodapé no pé da folha da chamada.
/// </summary>
/// <remarks>
/// Área útil de 100 × 110pt: com o medidor determinístico cabem dez caracteres por linha e cinco
/// linhas e meia por folha, e a folga do filete é uma linha de corpo — 20pt. Uma nota de uma linha
/// custa 40pt de folha, então sobram três linhas de texto.
/// </remarks>
public sealed class FootnoteLayoutTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 130.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    /// <remarks>
    /// A nota é desenhada onde a <b>chamada</b> está, e a definição fica escrita onde o autor a
    /// escreveu — no fim do arquivo. É a primeira vez que a ordem de desenho e a de fonte divergem.
    /// </remarks>
    [Fact]
    public void A_nota_e_assentada_no_pe_da_folha_da_chamada()
    {
        var document = Layout("aaa[^1]\nbbb\nccc\nddd\neee\n\\note 1 nota");

        Assert.Equal(2, document.Pages.Count);

        var first = document.Pages[0];

        Assert.Equal(4, first.Lines.Count);
        Assert.Equal(["aaa1", "bbb", "ccc", "1nota"], first.Lines.Select(TextOf));

        var note = first.Lines[^1];

        Assert.Equal(LineKind.Footnote, note.Kind);
        Assert.Equal(24, note.SourceStart);

        // Assentada no PÉ da área de conteúdo, e não logo abaixo do texto.
        Assert.Equal(90.0, note.YPt);
        Assert.Equal(110.0, note.YPt + note.HeightPt);

        // O filete no meio da folga que separa os dois.
        Assert.Equal(80.0, first.FootnoteRulePt);

        // A folha seguinte não tem nota, e não tem filete.
        Assert.Equal(["ddd", "eee"], document.Pages[1].Lines.Select(TextOf));
        Assert.Null(document.Pages[1].FootnoteRulePt);
    }

    /// <remarks>
    /// <b>A prova de que a divergência é real</b>, e o primeiro documento em que o ramo de
    /// ordenação do índice roda. Em ordem de desenho os offsets vão 0, 8, 12, 24, 16, 20 — a nota
    /// da primeira folha vem do fim do arquivo. Em ordem de fonte voltam a crescer.
    /// </remarks>
    [Fact]
    public void A_ordem_de_desenho_diverge_da_de_fonte_e_o_indice_conserta()
    {
        var document = Layout("aaa[^1]\nbbb\nccc\nddd\neee\n\\note 1 nota");

        Assert.Equal(
            [0, 8, 12, 24, 16, 20],
            document.Pages.SelectMany(page => page.Lines).Select(line => line.SourceStart));

        Assert.Equal([0, 8, 12, 16, 20, 24], document.Index.Select(found => found.SourceStart));
    }

    /// <remarks>
    /// <b>Nenhum texto pode sumir da tela.</b> Um identificador que ninguém referenciou não é
    /// motivo para tirar a linha do lugar em que ela foi escrita.
    /// </remarks>
    [Fact]
    public void Definicao_nunca_chamada_continua_sendo_paragrafo_comum()
    {
        var document = Layout("aaa\n\\note 1 nota");

        var page = Assert.Single(document.Pages);

        Assert.Equal(["aaa", "1nota"], page.Lines.Select(TextOf));
        Assert.All(page.Lines, line => Assert.Equal(LineKind.Text, line.Kind));
        Assert.Null(page.FootnoteRulePt);
    }

    /// <remarks>
    /// <b>A reserva acontece antes de a linha ser assentada</b>, e é o que dispensa o laço de
    /// convergência: a linha que chama a nota desce junto com ela quando as duas não cabem no que
    /// resta da folha. Aqui "ddd" caberia sozinha — sobram 50pt e ela pede 20 —, mas com os 40 da
    /// nota não cabe, e as duas descem.
    /// </remarks>
    [Fact]
    public void A_linha_que_chama_desce_junto_com_a_nota_que_nao_cabe()
    {
        var document = Layout("aaa\nbbb\nccc\nddd[^1]\neee\n\\note 1 nota");

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(["aaa", "bbb", "ccc"], document.Pages[0].Lines.Select(TextOf));
        Assert.Null(document.Pages[0].FootnoteRulePt);

        Assert.Equal(["ddd1", "eee", "1nota"], document.Pages[1].Lines.Select(TextOf));
        Assert.NotNull(document.Pages[1].FootnoteRulePt);
    }

    [Fact]
    public void Nota_chamada_duas_vezes_aparece_uma()
    {
        var document = Layout("aaa[^1]\nbbb[^1]\n\\note 1 nota");

        var page = Assert.Single(document.Pages);

        Assert.Equal(["aaa1", "bbb1", "1nota"], page.Lines.Select(TextOf));
    }

    /// <remarks>
    /// Duas definições com o mesmo rótulo: a primeira vai para o pé, e a segunda <b>continua no
    /// fluxo</b>. A alternativa seria ela sumir da tela, e nenhum texto pode sumir.
    /// </remarks>
    [Fact]
    public void Definicao_repetida_nao_some_da_tela()
    {
        var document = Layout("aaa[^1]\n\\note 1 uma\n\\note 1 outra");

        var page = Assert.Single(document.Pages);

        Assert.Equal(["aaa1", "1outra", "1uma"], page.Lines.Select(TextOf));
        Assert.Equal(LineKind.Text, page.Lines[1].Kind);
        Assert.Equal(LineKind.Footnote, page.Lines[2].Kind);
    }

    /// <remarks>
    /// A nota chamada só de dentro de outra nota seria assentada na folha em que <i>aquela</i> foi
    /// desenhada — dependência circular disfarçada. Continua sendo parágrafo comum.
    /// </remarks>
    [Fact]
    public void Chamada_de_dentro_de_uma_nota_nao_conta()
    {
        var document = Layout("aaa[^1]\n\\note 1 veja[^2]\n\\note 2 outra");

        var page = Assert.Single(document.Pages);

        Assert.Equal(LineKind.Text, page.Lines[1].Kind);
        Assert.Equal("2outra", TextOf(page.Lines[1]));
    }

    /// <remarks>
    /// O caret tem de poder pousar na nota: ela é texto do arquivo, e o autor a edita como edita um
    /// parágrafo. O mapa offset → linha continua total, com a nota fora do lugar em que foi escrita.
    /// </remarks>
    [Theory]
    [InlineData("aaa[^1]\nbbb\nccc\nddd\neee\n\\note 1 nota")]
    [InlineData("aaa[^1]\nbbb[^2]\n\\note 1 uma\n\\note 2 outra")]
    [InlineData("aaa\n\\note 1 nunca chamada")]
    public void Todo_offset_continua_pertencendo_a_uma_linha(string source)
    {
        var document = Layout(source);

        for (var offset = 0; offset <= source.Length; offset++)
        {
            Assert.Contains(
                document.Index,
                found => offset >= found.SourceStart && offset <= found.SourceEnd);
        }
    }

    /// <remarks>
    /// O reflow incremental tem de sobreviver à divergência das duas ordens — é o caso que a
    /// Fatia 5b existiu para destravar, e o que quebraria em silêncio se o reuso ainda achatasse as
    /// folhas em ordem de desenho.
    /// </remarks>
    [Theory]
    [InlineData("aaa[^1]\nbbb\nccc\nddd\neee\n\\note 1 nota", "aaa[^1]\nbXbb\nccc\nddd\neee\n\\note 1 nota", 10)]
    [InlineData("aaa[^1]\nbbb\n\\note 1 nota", "aaa[^1]\nbbb\n\\note 1 nXota", 17)]
    public void Reaproveitar_devolve_o_mesmo_documento_com_notas(string before, string after, int caret)
    {
        var previous = LayoutEngine.Layout(MarkupParser.Parse(before), Settings, Measurer, caret - 1);
        var reuse = LayoutReuse.Between(before, after, previous);

        Assert.NotNull(reuse);

        var reused = LayoutEngine.Layout(MarkupParser.Parse(after), Settings, Measurer, caret, reuse);
        var full = LayoutEngine.Layout(MarkupParser.Parse(after), Settings, Measurer, caret);

        Assert.Equal(full.Pages.Count, reused.Pages.Count);

        for (var page = 0; page < full.Pages.Count; page++)
        {
            Assert.Equal(full.Pages[page].FootnoteRulePt, reused.Pages[page].FootnoteRulePt);
            Assert.Equal(
                full.Pages[page].Lines.Select(line => (line.SourceStart, line.YPt, line.Kind)),
                reused.Pages[page].Lines.Select(line => (line.SourceStart, line.YPt, line.Kind)));
        }
    }

    /// <summary>
    /// O caret entra na nota: clique, setas e End andam por <b>dentro</b> dela.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Os testes que faltavam, e a entrega foi recusada por isso.</b> A fatia provava que a nota
    /// era <i>posicionada</i> certo e nunca que o caret <i>entrava</i> nela — invariante coberta num
    /// caminho só, que é a falha que o `CLAUDE.md` descreve.
    /// </para>
    /// <para>
    /// A causa era a pergunta errada: enquanto só havia texto e marcador de página, "é texto?" e
    /// "não é marcador?" eram a mesma coisa, e o caret perguntava a primeira. A nota chegou como um
    /// <c>Kind</c> novo e virou unidade indivisível — clicar devolvia o começo da linha.
    /// </para>
    /// </remarks>
    [Fact]
    public void O_caret_anda_por_dentro_da_nota()
    {
        var document = Layout("aaa[^1]\n\\note 1 nota");
        var note = document.Pages[0].Lines[^1];

        Assert.Equal(LineKind.Footnote, note.Kind);
        Assert.True(note.IsEditable);

        // Clicar no meio da nota pousa na coluna clicada, e não no começo da linha.
        var clicked = CaretNavigator.AtPoint(
            0, CaretGeometry.ColumnPt(note, note.SourceStart + 8, Measurer), note.YPt + 1.0, document, Measurer);

        Assert.Equal(note.SourceStart + 8, clicked.Offset);

        // E as setas andam de um caractere, em vez de saltarem a linha inteira.
        Assert.Equal(clicked.Offset + 1, CaretNavigator.MoveRight(clicked, document, Measurer).Offset);
        Assert.Equal(clicked.Offset - 1, CaretNavigator.MoveLeft(clicked, document, Measurer).Offset);

        // End vai ao fim da nota, e não ao começo dela.
        Assert.Equal(note.SourceEnd, CaretNavigator.MoveToLineEnd(clicked, document, Measurer).Offset);
    }

    /// <remarks>
    /// O contraste que dá sentido à regra: o marcador de quebra de página <b>continua</b> sendo
    /// unidade indivisível. Clicar no meio do filete não descreve nada que o autor possa editar.
    /// </remarks>
    [Fact]
    public void O_marcador_de_pagina_continua_indivisivel()
    {
        var document = Layout("aaa\n\\page\nbbb");
        var marker = document.Pages[0].Lines[1];

        Assert.Equal(LineKind.PageBreak, marker.Kind);
        Assert.False(marker.IsEditable);

        var clicked = CaretNavigator.AtPoint(0, 40.0, marker.YPt + 1.0, document, Measurer);

        Assert.Equal(marker.SourceStart, clicked.Offset);
    }

    private static string TextOf(LaidOutLine line) => string.Concat(line.Runs.Select(run => run.Text));

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer, caretOffset: -1);
}
