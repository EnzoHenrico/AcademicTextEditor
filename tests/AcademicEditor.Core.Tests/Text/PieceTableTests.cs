using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.Text;

public sealed class PieceTableTests
{
    [Fact]
    public void Tabela_vazia_nao_tem_peca()
    {
        var table = new PieceTable("");

        Assert.Equal(0, table.Length);
        Assert.Equal(0, table.PieceCount);
        Assert.Equal("", Text(table));
    }

    [Theory]
    [InlineData(0, "XYZ", "XYZabcdef")]
    [InlineData(3, "XYZ", "abcXYZdef")]
    [InlineData(6, "XYZ", "abcdefXYZ")]
    public void Insert_no_inicio_meio_e_fim(int offset, string text, string expected)
    {
        var table = new PieceTable("abcdef");

        table.Insert(offset, text);

        Assert.Equal(expected, Text(table));
        Assert.Equal(expected.Length, table.Length);
    }

    [Fact]
    public void Insert_em_tabela_vazia()
    {
        var table = new PieceTable("");

        table.Insert(0, "abc");

        Assert.Equal("abc", Text(table));
    }

    // O ponto da piece table. Digitar caractere a caractere no fim não pode fazer a lista de
    // peças crescer a cada tecla — se crescesse, cada snapshot copiaria uma lista cada vez maior
    // e o custo de digitar aumentaria com o tanto já digitado.
    [Fact]
    public void Digitacao_sequencial_no_fim_nao_faz_a_lista_de_pecas_crescer()
    {
        var table = new PieceTable("abc");

        foreach (var c in "0123456789")
        {
            table.Insert(table.Length, c.ToString());
        }

        Assert.Equal("abc0123456789", Text(table));
        Assert.Equal(2, table.PieceCount);
    }

    [Fact]
    public void Insert_fora_de_ordem_nao_estica_a_peca_errada()
    {
        var table = new PieceTable("abc");

        table.Insert(3, "X");
        table.Insert(0, "Y");
        table.Insert(table.Length, "Z");

        Assert.Equal("YabcXZ", Text(table));
    }

    [Theory]
    [InlineData(0, 3, "def")]
    [InlineData(3, 3, "abc")]
    [InlineData(2, 2, "abef")]
    [InlineData(0, 6, "")]
    public void Delete_dentro_de_uma_peca(int offset, int length, string expected)
    {
        var table = new PieceTable("abcdef");

        table.Delete(offset, length);

        Assert.Equal(expected, Text(table));
        Assert.Equal(expected.Length, table.Length);
    }

    [Fact]
    public void Delete_cruzando_pecas()
    {
        var table = new PieceTable("abcdef");
        table.Insert(3, "XYZ");

        // "abc" + "XYZ" + "def": apaga do 'c' ao 'Z', consumindo o fim da primeira peça,
        // a peça inteira do meio, e parando exatamente na fronteira com a terceira.
        table.Delete(2, 4);

        Assert.Equal("abdef", Text(table));
    }

    [Fact]
    public void Delete_consumindo_pecas_inteiras()
    {
        var table = new PieceTable("abc");
        table.Insert(3, "DEF");
        table.Insert(6, "GHI");

        table.Delete(3, 3);

        Assert.Equal("abcGHI", Text(table));
    }

    [Fact]
    public void Delete_no_meio_parte_a_peca_em_duas()
    {
        var table = new PieceTable("abcdef");

        table.Delete(2, 2);

        Assert.Equal("abef", Text(table));
        Assert.Equal(2, table.PieceCount);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(4, 3)]
    [InlineData(7, 0)]
    public void Delete_fora_dos_limites_e_erro_de_programacao(int offset, int length)
    {
        var table = new PieceTable("abcdef");

        Assert.Throws<ArgumentOutOfRangeException>(() => table.Delete(offset, length));
    }

    [Fact]
    public void Insert_fora_dos_limites_e_erro_de_programacao()
    {
        var table = new PieceTable("abc");

        Assert.Throws<ArgumentOutOfRangeException>(() => table.Insert(4, "X"));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Insert(-1, "X"));
    }

    [Fact]
    public void CharAt_le_das_duas_fontes()
    {
        var table = new PieceTable("abc");
        table.Insert(1, "XY");

        Assert.Equal("aXYbc", Text(table));
        Assert.Equal('a', table.CharAt(0));
        Assert.Equal('X', table.CharAt(1));
        Assert.Equal('b', table.CharAt(3));
    }

    // A razão de o snapshot existir: o layout roda em background sobre ele enquanto a digitação
    // continua alterando a tabela. Se o snapshot enxergasse edições posteriores, o layout
    // publicado descreveria um texto que nunca existiu.
    [Fact]
    public void Snapshot_nao_enxerga_edicoes_posteriores()
    {
        var table = new PieceTable("abc");
        var snapshot = table.CreateSnapshot();

        table.Insert(3, "def");
        table.Delete(0, 1);

        Assert.Equal("abc", snapshot.GetText());
        Assert.Equal(3, snapshot.Length);
        Assert.Equal("bcdef", Text(table));
    }

    // O buffer de adição cresce dobrando, e crescer realoca. O array antigo continua na mão dos
    // snapshots já entregues — é isso que dispensa lock entre a digitação e o layout.
    [Fact]
    public void Snapshot_sobrevive_ao_crescimento_do_buffer_de_adicao()
    {
        var table = new PieceTable("");
        table.Insert(0, new string('a', 100));

        var snapshot = table.CreateSnapshot();

        // Passa folgado da capacidade inicial, forçando pelo menos uma realocação.
        for (var i = 0; i < 50; i++)
        {
            table.Insert(table.Length, new string('b', 100));
        }

        Assert.Equal(new string('a', 100), snapshot.GetText());
    }

    private static string Text(PieceTable table) => table.CreateSnapshot().GetText();
}
