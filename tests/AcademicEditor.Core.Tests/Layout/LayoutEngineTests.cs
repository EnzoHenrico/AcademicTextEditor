using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Integração do motor: da fonte em markup até o documento paginado, com medidor determinístico.
/// </summary>
public sealed class LayoutEngineTests
{
    // 10 caracteres de corpo por linha, 5 linhas de corpo por página.
    private static readonly PageSettings Settings = PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    // Uma folha, e uma linha vazia nela. A linha existe para o caret: sem ela, apagar todo o
    // texto tiraria do caret a altura e a posição, e ele sumiria da tela.
    [Fact]
    public void Documento_vazio_produz_uma_pagina_com_uma_linha_vazia()
    {
        var paginated = Layout("");

        var line = Assert.Single(Assert.Single(paginated.Pages).Lines);

        Assert.Empty(line.Runs);
        Assert.Equal(0, line.SourceStart);
        Assert.Equal(0, line.SourceLength);
        Assert.True(line.HeightPt > 0.0, "a linha vazia precisa de altura para o caret caber nela");
    }

    [Fact]
    public void Paginado_carrega_a_geometria_usada()
    {
        Assert.Equal(Settings, Layout("texto").Settings);
    }

    [Fact]
    public void Paragrafo_longo_transborda_para_a_pagina_seguinte()
    {
        // 8 palavras de 9 caracteres: uma por linha, 5 linhas por página.
        var source = string.Join(' ', Enumerable.Repeat("aaaaaaaaa", 8));

        var paginated = Layout(source);

        Assert.Equal(2, paginated.Pages.Count);
        Assert.Equal(5, paginated.Pages[0].Lines.Count);
        Assert.Equal(3, paginated.Pages[1].Lines.Count);
    }

    [Fact]
    public void Quebra_explicita_no_markup_chega_ate_a_paginacao()
    {
        var paginated = Layout("Antes\n\n\\page\n\nDepois");

        Assert.Equal(2, paginated.Pages.Count);
        Assert.Equal("Antes", TextOf(paginated.Pages[0]));
        Assert.Equal("Depois", TextOf(paginated.Pages[1]));
    }

    // O heading é mais alto que o corpo, então consome mais altura útil da página. Se o motor
    // ignorasse o estilo, a contagem de páginas do documento inteiro sairia errada.
    [Fact]
    public void Heading_ocupa_mais_altura_que_um_paragrafo()
    {
        var withHeading = Layout("# T\naaa\naaa\naaa\naaa");
        var withoutHeading = Layout("T\naaa\naaa\naaa\naaa");

        Assert.Equal(2, withHeading.Pages.Count);
        Assert.Single(withoutHeading.Pages);
    }

    // O Enter na última linha de uma folha cheia abre a folha seguinte, e a linha nova é a
    // primeira dela — é onde o caret precisa aparecer, e não no rodapé da folha anterior.
    [Fact]
    public void Linha_acrescentada_em_pagina_cheia_abre_a_folha_seguinte()
    {
        var full = Layout("aaa\naaa\naaa\naaa\naaa");
        Assert.Single(full.Pages);
        Assert.Equal(5, full.Pages[0].Lines.Count);

        // Mesmo texto com um '\n' no fim: a linha vazia não cabe mais nesta folha.
        var afterEnter = Layout("aaa\naaa\naaa\naaa\naaa\n");

        Assert.Equal(2, afterEnter.Pages.Count);
        Assert.Equal(5, afterEnter.Pages[0].Lines.Count);

        var newLine = Assert.Single(afterEnter.Pages[1].Lines);

        Assert.Equal(0.0, newLine.YPt);
        Assert.True(newLine.HeightPt > 0.0, "a linha nova tem altura, senão o caret some");
    }

