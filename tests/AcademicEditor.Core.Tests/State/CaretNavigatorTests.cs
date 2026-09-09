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

    // O bug 2. Depois de End a coluna alvo é a largura cheia da linha, então o movimento vertical
    // cai exatamente na fronteira da quebra — e sem escolher a afinidade, ↑ resolvia para a linha
    // de baixo (o caret parecia ir ao começo da mesma linha) e ↓ pulava uma.
    [Fact]
    public void Setas_verticais_depois_de_End_chegam_a_linha_vizinha()
    {
        // Três linhas visuais de um parágrafo só, todas terminando em quebra por largura.
        var document = Layout("aaaaa bbbbb ccccc ddddd");
        var lines = document.Pages[0].Lines;

        Assert.True(lines.Count >= 3, $"esperava 3+ linhas visuais, veio {lines.Count}");

        // End na linha do meio.
        var caret = CaretNavigator.At(lines[1].SourceStart, document, Measurer);
        caret = CaretNavigator.MoveToLineEnd(caret, document, Measurer);

        var up = CaretNavigator.MoveUp(caret, document, Measurer);
        Assert.Equal(0.0, CaretGeometry.Locate(up.Offset, document, Measurer, up.Affinity).YPt);

        var down = CaretNavigator.MoveDown(caret, document, Measurer);
        Assert.Equal(
            lines[2].YPt,
            CaretGeometry.Locate(down.Offset, document, Measurer, down.Affinity).YPt);
    }

    // Tecla morta parece editor travado: na borda ela leva ao extremo da linha corrente.
    [Fact]
    public void Seta_para_cima_na_primeira_linha_vai_para_o_comeco_dela()
    {
        var document = Layout("abcdef");
        var caret = CaretNavigator.At(4, document, Measurer);

        Assert.Equal(0, CaretNavigator.MoveUp(caret, document, Measurer).Offset);
    }

    [Fact]
    public void Seta_para_baixo_na_ultima_linha_vai_para_o_fim_dela()
    {
        var document = Layout("abcdef");
        var caret = CaretNavigator.At(2, document, Measurer);

        Assert.Equal(6, CaretNavigator.MoveDown(caret, document, Measurer).Offset);
    }

    // ------------------------------------------------------------------------------------------
    // AtPoint — a entrada por clique.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Clique_pousa_na_fronteira_de_caractere_mais_proxima()
    {
        var document = Layout("abcdef");

        // 10pt por caractere: 24pt está na metade esquerda do terceiro, 26pt na direita.
        Assert.Equal(2, CaretNavigator.AtPoint(0, 24.0, 5.0, document, Measurer).Offset);
        Assert.Equal(3, CaretNavigator.AtPoint(0, 26.0, 5.0, document, Measurer).Offset);
    }

    [Fact]
    public void Clique_escolhe_a_linha_pela_altura()
    {
        var document = Layout("abc\ndef");

        // Linha 0 ocupa 0..20pt, linha 1 ocupa 20..40pt.
        Assert.Equal(1, CaretNavigator.AtPoint(0, 14.0, 5.0, document, Measurer).Offset);
        Assert.Equal(5, CaretNavigator.AtPoint(0, 14.0, 25.0, document, Measurer).Offset);
    }

    // Uma tecla que não faz nada parece o editor travado; um clique que não faz nada parece o
    // editor ignorando o mouse. Fora do texto, pousa no ponto mais próximo que existe.
    [Fact]
    public void Clique_fora_do_texto_pousa_no_extremo_mais_proximo()
    {
        var document = Layout("abc\ndef");

        // Acima da primeira linha e à esquerda da margem: começo do documento.
        Assert.Equal(0, CaretNavigator.AtPoint(0, -50.0, -50.0, document, Measurer).Offset);

        // Abaixo da última linha e à direita da margem: fim do documento.
        Assert.Equal(7, CaretNavigator.AtPoint(0, 500.0, 500.0, document, Measurer).Offset);
    }

    [Fact]
    public void Clique_em_pagina_fora_do_intervalo_grampeia_na_pilha()
    {
        var document = Layout("abc");

        Assert.Equal(0, CaretNavigator.AtPoint(-3, 0.0, 0.0, document, Measurer).Offset);
        Assert.Equal(3, CaretNavigator.AtPoint(99, 500.0, 500.0, document, Measurer).Offset);
    }

    // O marcador é indivisível — a mesma regra que as setas seguem. Uma posição no meio do filete
    // tracejado não descreve nada que o autor possa editar.
    [Fact]
    public void Clique_no_marcador_de_quebra_pousa_no_inicio_dele()
    {
        var document = Layout("abc\n\\page\ndef");
        var marker = document.Pages[0].Lines.Single(line => line.Kind == LineKind.PageBreak);

        var caret = CaretNavigator.AtPoint(0, 35.0, marker.YPt + 5.0, document, Measurer);

        Assert.Equal(marker.SourceStart, caret.Offset);
    }

    // Folha sem linha alguma. O parser de hoje não produz uma — desde que o \\page passou a ocupar
    // uma linha desenhada, toda folha tem ao menos o marcador —, mas CaretGeometry.PreviousLine e
    // NextLine já a atravessam, e o clique tem de concordar com elas. Montada à mão pelo mesmo
    // motivo: verificar a regra, não a rota que chega até ela.
    [Fact]
    public void Clique_em_folha_em_branco_cai_na_vizinha_com_conteudo()
    {
        var populated = Layout("abcdef");
        var document = new PaginatedDocument(
            [populated.Pages[0], new PageLayout([]), populated.Pages[0]],
            Settings);

        // Para trás primeiro: o caret pertence ao texto que a folha em branco veio depois de
        // encerrar, não ao que ainda não começou.
        // À direita de tudo, para que o offset identifique a linha sem ambiguidade: o fim dela.
        var caret = CaretNavigator.AtPoint(1, 500.0, 0.0, document, Measurer);

        Assert.Equal(populated.Pages[0].Lines[^1].SourceEnd, caret.Offset);
    }

    // A asserção que pega um sinal trocado na conversão: AtPoint é o inverso de Locate, e ir e
    // voltar tem de devolver o mesmo offset em toda posição que uma linha cobre.
    [Theory]
    [InlineData("abc\ndef")]
    [InlineData("aaaaa bbbbbbbbb")]
    [InlineData("aaaa\nbbbb\ncccc\ndddd\neeee\nffff\ngggg")]
    public void Clique_e_o_inverso_da_geometria_do_caret(string source)
    {
        var document = Layout(source);

        foreach (var affinity in new[] { CaretAffinity.Downstream, CaretAffinity.Upstream })
        {
            for (var offset = 0; offset <= source.Length; offset++)
            {
                var position = CaretGeometry.Locate(offset, document, Measurer, affinity);
                var line = document.Pages[position.PageIndex].Lines
                    .First(candidate => candidate.YPt == position.YPt);

                // Offsets que nenhuma linha cobre — o '\n' entre dois blocos — não têm posição na
                // tela, e não é deles que o clique fala.
                if (offset < line.SourceStart || offset > line.SourceEnd)
                {
                    continue;
                }

                var caret = CaretNavigator.AtPoint(
                    position.PageIndex, position.XPt, position.YPt, document, Measurer);

                Assert.Equal(offset, caret.Offset);
            }
        }
    }

    // Clicar no fim visual de uma linha quebrada pela margem tem de desenhar o caret ali, e não no
    // começo da linha de baixo: os dois são o mesmo offset, e quem desempata é a afinidade.
    [Fact]
    public void Clique_no_fim_de_linha_quebrada_reivindica_a_linha_de_cima()
    {
        var document = Layout("aaaaa bbbbbbbbb");
        var first = document.Pages[0].Lines[0];

        var caret = CaretNavigator.AtPoint(0, first.SourceLength * 10.0, first.YPt + 5.0, document, Measurer);

        Assert.Equal(first.SourceEnd, caret.Offset);
        Assert.Equal(CaretAffinity.Upstream, caret.Affinity);
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
