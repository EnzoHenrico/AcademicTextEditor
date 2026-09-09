using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.Text;

/// <summary>A contagem de caracteres e palavras da barra de status.</summary>
public sealed class DocumentStatisticsTests
{
    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("   \n\t ", 6, 0)]
    [InlineData("uma", 3, 1)]
    [InlineData("uma frase curta", 15, 3)]
    // Brancos seguidos não abrem palavra nova — nem o começo nem o fim do texto.
    [InlineData("  duas    palavras  ", 20, 2)]
    // Quebra de linha separa palavras e conta como caractere, como em qualquer contador.
    [InlineData("uma\nlinha", 9, 2)]
    // Pontuação anda com a palavra: "Silva," é uma, não duas. Diferente do duplo clique.
    [InlineData("Silva, J. (2020)", 16, 3)]
    public void Conta_caracteres_e_palavras(string text, int characters, int words)
    {
        var statistics = DocumentStatistics.Of(text);

        Assert.Equal(characters, statistics.Characters);
        Assert.Equal(words, statistics.Words);
    }

    /// <remarks>
    /// O emoji é um caractere para quem escreveu e dois para o buffer. Contar unidades UTF-16 faria
    /// um limite de submissão por caracteres mentir — e é o mesmo critério que Backspace e Delete
    /// já seguem para não apagar meio code point.
    /// </remarks>
    [Fact]
    public void Par_substituto_conta_como_um_caractere()
    {
        const string Text = "a\U0001F600b";

        Assert.Equal(4, Text.Length);
        Assert.Equal(3, DocumentStatistics.Of(Text).Characters);
        Assert.Equal(1, DocumentStatistics.Of(Text).Words);
    }

    /// <remarks>
    /// O que se conta é o arquivo, marcação inclusa. Contar só o que é desenhado faria o número
    /// mudar ao mover o caret, porque a marcação é revelada na linha em que ele está.
    /// </remarks>
    [Fact]
    public void A_marcacao_entra_na_conta()
    {
        Assert.Equal(2, DocumentStatistics.Of("# Título").Words);
        Assert.Equal(1, DocumentStatistics.Of("**negrito**").Words);
    }
}