    // A garantia que os dois mocks do App pedem, no lugar onde ela pode ser verificada: o Core não
    // referencia o App, então o teste traz a sua própria fonte. Se a normalização sumir, o CRLF
    // volta a abrir um vão de dois caracteres entre linhas e os SourceStart divergem.
    [Fact]
    public void Crlf_e_lf_produzem_a_mesma_paginacao()
    {
        const string Lf = "# Título\n\nUm parágrafo que é uma linha só e precisa quebrar.\n\nOutro.";

        var fromLf = LayoutOf(new EditorDocument(Lf));
        var fromCrLf = LayoutOf(new EditorDocument(Lf.Replace("\n", "\r\n")));

        Assert.Equal(fromLf.Pages.Count, fromCrLf.Pages.Count);

        var linesFromLf = fromLf.Pages.SelectMany(page => page.Lines).ToArray();
        var linesFromCrLf = fromCrLf.Pages.SelectMany(page => page.Lines).ToArray();

        Assert.Equal(linesFromLf.Length, linesFromCrLf.Length);
        Assert.Equal(
            linesFromLf.Select(line => (line.SourceStart, line.SourceLength)),
            linesFromCrLf.Select(line => (line.SourceStart, line.SourceLength)));
    }

    // Com o caret no título, o "# " aparece e a linha passa a começar no offset 0 — não existe mais
    // posição no arquivo sem posição na tela, que era a causa do Enter estragar a formatação.
    [Fact]
    public void Bloco_sob_o_caret_revela_a_marcacao()
    {
        // Título curto de propósito: em 20pt cabem 5 caracteres na largura útil deste teste, e um
        // título mais longo quebraria em duas linhas e obscureceria o que se quer medir.
        const string Source = "# Tí\n\ncorpo";

        var hidden = Layout(Source, caretOffset: Source.Length);
        var revealed = Layout(Source, caretOffset: 3);

        var hiddenLine = hidden.Pages[0].Lines[0];
        var revealedLine = revealed.Pages[0].Lines[0];

        // O que muda ao revelar é o TEXTO DESENHADO, e só ele. A linha cobre o bloco inteiro nos
        // dois casos — com a marcação escondida o "# " tem posição na tela e largura zero.
        Assert.Equal("Tí", TextOf(hiddenLine));
        Assert.Equal("# Tí", TextOf(revealedLine));

        Assert.Equal(0, hiddenLine.SourceStart);
        Assert.Equal(0, revealedLine.SourceStart);
    }

    // Só a largura muda. Se a altura mudasse, o documento inteiro subiria e desceria a cada vez que
    // o caret entrasse ou saísse de um título.
    [Fact]
    public void Revelar_muda_a_largura_da_linha_nao_a_altura()
    {
        const string Source = "# Título";

        var hidden = Layout(Source, caretOffset: -1).Pages[0].Lines[0];
        var revealed = Layout(Source, caretOffset: 3).Pages[0].Lines[0];

        Assert.Equal(hidden.HeightPt, revealed.HeightPt);
        Assert.Equal(hidden.BaselinePt, revealed.BaselinePt);
    }

    [Fact]
    public void Apenas_o_bloco_do_caret_revela()
    {
        const string Source = "# Um\n## Dois";

        var lines = Layout(Source, caretOffset: 2).Pages[0].Lines;

        Assert.Equal("# Um", TextOf(lines[0]));
        Assert.Equal("Dois", TextOf(lines[1]));
    }

    [Fact]
    public void Sem_caret_nada_revela()
    {
        var document = Layout("# Tí", caretOffset: -1);

        Assert.Equal("Tí", TextOf(document.Pages[0].Lines[0]));
        Assert.Equal(TextRange.Empty, document.RevealedBlock);
    }

    // É por este trecho que o ViewModel sabe quando uma seta não custa layout nenhum.
    [Fact]
    public void Paginado_diz_qual_bloco_revelou()
    {
        var document = Layout("# Título\n\ncorpo", caretOffset: 3);

        Assert.Equal(new TextRange(0, 8), document.RevealedBlock);
        Assert.True(document.RevealedBlock.Contains(0));
        Assert.True(document.RevealedBlock.Contains(8));
        Assert.False(document.RevealedBlock.Contains(9));
    }

