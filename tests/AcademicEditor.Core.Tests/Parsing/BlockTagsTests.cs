using AcademicEditor.Core.Parsing;

namespace AcademicEditor.Core.Tests.Parsing;

/// <summary>
/// A regra que decide o que é uma tag de bloco.
/// </summary>
/// <remarks>
/// A garantia que estes testes guardam é a que evita a wrongness silenciosa: <b>o que não é tag
/// conhecida é texto na folha</b>. Uma tag malformada que sumisse deixaria o autor sem entender
/// para onde foi o que digitou — e <c>\meta</c> e <c>\meta\</c> diferem por um caractere.
/// </remarks>
public sealed class BlockTagsTests
{
    [Theory]
    [InlineData("\\page", BlockTags.PageBreak)]
    [InlineData("\\toc", BlockTags.TableOfContents)]
    [InlineData("  \\page  ", BlockTags.PageBreak)]
    [InlineData("\t\\toc", BlockTags.TableOfContents)]
    public void Tag_conhecida_e_lida_com_espacos_em_volta(string line, string expected)
    {
        Assert.True(BlockTags.TryRead(line, out var name));
        Assert.Equal(expected, name.ToString());
        Assert.True(BlockTags.IsMarker(line));
    }

    [Fact]
    public void Tag_desconhecida_tem_nome_mas_nao_e_marcador()
    {
        // As duas perguntas são separadas de propósito: ler o nome é gramática, saber o que ele
        // significa é do parser. É o que permite a esta classe não conhecer nenhuma tag futura.
        Assert.True(BlockTags.TryRead("\\pgae", out var name));
        Assert.Equal("pgae", name.ToString());
        Assert.False(BlockTags.IsMarker("\\pgae"));
    }

    [Theory]
    [InlineData("\\meta\\")]
    [InlineData("/meta/")]
    [InlineData("\\page e mais texto")]
    [InlineData("\\page2")]
    [InlineData("\\")]
    [InlineData("")]
    [InlineData("page")]
    [InlineData("C:\\projetos")]
    [InlineData("12/03/2026")]
    public void O_que_nao_e_tag_e_texto(string line)
    {
        Assert.False(BlockTags.TryRead(line, out _));
        Assert.False(BlockTags.IsMarker(line));
    }

    /// <remarks>
    /// A forma pareada está <b>reservada</b> na gramática e não tem máquina: o cabeçalho de
    /// metadados, que seria o primeiro cliente, ficou em <c>---</c>. Enquanto não houver um, ela é
    /// texto — e este teste é o que impede alguém de implementá-la sem perceber que ninguém pediu.
    /// </remarks>
    [Fact]
    public void A_forma_pareada_continua_sem_maquina()
    {
        var tokens = MarkupTokenizer.Tokenize("\\meta\\\ntitulo: x\n/meta/");

        Assert.All(tokens, token => Assert.Equal(MarkupTokenKind.Text, token.Kind));
    }

    [Fact]
    public void O_tokenizer_classifica_as_duas_tags_conhecidas()
    {
        var tokens = MarkupTokenizer.Tokenize("\\page\n\\toc\n\\nao");

        Assert.Equal(MarkupTokenKind.PageBreak, tokens[0].Kind);
        Assert.Equal(MarkupTokenKind.TableOfContents, tokens[1].Kind);
        Assert.Equal(MarkupTokenKind.Text, tokens[2].Kind);
    }
}
