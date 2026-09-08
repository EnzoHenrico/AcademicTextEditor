using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.Text;

public sealed class EditorDocumentTests
{
    [Fact]
    public void Insert_anuncia_o_que_entrou()
    {
        var document = new EditorDocument("abc");
        var edits = new List<TextEdit>();
        document.Changed += (_, edit) => edits.Add(edit);

        document.Insert(1, "XY");

        var edit = Assert.Single(edits);
        Assert.Equal(new TextEdit(1, RemovedLength: 0, "XY"), edit);
        Assert.Equal("aXYbc", document.CreateSnapshot().GetText());
    }

    [Fact]
    public void Delete_anuncia_o_que_saiu()
    {
        var document = new EditorDocument("abcdef");
        var edits = new List<TextEdit>();
        document.Changed += (_, edit) => edits.Add(edit);

        document.Delete(2, 3);

        Assert.Equal(new TextEdit(2, RemovedLength: 3, ""), Assert.Single(edits));
        Assert.Equal("abf", document.CreateSnapshot().GetText());
    }

    // Um evento por alteração que não alterou nada faria o layout recalcular à toa a cada tecla
    // morta — e, com debounce, ainda atrasaria a repaginação real.
    [Fact]
    public void Alteracao_vazia_nao_anuncia_nada()
    {
        var document = new EditorDocument("abc");
        var announced = 0;
        document.Changed += (_, _) => announced++;

        document.Insert(0, "");
        document.Delete(0, 0);

        Assert.Equal(0, announced);
        Assert.Equal(3, document.Length);
    }

    // A invariante que dispensa o parser, o line breaker e cada tecla de edição de saber o que
    // fazer com CRLF: o buffer nunca contém \r.
    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("a\r\n\r\nb", "a\n\nb")]
    [InlineData("a\r\n", "a\n")]
    public void Construtor_normaliza_o_fim_de_linha(string input, string expected)
    {
        var document = new EditorDocument(input);

        Assert.Equal(expected, document.CreateSnapshot().GetText());
        Assert.Equal(expected.Length, document.Length);
    }

    [Fact]
    public void Insert_normaliza_e_devolve_o_que_realmente_entrou()
    {
        var document = new EditorDocument("ab");

        // Colar duas linhas do Windows: quatro caracteres na string, três no buffer. Quem movesse
        // o caret por text.Length o deixaria um caractere adiante do buffer.
        var inserted = document.Insert(1, "X\r\nY");

        Assert.Equal(3, inserted);
        Assert.Equal("aX\nYb", document.CreateSnapshot().GetText());
    }

    [Fact]
    public void Texto_sem_cr_nao_e_copiado()
    {
        const string Source = "sem retorno de carro";

        Assert.Same(Source, LineEndings.NormalizeToLf(Source));
    }
}