    /// <summary>
    /// Heading recém-aberto com a marcação escondida: a linha cobre o bloco inteiro, sustenido e
    /// tudo.
    /// </summary>
    /// <remarks>
    /// <b>Este teste afirmava o contrário</b> — que a linha ancorava no texto, deixando os offsets
    /// do <c>##&#160;</c> fora dela — e a razão registrada era "senão o caret pousa antes da
    /// marcação que ele nem vê". A premissa expirou: desde que a marcação é revelada no bloco do
    /// caret, quem pousa ali <i>vê</i>. O que sobrava era um buraco no mapa offset → linha, e um
    /// buraco desses não fica onde nasceu: quem procura a linha de um offset descoberto acha a do
    /// bloco <b>anterior</b>, e o caret é desenhado no parágrafo de cima.
    /// </remarks>
    [Fact]
    public void Heading_vazio_com_marcacao_escondida_cobre_o_bloco_inteiro()
    {
        var line = Layout("## ", caretOffset: -1).Pages[0].Lines[0];

        Assert.Equal(0, line.SourceStart);
        Assert.Equal(3, line.SourceLength);

        // E continua sem desenhar nada: cobrir não é revelar.
        Assert.Empty(line.Runs);
    }

    [Fact]
    public void Area_de_conteudo_nao_positiva_e_erro_de_programacao()
    {
        var impossible = PageSettings.Uniform(widthPt: 100.0, heightPt: 100.0, marginPt: 50.0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayoutEngine.Layout(MarkupParser.Parse("texto"), impossible, Measurer));
    }

    // A marcação inline segue a mesma regra da de bloco: escondida enquanto o caret está fora,
    // revelada quando ele entra. É o mecanismo que a Fatia 5.1 da Fase 3 montou para o "## ".
    [Fact]
    public void Marcacao_inline_e_escondida_fora_do_bloco_do_caret()
    {
        Assert.Equal("a b c", TextOf(Layout("a **b** c", caretOffset: -1).Pages[0].Lines[0]));
    }

    [Fact]
    public void Marcacao_inline_e_revelada_no_bloco_do_caret()
    {
        Assert.Equal("a **b** c", TextOf(Layout("a **b** c", caretOffset: 5).Pages[0].Lines[0]));
    }

    // A marcação carrega o estilo do texto que envolve, então revelá-la acrescenta largura de um
    // estilo que a linha já tinha. Se mudasse a altura, o documento subiria e desceria a cada vez
    // que o caret entrasse e saísse de uma palavra em negrito.
    [Fact]
    public void Revelar_marcacao_inline_nao_muda_a_altura_da_linha()
    {
        var hidden = Layout("a **b** c", caretOffset: -1).Pages[0].Lines[0];
        var revealed = Layout("a **b** c", caretOffset: 5).Pages[0].Lines[0];

        Assert.Equal(hidden.HeightPt, revealed.HeightPt);
        Assert.Equal(hidden.BaselinePt, revealed.BaselinePt);

        // O trecho da fonte que a linha cobre é o mesmo — a marcação está no meio dela —, e o que
        // muda é a tinta: quatro asteriscos a mais.
        Assert.Equal(hidden.SourceLength, revealed.SourceLength);
        Assert.Equal(
            LineExtents.ExtentPt(hidden) + (4 * Measurer.CharWidthPt),
            LineExtents.ExtentPt(revealed));
    }

    // O negrito muda o estilo no meio da linha, e a quebra por largura tem de continuar caindo no
    // mesmo lugar e continuar cobrindo a fonte sem buraco.
    [Fact]
    public void Linha_com_negrito_quebra_e_continua_contigua()
    {
        // Revelada, para que todo caractere da fonte tenha posição na tela.
        const string Source = "aaaa **bbbb** cccc dddd";

        var lines = Layout(Source, caretOffset: 0).Pages.SelectMany(page => page.Lines).ToArray();

        Assert.True(lines.Length > 1, "esperava que a linha quebrasse pela largura");
        Assert.Equal(0, lines[0].SourceStart);
        Assert.Equal(Source.Length, lines[^1].SourceEnd);

        for (var index = 1; index < lines.Length; index++)
        {
            Assert.Equal(lines[index - 1].SourceEnd, lines[index].SourceStart);
        }
    }

    private static string TextOf(LaidOutLine line) => string.Concat(line.Runs.Select(run => run.Text));

    private static PaginatedDocument Layout(string source, int caretOffset) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer, caretOffset);

    private static PaginatedDocument LayoutOf(EditorDocument document) =>
        Layout(document.CreateSnapshot().GetText());

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);

    private static string TextOf(PageLayout page) =>
        string.Concat(page.Lines.SelectMany(line => line.Runs).Select(run => run.Text));
}
