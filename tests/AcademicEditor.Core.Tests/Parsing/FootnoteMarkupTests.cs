using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Parsing;

/// <summary>
/// A definição de nota de rodapé — <c>[^id]: texto</c> — e as chamadas que a referenciam.
/// </summary>
public sealed class FootnoteMarkupTests
{
    [Theory]
    [InlineData("\\note 1 nota", "1")]
    [InlineData("\\note nota texto", "nota")]
    [InlineData("\\note abc    com folga", "abc")]
    public void Definicao_vira_bloco_proprio(string source, string id)
    {
        var block = Assert.Single(MarkupParser.Parse(source).Blocks);

        Assert.Equal(id, Assert.IsType<FootnoteNode>(block).Id);
    }

    /// <remarks>
    /// Mesma regra do heading e do alinhamento: <b>sem o espaço não é marcação</b>. Sem ela,
    /// <c>\\note1 algo</c> — que o autor pode ter escrito de propósito — deixaria de ser texto.
    /// </remarks>
    [Theory]
    [InlineData("\\note1 sem espaco depois do marcador")]
    [InlineData("\\note  rotulo vazio")]
    [InlineData("\\note 1")]
    [InlineData("\\nota 1 marcador que nao existe")]
    [InlineData("nao comeca com \\note 1 aqui")]
    public void Sem_a_forma_exata_continua_sendo_texto(string source)
    {
        Assert.IsType<ParagraphNode>(Assert.Single(MarkupParser.Parse(source).Blocks));
    }

    /// <remarks>
    /// <b>O marcador é marcação e o rótulo não é.</b> Escondida a marcação, a linha mostra o rótulo
    /// sobrescrito seguido do texto — que é como a nota se lê no pé da folha. Um rótulo escondido
    /// junto com o marcador deixaria a nota sem dizer a que chamada responde.
    /// </remarks>
    [Fact]
    public void O_rotulo_aparece_sobrescrito_e_a_pontuacao_some()
    {
        var block = Assert.Single(MarkupParser.Parse("\\note 1 nota").Blocks);

        Assert.Collection(
            block.Runs,
            run => Assert.True(run is { Text: "\\note ", IsMarkup: true }),
            run => Assert.True(run is { Text: "1", IsMarkup: false } && run.Style.Superscript),
            run => Assert.True(run is { Text: " ", IsMarkup: true }),
            run => Assert.True(run is { Text: "nota", IsMarkup: false } && !run.Style.Superscript));
    }

    // A invariante do InlineRun continua de pé: todo caractere da linha entra em exatamente um run,
    // na ordem, cópia literal. É ela que mantém o caret funcionando.
    [Theory]
    [InlineData("\\note 1 nota")]
    [InlineData("\\note abc   texto com **negrito** e $x$")]
    [InlineData(":-: \\note 1 centralizada")]
    public void Todo_caractere_da_definicao_entra_em_exatamente_um_run(string source)
    {
        var block = Assert.Single(MarkupParser.Parse(source).Blocks);

        Assert.Equal(source, string.Concat(block.Runs.Select(run => run.Text)));
        Assert.Equal(0, block.Runs[0].SourceStart);
        Assert.Equal(source.Length, block.Runs[^1].SourceEnd);
    }

    [Fact]
    public void As_chamadas_do_bloco_saem_do_parser_em_ordem()
    {
        var block = Assert.Single(MarkupParser.Parse("texto[^a] e mais[^b] fim").Blocks);

        Assert.Equal(["a", "b"], block.FootnoteCalls.Select(call => call.Id));
        Assert.Equal([5, 16], block.FootnoteCalls.Select(call => call.SourceStart));
    }

    [Fact]
    public void Bloco_sem_chamada_nao_aloca_lista()
    {
        var block = Assert.Single(MarkupParser.Parse("texto comum sem nota").Blocks);

        Assert.Empty(block.FootnoteCalls);
    }

    /// <remarks>
    /// O alinhamento é lido antes de tudo, então uma definição centralizada funciona de graça — o
    /// que sobra depois do prefixo continua sendo classificado como sempre.
    /// </remarks>
    [Fact]
    public void Alinhamento_e_definicao_convivem()
    {
        var block = Assert.Single(MarkupParser.Parse(":-: \\note 1 nota", TypographyPreset.Default).Blocks);

        Assert.Equal("1", Assert.IsType<FootnoteNode>(block).Id);
        Assert.Equal(TextAlignment.Center, block.Alignment);
    }
}
