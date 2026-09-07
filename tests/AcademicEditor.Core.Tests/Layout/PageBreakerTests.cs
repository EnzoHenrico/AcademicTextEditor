using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Tests.Layout;

public sealed class PageBreakerTests
{
    [Fact]
    public void Conteudo_que_enche_a_pagina_exatamente_nao_transborda()
    {
        var breaker = new PageBreaker(contentHeightPt: 100.0);
        AddLines(breaker, count: 5, heightPt: 20.0);

        var pages = breaker.Build();

        Assert.Equal(5, Assert.Single(pages).Lines.Count);
    }

    // Um ponto a mais de conteúdo que a página comporta já é outra folha — é a diferença entre
    // ver o documento como será impresso e vê-lo aproximadamente.
    [Fact]
    public void Um_ponto_alem_da_altura_util_abre_outra_pagina()
    {
        var breaker = new PageBreaker(contentHeightPt: 99.0);
        AddLines(breaker, count: 5, heightPt: 20.0);

        var pages = breaker.Build();

        Assert.Collection(
            pages,
            page => Assert.Equal(4, page.Lines.Count),
            page => Assert.Single(page.Lines));
    }

    [Fact]
    public void YPt_e_relativo_ao_topo_da_pagina_e_reinicia_a_cada_folha()
    {
        var breaker = new PageBreaker(contentHeightPt: 40.0);
        AddLines(breaker, count: 4, heightPt: 20.0);

        var pages = breaker.Build();

        Assert.Equal([0.0, 20.0], pages[0].Lines.Select(line => line.YPt));
        Assert.Equal([0.0, 20.0], pages[1].Lines.Select(line => line.YPt));
    }

    // Uma linha mais alta que a folha não tem para onde ir. Fica sozinha e estoura a margem, em
    // vez de sumir ou travar o laço procurando uma página onde caiba.
    [Fact]
    public void Linha_mais_alta_que_a_pagina_fica_sozinha_na_dela()
    {
        var breaker = new PageBreaker(contentHeightPt: 20.0);
        breaker.AddLine(Line(20.0));
        breaker.AddLine(Line(50.0));
        breaker.AddLine(Line(20.0));

        var pages = breaker.Build();

        Assert.Equal(3, pages.Count);
        Assert.Equal(50.0, Assert.Single(pages[1].Lines).HeightPt);
    }

    [Fact]
    public void Documento_sem_linhas_produz_uma_pagina_em_branco()
    {
        var pages = new PageBreaker(contentHeightPt: 100.0).Build();

        Assert.Empty(Assert.Single(pages).Lines);
    }

    [Fact]
    public void Quebra_explicita_fecha_a_pagina_corrente()
    {
        var breaker = new PageBreaker(contentHeightPt: 100.0);
        breaker.AddLine(Line(20.0));
        breaker.ForcePageBreak();
        breaker.AddLine(Line(20.0));

        var pages = breaker.Build();

        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.Single(page.Lines));
    }

    // Duas quebras seguidas pedem uma folha em branco. Adivinhar que o autor não quis seria pior
    // que obedecer: no meio de uma tese, a página em branco costuma ser intencional.
    [Fact]
    public void Quebras_consecutivas_produzem_folha_em_branco()
    {
        var breaker = new PageBreaker(contentHeightPt: 100.0);
        breaker.AddLine(Line(20.0));
        breaker.ForcePageBreak();
        breaker.ForcePageBreak();
        breaker.AddLine(Line(20.0));

        var pages = breaker.Build();

        Assert.Equal(3, pages.Count);
        Assert.Empty(pages[1].Lines);
    }

    [Fact]
    public void Altura_util_nao_positiva_e_erro_de_programacao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageBreaker(contentHeightPt: 0.0));
    }

    private static void AddLines(PageBreaker breaker, int count, double heightPt)
    {
        for (var i = 0; i < count; i++)
        {
            breaker.AddLine(Line(heightPt));
        }
    }

    private static LaidOutLine Line(double heightPt) =>
        new(YPt: 0.0, heightPt, BaselinePt: heightPt * 0.8, Runs: [], SourceStart: 0, SourceLength: 0);
}
