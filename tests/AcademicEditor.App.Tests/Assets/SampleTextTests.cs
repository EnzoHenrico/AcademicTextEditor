using AcademicEditor.App.Assets.Samples;

using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.App.Tests.Assets;

/// <summary>
/// O texto que o aplicativo abre demonstra o que promete.
/// </summary>
/// <remarks>
/// <b>Existe porque o exemplo já mentiu duas vezes.</b> Ele descrevia a nota de rodapé como
/// parágrafo comum depois de ela passar a ir para o pé da folha, e ensinava a declará-la com uma
/// marcação que deixou de existir. É o primeiro contato de quem abre o editor: um exemplo que não
/// funciona ensina errado antes de qualquer documentação.
/// </remarks>
public sealed class SampleTextTests
{
    [Fact]
    public void A_definicao_de_nota_do_exemplo_e_reconhecida()
    {
        var blocks = MarkupParser.Parse(Text.UniqueFeaturesLf).Blocks;

        var note = Assert.Single(blocks.OfType<FootnoteNode>());
        var calls = blocks.SelectMany(block => block.FootnoteCalls).Select(call => call.Id);

        // E ela é chamada de fora, senão o exemplo mostraria a nota parada no meio do texto.
        Assert.Contains(note.Id, calls);
    }

    /// <remarks>
    /// A prosa do exemplo cita a marcação para ensiná-la. Se o marcador mudar de nome outra vez, o
    /// texto tem de mudar junto — e é este teste que cobra.
    /// </remarks>
    [Fact]
    public void O_exemplo_ensina_a_marcacao_que_o_motor_entende()
    {
        Assert.Contains(MarkupTokenizer.FootnoteMarker, Text.UniqueFeaturesLf, StringComparison.Ordinal);
        Assert.Contains(MarkupTokenizer.PageBreakMarker, Text.UniqueFeaturesLf, StringComparison.Ordinal);
    }
}
