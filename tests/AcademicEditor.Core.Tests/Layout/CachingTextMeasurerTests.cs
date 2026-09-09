using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Tests.Layout;

public sealed class CachingTextMeasurerTests
{
    private static readonly TextStyle Large = TextStyle.Body with { FontSizePt = 22.0, Weight = FontWeightKind.Bold };

    // A razão de existir do cache: o line breaker mede a mesma palavra toda vez que ela aparece.
    [Fact]
    public void Trecho_repetido_nao_chega_ao_medidor_de_baixo()
    {
        var inner = new CountingMeasurer();
        var measurer = new CachingTextMeasurer(inner);

        var first = measurer.MeasureWidthPt("paginação", TextStyle.Body);
        var second = measurer.MeasureWidthPt("paginação", TextStyle.Body);

        Assert.Equal(first, second);
        Assert.Equal(1, inner.WidthCalls);
    }

    // Mesmo texto em 22pt mede o dobro do que em 11pt: guardar por texto e ignorar o estilo poria
    // o título com a largura do corpo, e a linha quebraria no lugar errado.
    [Fact]
    public void Estilos_diferentes_nao_compartilham_entrada()
    {
        var inner = new CountingMeasurer();
        var measurer = new CachingTextMeasurer(inner);

        var body = measurer.MeasureWidthPt("abc", TextStyle.Body);
        var large = measurer.MeasureWidthPt("abc", Large);

        Assert.Equal(2, inner.WidthCalls);
        Assert.NotEqual(body, large);
        Assert.Equal(large, measurer.MeasureWidthPt("abc", Large));
    }

    [Fact]
    public void Largura_e_a_mesma_com_e_sem_cache()
    {
        var inner = new FakeTextMeasurer();
        var measurer = new CachingTextMeasurer(inner);

        foreach (var text in (string[])["a", "abc", "abc def", "😀", "  ", "paginação"])
        {
            Assert.Equal(inner.MeasureWidthPt(text, TextStyle.Body), measurer.MeasureWidthPt(text, TextStyle.Body));
        }
    }

    // Texto vazio é o caso mais comum de todos — todo bloco em branco passa por aqui — e não vale
    // uma entrada de cache nem uma chamada.
    [Fact]
    public void Texto_vazio_nao_chega_ao_medidor_de_baixo()
    {
        var inner = new CountingMeasurer();

        Assert.Equal(0.0, new CachingTextMeasurer(inner).MeasureWidthPt(string.Empty, TextStyle.Body));
        Assert.Equal(0, inner.WidthCalls);
    }

    // Métricas dependem só do estilo: são meia dúzia, não crescem, e quem as produz já as guarda.
    [Fact]
    public void Metricas_de_linha_passam_direto()
    {
        var inner = new FakeTextMeasurer();
        var measurer = new CachingTextMeasurer(inner);

        Assert.Equal(inner.GetLineMetrics(Large), measurer.GetLineMetrics(Large));
    }

    // Sem teto, medir vinte trechos duas vezes chega ao medidor de baixo vinte vezes. É a forma
    // observável de dizer "nada foi descartado" — e o contraste com o teste seguinte.
    [Fact]
    public void Dentro_do_teto_nada_e_medido_duas_vezes()
    {
        var inner = new CountingMeasurer();
        var measurer = new CachingTextMeasurer(inner);

        MeasureCycle(measurer, distinct: 20, rounds: 2);

        Assert.Equal(20, inner.WidthCalls);
    }

    // O teto existe por causa dos prefixos transitórios da busca binária que posiciona o caret:
    // espaço de chaves ilimitado. Estourar não pode devolver largura errada — só voltar a medir.
    [Fact]
    public void Teto_descarta_entradas_sem_perder_correcao()
    {
        var reference = new FakeTextMeasurer();
        var inner = new CountingMeasurer();
        var measurer = new CachingTextMeasurer(inner, capacity: 4);

        MeasureCycle(measurer, distinct: 20, rounds: 2, reference);

        Assert.True(inner.WidthCalls > 20, $"nada foi descartado: {inner.WidthCalls} medições para 20 trechos");
    }

    /// <summary>
    /// Mede <paramref name="distinct"/> trechos diferentes, <paramref name="rounds"/> vezes cada.
    /// Com <paramref name="reference"/>, confere cada largura contra o medidor sem cache.
    /// </summary>
    private static void MeasureCycle(
        CachingTextMeasurer measurer,
        int distinct,
        int rounds,
        FakeTextMeasurer? reference = null)
    {
        for (var round = 0; round < rounds; round++)
        {
            for (var index = 0; index < distinct; index++)
            {
                var text = new string('x', index + 1);
                var width = measurer.MeasureWidthPt(text, TextStyle.Body);

                if (reference is not null)
                {
                    Assert.Equal(reference.MeasureWidthPt(text, TextStyle.Body), width);
                }
            }
        }
    }

    [Fact]
    public void Medidor_nulo_e_teto_nao_positivo_sao_erro_de_programacao()
    {
        Assert.Throws<ArgumentNullException>(() => new CachingTextMeasurer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CachingTextMeasurer(new FakeTextMeasurer(), capacity: 0));
    }

    /// <summary>Conta as chamadas que chegaram ao medidor de baixo.</summary>
    private sealed class CountingMeasurer : ITextMeasurer
    {
        private readonly FakeTextMeasurer _inner = new();

        public int WidthCalls { get; private set; }

        public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style)
        {
            WidthCalls++;
            return _inner.MeasureWidthPt(text, style);
        }

        public LineMetrics GetLineMetrics(TextStyle style) => _inner.GetLineMetrics(style);
    }
}
