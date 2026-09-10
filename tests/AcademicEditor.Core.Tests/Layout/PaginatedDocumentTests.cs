using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// O índice de linhas em ordem de fonte, e a guarda que impede o documento de perdê-lo.
/// </summary>
public sealed class PaginatedDocumentTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 120.0, heightPt: 60.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    /// <remarks>
    /// A propriedade de que a busca binária depende. Hoje ela é de graça — o passeio pelas folhas
    /// já sai em ordem de fonte —, e é justamente por isso que precisa de teste: quando a nota de
    /// rodapé quebrar a coincidência, é a ordenação do construtor que tem de manter isto de pé.
    /// </remarks>
    [Theory]
    [InlineData("um paragrafo qualquer com varias palavras para quebrar em linhas")]
    [InlineData("# Titulo\n\ncorpo\n\n\\page\n\ndepois da quebra")]
    [InlineData("a\n\n\n\nb\n\nc")]
    public void O_indice_cobre_todas_as_linhas_em_ordem_de_fonte(string source)
    {
        var document = Layout(source);

        Assert.Equal(document.Pages.Sum(page => page.Lines.Count), document.Index.Count);

        for (var position = 0; position < document.Index.Count; position++)
        {
            var found = document.Index[position];
            var line = document.Pages[found.PageIndex].Lines[found.LineIndex];

            // A entrada descreve a linha que ela aponta, e não uma cópia que derivou dela.
            Assert.Equal(line.SourceStart, found.SourceStart);
            Assert.Equal(line.SourceLength, found.SourceLength);

            if (position > 0)
            {
                Assert.True(
                    document.Index[position - 1].SourceStart <= found.SourceStart,
                    $"o índice desandou em {position}: "
                        + $"{document.Index[position - 1].SourceStart} depois de {found.SourceStart}");
            }
        }
    }

    /// <remarks>
    /// <b>A armadilha vira erro alto.</b> A cópia de record não reexecuta o cálculo do índice, e um
    /// índice velho não dá exceção: dá um caret que pousa na linha errada, longe de onde se errou.
    /// Trocar folhas mantendo as mesmas linhas é o que o passe de cabeçalho e rodapé faz, e é a
    /// única coisa que continua permitida.
    /// </remarks>
    [Fact]
    public void Trocar_as_folhas_mantendo_as_linhas_e_permitido()
    {
        var document = Layout("um paragrafo qualquer com varias palavras");

        var withBands = document.WithSameLines(
            [.. document.Pages.Select(page => page with { Header = [] })]);

        Assert.Equal(document.Index, withBands.Index);
    }

    [Fact]
    public void Trocar_as_linhas_por_baixo_do_indice_e_erro()
    {
        var document = Layout("um paragrafo qualquer com varias palavras");

        // Mesma contagem de folhas, listas de linhas diferentes: é exatamente o caso que passaria
        // despercebido e deixaria o índice apontando para linhas que já não existem.
        var replaced = document.Pages
            .Select(page => new PageLayout([.. page.Lines]))
            .ToArray();

        Assert.Throws<ArgumentException>(() => document.WithSameLines(replaced));
        Assert.Throws<ArgumentException>(() => document.WithSameLines([document.Pages[0]]));
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);
}
