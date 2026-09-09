using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

public sealed class WordBoundariesTests
{
    // 10pt por caractere e 20pt de altura: 10 caracteres por linha, 5 linhas por página.
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 120.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(2, 0, 5)]
    [InlineData(4, 0, 5)]
    public void Duplo_clique_dentro_da_palavra_pega_a_palavra(int offset, int start, int length)
    {
        var range = WordBoundaries.WordAt(offset, Layout("Silva foi"));

        Assert.Equal(new TextRange(start, length), range);
    }

    // No fim da linha não há caractere à direita, e vale o da esquerda: dois cliques depois da
    // última letra pegam a palavra que acabou de terminar.
    [Fact]
    public void Duplo_clique_no_fim_da_linha_pega_a_ultima_palavra()
    {
        Assert.Equal(new TextRange(0, 5), WordBoundaries.WordAt(5, Layout("Silva")));
    }

    [Fact]
    public void Duplo_clique_no_branco_pega_o_grupo_de_brancos()
    {
        Assert.Equal(new TextRange(5, 3), WordBoundaries.WordAt(6, Layout("Silva   foi")));
    }

    [Fact]
    public void Pontuacao_e_classe_propria()
    {
        // "fim." — o ponto não faz parte da palavra, e dois cliques nele pegam só o ponto.
        Assert.Equal(new TextRange(0, 3), WordBoundaries.WordAt(1, Layout("fim. e")));
        Assert.Equal(new TextRange(3, 1), WordBoundaries.WordAt(3, Layout("fim. e")));
    }

    // Sem isto, "nome_completo" viraria três seleções — e identificador é o que mais aparece
    // num texto técnico.
    [Fact]
    public void Sublinha_conta_como_letra()
    {
        Assert.Equal(new TextRange(0, 6), WordBoundaries.WordAt(3, Layout("nome_x fim")));
    }

    [Fact]
    public void Linha_em_branco_nao_tem_palavra()
    {
        // "abc\n\ndef": o offset 4 é a linha vazia.
        Assert.Equal(new TextRange(4, 0), WordBoundaries.WordAt(4, Layout("abc\n\ndef")));
    }

    [Fact]
    public void Palavra_nao_atravessa_a_quebra_por_largura()
    {
        // "aaaaa bbbbbbbbb" quebra em "aaaaa " e "bbbbbbbbb": são duas linhas, e cada uma tem a
        // sua palavra.
        var document = Layout("aaaaa bbbbbbbbb");

        Assert.Equal(new TextRange(0, 5), WordBoundaries.WordAt(2, document));
        Assert.Equal(new TextRange(6, 9), WordBoundaries.WordAt(9, document));
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);
}
