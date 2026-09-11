using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.State;
using AcademicEditor.Core.Tests.Layout;

namespace AcademicEditor.Core.Tests.State;

/// <summary>
/// O cabeçalho de metadados visto pelo motor: ele cobre o trecho, não desenha nada e não recebe o
/// caret.
/// </summary>
/// <remarks>
/// <b>Cobrir e não receber são duas coisas</b>, e é a distinção inteira desta fatia. Cobrir mantém
/// o mapa <c>offset → linha</c> total — a invariante que a Fatia 5a.1 provou e que custou caro onde
/// foi enfraquecida antes. Não receber é o que impede o caret de virar uma barra de altura zero,
/// que é uma barra que sumiu da tela.
/// </remarks>
public sealed class FrontMatterBoundaryTests
{
    private static readonly PageSettings Settings =
        PageSettings.Uniform(widthPt: 200.0, heightPt: 200.0, marginPt: 10.0);

    private static readonly FakeTextMeasurer Measurer = new();

    private const string Source = "---\ntitle: Uma tese\n---\ncorpo um\n\ncorpo dois";

    private static int BodyStart => FrontMatter.BodyStart(Source);

    [Fact]
    public void O_cabecalho_e_uma_linha_de_altura_zero_que_cobre_o_trecho()
    {
        var header = Layout(Source).Pages[0].Lines[0];

        Assert.Equal(LineKind.FrontMatter, header.Kind);
        Assert.Equal(0, header.SourceStart);
        Assert.Equal(BodyStart, header.SourceLength);
        Assert.Equal(0.0, header.HeightPt);
        Assert.Empty(header.Runs);
    }

    [Fact]
    public void Sem_cabecalho_nao_ha_linha_de_cabecalho()
    {
        Assert.DoesNotContain(
            Layout("corpo um\n\ncorpo dois").Pages.SelectMany(page => page.Lines),
            line => line.Kind == LineKind.FrontMatter);
    }

    /// <remarks>
    /// A altura zero é o que faz o cabeçalho não gastar papel. Um cabeçalho de dez chaves não pode
    /// empurrar a primeira linha do trabalho para a folha seguinte.
    /// </remarks>
    [Fact]
    public void O_cabecalho_nao_ocupa_papel()
    {
        var withHeader = Layout(Source).Pages[0].Lines;
        var without = Layout("corpo um\n\ncorpo dois").Pages[0].Lines;

        Assert.Equal(
            without.Sum(line => line.HeightPt),
            withHeader.Sum(line => line.HeightPt));
    }

    /// <summary>
    /// Todo offset do arquivo pertence a uma linha, cabeçalho incluído.
    /// </summary>
    [Fact]
    public void O_mapa_continua_total()
    {
        var document = Layout(Source);

        for (var offset = 0; offset <= Source.Length; offset++)
        {
            Assert.Contains(
                document.Pages.SelectMany(page => page.Lines),
                line => offset >= line.SourceStart && offset <= line.SourceEnd);
        }
    }

    /// <summary>
    /// O caret nunca pousa no cabeçalho, venha de que offset vier.
    /// </summary>
    [Fact]
    public void O_caret_nunca_pousa_no_cabecalho()
    {
        var document = Layout(Source);

        for (var offset = 0; offset <= Source.Length; offset++)
        {
            var position = CaretGeometry.Locate(offset, document, Measurer);

            // Altura zero é a assinatura do cabeçalho, e é o sintoma como o autor o veria: o caret
            // desaparece da tela.
            Assert.True(position.HeightPt > 0.0, $"o caret sumiu no offset {offset}");
        }
    }

    /// <remarks>
    /// ← no primeiro offset do corpo não tem para onde ir: o que está atrás não aceita caret. A
    /// tecla não faz nada, como no offset 0 de um documento sem cabeçalho.
    /// </remarks>
    [Fact]
    public void A_seta_para_a_esquerda_nao_entra_no_cabecalho()
    {
        var document = Layout(Source);
        var caret = CaretNavigator.At(BodyStart, document, Measurer);

        Assert.Equal(BodyStart, CaretNavigator.MoveLeft(caret, document, Measurer).Offset);
        Assert.Equal(BodyStart, CaretNavigator.MoveUp(caret, document, Measurer).Offset);
    }

    [Fact]
    public void Clicar_no_alto_da_folha_pousa_no_corpo()
    {
        var document = Layout(Source);
        var caret = CaretNavigator.AtPoint(0, xPt: 0.0, yPt: 0.0, document, Measurer);

        Assert.Equal(BodyStart, caret.Offset);
    }

    /// <summary>
    /// Backspace no primeiro offset do corpo não apaga nada.
    /// </summary>
    /// <remarks>
    /// O caractere atrás dele é o <c>\n</c> que fecha a cerca. Apagá-lo desmancharia o cabeçalho
    /// inteiro — o <c>---</c> deixaria de estar sozinho na linha — e o arquivo todo apareceria na
    /// folha de uma vez. O trecho vazio é a forma de dizer "não apague nada" a quem já trata
    /// comprimento zero como nada a fazer.
    /// </remarks>
    [Fact]
    public void Backspace_no_comeco_do_corpo_nao_apaga_nada()
    {
        var range = Assert.NotNull(BlockMarkers.BackspaceRange(BodyStart, Layout(Source)));

        Assert.Equal(0, range.Length);
    }

    [Fact]
    public void Backspace_dentro_do_corpo_continua_normal()
    {
        Assert.Null(BlockMarkers.BackspaceRange(BodyStart + 3, Layout(Source)));
    }

    /// <remarks>
    /// Arquivo que é só cabeçalho ainda precisa de uma linha onde o caret fique — sem ela ele não
    /// teria altura nem posição, que é o mesmo motivo pelo qual o documento vazio produz uma linha
    /// vazia desde a Fatia 4 da Fase 3.
    /// </remarks>
    [Theory]
    [InlineData("---\ntitle: x\n---\n")]
    [InlineData("---\ntitle: x\n---")]
    public void Arquivo_so_de_cabecalho_ainda_tem_onde_pousar(string source)
    {
        var document = Layout(source);
        var position = CaretGeometry.Locate(source.Length, document, Measurer);

        Assert.True(position.HeightPt > 0.0, "o caret não tem altura num arquivo só de cabeçalho");
    }

    private static PaginatedDocument Layout(string source) =>
        LayoutEngine.Layout(MarkupParser.Parse(source), Settings, Measurer);
}
