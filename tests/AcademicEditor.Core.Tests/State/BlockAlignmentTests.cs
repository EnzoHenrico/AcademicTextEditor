using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.State;

namespace AcademicEditor.Core.Tests.State;

/// <summary>O rodízio de alinhamento do <c>Ctrl+J</c>.</summary>
public sealed class BlockAlignmentTests
{
    /// <remarks>
    /// Quatro estados, e o primeiro é <b>sem marcação</b> — o padrão da norma. É por isso que não
    /// existe marcação para justificado: ela pediria o que já está valendo.
    /// </remarks>
    [Theory]
    [InlineData("texto", 0, ":- ")]
    [InlineData(":- texto", 3, ":-: ")]
    [InlineData(":-: texto", 4, "-: ")]
    [InlineData("-: texto", 3, "")]
    public void O_rodizio_percorre_os_quatro_estados(string line, int currentLength, string expected)
    {
        var edit = Assert.Single(BlockAlignment.Next(line, new TextRange(0, 0)));

        Assert.Equal(0, edit.Start);
        Assert.Equal(currentLength, edit.Length);
        Assert.Equal(expected, edit.Replacement);
    }

    /// <remarks>
    /// Espaços a mais são absorvidos pela marcação e saem normalizados: <c>":-:   texto"</c> tem
    /// seis caracteres de marcação, e o que entra no lugar tem a forma canônica.
    /// </remarks>
    [Fact]
    public void A_marcacao_sai_normalizada()
    {
        var edit = Assert.Single(BlockAlignment.Next(":-:   texto", new TextRange(0, 0)));

        Assert.Equal(6, edit.Length);
        Assert.Equal("-: ", edit.Replacement);
    }

    /// <remarks>
    /// O destino sai da <b>primeira</b> linha tocada. Sem isso, uma seleção com alinhamentos
    /// mistos veria cada linha seguir o seu próprio rodízio, e elas nunca convergiriam.
    /// </remarks>
    [Fact]
    public void Uma_selecao_mista_converge_para_o_alinhamento_da_primeira_linha()
    {
        const string Source = "um\n:-: dois\n-: tres";

        var edits = BlockAlignment.Next(Source, new TextRange(0, Source.Length));

        Assert.Equal(3, edits.Count);
        Assert.All(edits, edit => Assert.Equal(":- ", edit.Replacement));
    }

    /// <remarks>
    /// De trás para a frente é a ordem de <b>aplicação</b>: assim cada troca acontece num texto que
    /// as anteriores ainda não deslocaram, e nenhum offset precisa ser recalculado no caminho.
    /// </remarks>
    [Fact]
    public void As_trocas_saem_na_ordem_em_que_devem_ser_aplicadas()
    {
        const string Source = "um\ndois\ntres";

        var edits = BlockAlignment.Next(Source, new TextRange(0, Source.Length));

        Assert.Equal([8, 3, 0], edits.Select(edit => edit.Start));
    }

    /// <remarks>
    /// Marcar uma linha em branco faria dela texto; marcar um <c>\page</c> desfaria a quebra de
    /// página. Nenhuma das duas é o que se pede ao apertar Ctrl+J.
    /// </remarks>
    [Theory]
    [InlineData("um\n\ndois")]
    [InlineData("um\n\\page\ndois")]
    public void Linha_em_branco_e_quebra_de_pagina_ficam_de_fora(string source)
    {
        var edits = BlockAlignment.Next(source, new TextRange(0, source.Length));

        Assert.Equal(2, edits.Count);
    }

    /// <remarks>
    /// Regra de qualquer editor, e sem ela um <c>Ctrl+A</c> alinharia uma linha a mais do que o
    /// autor destacou.
    /// </remarks>
    [Fact]
    public void Selecao_que_termina_no_comeco_de_uma_linha_nao_pega_essa_linha()
    {
        var edit = Assert.Single(BlockAlignment.Next("um\ndois", new TextRange(0, 3)));

        Assert.Equal(0, edit.Start);
    }

    [Fact]
    public void Linha_que_ja_esta_no_destino_nao_vira_edicao()
    {
        // A primeira decide o destino (":- "), e a segunda já está nele: apagar e reinserir o mesmo
        // texto encheria o undo de passos que não mudam nada.
        const string Source = "um\n:- dois";

        var edit = Assert.Single(BlockAlignment.Next(Source, new TextRange(0, Source.Length)));

        Assert.Equal(0, edit.Start);
    }

    [Fact]
    public void Documento_vazio_nao_tem_o_que_alinhar()
    {
        Assert.Empty(BlockAlignment.Next(string.Empty, new TextRange(0, 0)));
    }

    /// <remarks>
    /// Sem repor os offsets, a seleção escorregaria alguns caracteres a cada Ctrl+J e o toque
    /// seguinte pegaria outras linhas.
    /// </remarks>
    [Fact]
    public void O_caret_acompanha_o_texto_que_se_deslocou()
    {
        const string Source = "um\ndois";

        var edits = BlockAlignment.Next(Source, new TextRange(0, Source.Length));

        // ":- um\n:- dois": o 'd' de "dois" sai do offset 3 para o 9 — três caracteres de
        // marcação na linha dele, e mais três na de cima.
        Assert.Equal(9, BlockAlignment.Reposition(3, edits));

        // O offset anda com o TEXTO, não com a posição: o caret estava antes do 'u' e continua
        // antes dele, agora depois da marcação que entrou. É o que faz a seleção cobrir o que o
        // autor destacou, e não a marcação que ela mesma acabou de criar.
        Assert.Equal(3, BlockAlignment.Reposition(0, edits));
    }

    [Fact]
    public void Offset_dentro_da_marcacao_trocada_vai_para_o_fim_da_nova()
    {
        // O caret apontava para um texto que deixou de existir; o fim da marcação nova é o lugar
        // mais próximo que ainda quer dizer alguma coisa.
        var edits = BlockAlignment.Next("-: texto", new TextRange(0, 0));

        Assert.Equal(0, BlockAlignment.Reposition(2, edits));
    }
}
