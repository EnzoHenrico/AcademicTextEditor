using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Text;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class CaretNavigatorTests
{
    // 10pt por caractere e 20pt de altura: 10 caracteres por linha, 5 linhas por página.
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Setas_horizontais_andam_caractere_a_caractere()
    {
        var document = Layout("abcdef");
        var caret = CaretNavigator.At(0, document, Measurer);

        caret = CaretNavigator.MoveRight(caret, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(2, caret.Offset);
        Assert.Equal(20.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);

        Assert.Equal(1, caret.Offset);
        Assert.Equal(10.0, caret.DesiredColumnPt);
    }

    [Fact]
    public void Setas_horizontais_nao_estouram_o_documento()
    {
        var document = Layout("abc");

        var start = CaretNavigator.At(0, document, Measurer);
        Assert.Equal(start, CaretNavigator.MoveLeft(start, document, Measurer));

        var end = CaretNavigator.At(3, document, Measurer);
        Assert.Equal(end, CaretNavigator.MoveRight(end, document, Measurer));
    }

    [Fact]
    public void Setas_verticais_nao_estouram_o_documento()
    {
        var document = Layout("uma linha só");

        var caret = CaretNavigator.At(0, document, Measurer);

        Assert.Equal(caret, CaretNavigator.MoveUp(caret, document, Measurer));
    }

    // O motivo de o caret guardar uma coluna alvo. Descer de uma linha longa para uma curta e
    // continuar descendo tem que voltar à coluna original, não grudar no fim da linha curta.
    [Fact]
    public void Coluna_alvo_sobrevive_a_travessia_de_linha_curta()
    {
        // Três linhas: longa, curta, longa.
        var document = Layout("aaaaaaaa\nbb\ncccccccc");

        var caret = CaretNavigator.At(7, document, Measurer);
        Assert.Equal(70.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);
        Assert.Equal(70.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);

        // De volta à coluna 7 da terceira linha, que começa no offset 12.
        Assert.Equal(70.0, CaretGeometry.ColumnPt(LineOf(document, caret.Offset), caret.Offset, Measurer));
    }

    // Home e End param no limite visual da quebra, não no do parágrafo — é a folha que manda.
    [Fact]
    public void Home_e_End_respeitam_a_quebra_visual_e_nao_o_paragrafo()
    {
        // Um parágrafo de 15 caracteres numa linha de 10: quebra em duas linhas visuais.
        var document = Layout("aaaaa bbbbbbbbb");
        var caret = CaretNavigator.At(8, document, Measurer);

        var end = CaretNavigator.MoveToLineEnd(caret, document, Measurer);
        var start = CaretNavigator.MoveToLineStart(caret, document, Measurer);

        Assert.Equal(6, start.Offset);
        Assert.Equal(0.0, start.DesiredColumnPt);
        Assert.Equal(15, end.Offset);
        Assert.True(end.Offset < 16, "End parou no fim da linha visual, não no fim do parágrafo");
    }

    [Fact]
    public void Navegacao_vertical_cruza_a_fronteira_de_pagina()
    {
        // Seis linhas de conteúdo numa página que comporta cinco.
        var document = Layout(string.Join("\n", Enumerable.Repeat("aaaa", 6)));
        Assert.Equal(2, document.Pages.Count);

        // Última linha da primeira página.
        var caret = CaretNavigator.At(document.Pages[0].Lines[4].SourceStart, document, Measurer);
        Assert.Equal(0, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);

        Assert.Equal(1, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MoveUp(caret, document, Measurer);

        Assert.Equal(0, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);
    }

    [Fact]
    public void PageDown_e_PageUp_trocam_de_folha()
    {
        var document = Layout(string.Join("\n", Enumerable.Repeat("aaaa", 12)));
        Assert.Equal(3, document.Pages.Count);

        var caret = CaretNavigator.At(0, document, Measurer);

        caret = CaretNavigator.MovePageDown(caret, document, Measurer);
        Assert.Equal(1, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MovePageDown(caret, document, Measurer);
        Assert.Equal(2, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);

        caret = CaretNavigator.MovePageUp(caret, document, Measurer);
        Assert.Equal(1, CaretGeometry.Locate(caret.Offset, document, Measurer).PageIndex);
    }

    [Fact]
    public void PageDown_na_ultima_folha_vai_para_a_ultima_linha()
    {
        var document = Layout("aaaa\n\nbbbb");

        var caret = CaretNavigator.At(0, document, Measurer);
        caret = CaretNavigator.MovePageDown(caret, document, Measurer);

        var lines = document.Pages[0].Lines;
        Assert.Equal(lines[^1].SourceStart, caret.Offset);
    }

    // A marcação do heading não é desenhada, então o caret não pousa nela: seguir para a direita
    // do fim de um bloco cai no primeiro caractere visível do bloco seguinte.
    [Fact]
    public void Caret_pula_a_marcacao_que_nao_e_desenhada()
    {
        const string Source = "# T\nabc";
        var document = Layout(Source);

        // Fim do texto do heading: "T" ocupa o offset 2.
        var caret = CaretNavigator.At(3, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(Source.IndexOf('a', StringComparison.Ordinal), caret.Offset);
    }

    [Fact]
    public void Par_substituto_e_atravessado_de_uma_vez()
    {
        var document = Layout("a😀b");

        var caret = CaretNavigator.At(1, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(3, caret.Offset);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);

        Assert.Equal(1, caret.Offset);
    }

    [Fact]
    public void Documento_vazio_mantem_o_caret_no_inicio()
    {
        var document = Layout("");
        var caret = CaretNavigator.At(0, document, Measurer);

        Assert.Equal(caret, CaretNavigator.MoveRight(caret, document, Measurer));
        Assert.Equal(caret, CaretNavigator.MoveDown(caret, document, Measurer));
        Assert.Equal(0, caret.Offset);
    }

    // A linha em branco é uma parada de verdade na navegação vertical: ela existe na folha, então
    // pular por cima dela seria o caret ignorando uma linha que o autor está vendo.
    [Fact]
    public void Setas_verticais_pousam_na_linha_em_branco()
    {
        const string Source = "aaaa\n\nbbbb";
        var document = Layout(Source);

        var caret = CaretNavigator.At(2, document, Measurer);
        Assert.Equal(20.0, caret.DesiredColumnPt);

        caret = CaretNavigator.MoveDown(caret, document, Measurer);

        // Offset 5 é a linha em branco. Sem coluna onde pousar, o caret vai para o começo dela.
        Assert.Equal(5, caret.Offset);
        Assert.Equal(20.0, caret.DesiredColumnPt);

        // E a coluna alvo sobrevive à travessia: desce para "bbbb" na coluna 2, não na 0.
        caret = CaretNavigator.MoveDown(caret, document, Measurer);
        Assert.Equal(8, caret.Offset);
    }

    // O '\n' não é posição de caret: ele separa duas linhas e a seta o atravessa de uma vez, do
    // fim de uma para o começo da outra.
    [Fact]
    public void Seta_direita_atravessa_a_quebra_de_linha()
    {
        var document = Layout("ab\ncd");

        var caret = CaretNavigator.At(2, document, Measurer);
        caret = CaretNavigator.MoveRight(caret, document, Measurer);

        Assert.Equal(3, caret.Offset);

        caret = CaretNavigator.MoveLeft(caret, document, Measurer);

        Assert.Equal(2, caret.Offset);
    }

    // O bug que abriu a Fatia 4.2. "aaaaa bbbbbbbbb" quebra em "aaaaa " / "bbbbbbbbb": o espaço
    // fica com a linha de cima, então o fim dela e o começo da de baixo são o mesmo offset 6.
    // Sem afinidade, End resolvia para a linha de baixo e o caret saltava para lá.
    [Fact]
    public void End_numa_linha_quebrada_fica_na_linha_dela()
    {
        var document = Layout("aaaaa bbbbbbbbb");
        var caret = CaretNavigator.MoveToLineEnd(CaretNavigator.At(2, document, Measurer), document, Measurer);

        Assert.Equal(6, caret.Offset);
        Assert.Equal(CaretAffinity.Upstream, caret.Affinity);

        // Linha 0, não linha 1 — e na coluna do fim dela, não na coluna zero.
        var position = CaretGeometry.Locate(caret.Offset, document, Measurer, caret.Affinity);

        Assert.Equal(0.0, position.YPt);
        Assert.Equal(60.0, caret.DesiredColumnPt);
    }

    // Uma tecla, um movimento visível. Antes, ← no começo de uma linha quebrada devolvia o mesmo
    // offset com a mesma afinidade: o caret ficava parado e a tecla parecia morta.
    [Fact]
    public void Setas_atravessam_a_fronteira_da_quebra_por_largura()
    {
        var document = Layout("aaaaa bbbbbbbbb");
        var start = CaretNavigator.At(6, document, Measurer);

        Assert.Equal(CaretAffinity.Downstream, start.Affinity);
        Assert.Equal(20.0, CaretGeometry.Locate(6, document, Measurer, start.Affinity).YPt);

        var left = CaretNavigator.MoveLeft(start, document, Measurer);

        // Mesmo offset, outra linha: é a afinidade que faz a diferença ser visível.
        Assert.Equal(6, left.Offset);
        Assert.Equal(CaretAffinity.Upstream, left.Affinity);
        Assert.Equal(0.0, CaretGeometry.Locate(left.Offset, document, Measurer, left.Affinity).YPt);
        Assert.NotEqual(start, left);

        var back = CaretNavigator.MoveRight(left, document, Measurer);

        Assert.Equal(start, back);
    }

    // A afinidade descreve uma fronteira da linha de origem. Levá-la adiante desenharia o caret na
    // linha errada quando a coluna alvo calhasse de cair numa outra fronteira.
    [Fact]
    public void Movimento_vertical_nao_carrega_a_afinidade()
    {
        var document = Layout("aaaaa bbbbbbbbb");
        var caret = CaretNavigator.MoveToLineEnd(CaretNavigator.At(2, document, Measurer), document, Measurer);

        Assert.Equal(CaretAffinity.Upstream, caret.Affinity);
        Assert.Equal(CaretAffinity.Downstream, CaretNavigator.MoveDown(caret, document, Measurer).Affinity);
    }

    // O outro sintoma da Fatia 4.2: quebrar a linha no fim dela punha um espaço no começo da linha
    // de baixo. End pousa DEPOIS do espaço em que a linha quebrou, então o \n entra depois dele e
    // o espaço fica em cima, onde é invisível.
    [Fact]
    public void Enter_no_fim_de_uma_linha_quebrada_deixa_o_espaco_em_cima()
    {
        var document = new EditorDocument("aaaaa bbbbbbbbb");
        var caret = CaretNavigator.MoveToLineEnd(
            CaretNavigator.At(2, LayoutOf(document), Measurer),
            LayoutOf(document),
            Measurer);

        document.Insert(caret.Offset, "\n");

        var lines = LayoutOf(document).Pages.SelectMany(page => page.Lines).ToArray();

        Assert.Equal("aaaaa ", TextOf(lines[0]));
        Assert.Equal("bbbbbbbbb", TextOf(lines[1]));
    }

    private static string TextOf(LaidOutLine line) => string.Concat(line.Runs.Select(run => run.Text));

    private static PaginatedDocument LayoutOf(EditorDocument document) =>
        Layout(document.CreateSnapshot().GetText());

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static LaidOutLine LineOf(PaginatedDocument document, int offset)
    {
        var position = CaretGeometry.Locate(offset, document, Measurer);

        return document.Pages[position.PageIndex].Lines
            .First(line => line.SourceStart <= offset && offset <= line.SourceEnd);
    }
}
