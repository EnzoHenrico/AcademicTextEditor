using System.Diagnostics;
using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

using Xunit.Abstractions;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// Quanto custa paginar um documento do tamanho de uma tese, com o medidor determinístico.
/// </summary>
/// <remarks>
/// <para>
/// Mede o <b>algoritmo</b>: parser, line breaker e page breaker. O custo de medir texto de verdade
/// fica de fora de propósito — o <see cref="FakeTextMeasurer"/> responde em aritmética, e é a
/// subtração entre este número e o do medidor real que diz onde está o gargalo.
/// </para>
/// <para>
/// O teto é generoso porque isto roda em máquina compartilhada: ele não existe para cravar o
/// número, existe para pegar uma regressão de ordem de grandeza — o dia em que alguém puser uma
/// busca linear dentro do laço de linhas.
/// </para>
/// </remarks>
public sealed class LayoutPerformanceTests(ITestOutputHelper output)
{
    private const int CeilingMilliseconds = 3000;

    [Fact]
    public void Documento_de_tese_pagina_dentro_do_teto()
    {
        var source = LongDocument.Build();
        var measurer = new FakeTextMeasurer();

        // Uma passada fora do relógio: a primeira paga JIT e o aquecimento do heap, e é ruído
        // sobre o que se quer medir, que é a repaginação em regime.
        _ = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);

        var stopwatch = Stopwatch.StartNew();
        var paginated = LayoutEngine.Layout(MarkupParser.Parse(source), PageSettings.A4, measurer);
        stopwatch.Stop();

        var pages = paginated.Pages.Count;
        var lines = paginated.Pages.Sum(page => page.Lines.Count);
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"caracteres : {source.Length:N0}");
        output.WriteLine($"páginas    : {pages:N0}");
        output.WriteLine($"linhas     : {lines:N0}");
        output.WriteLine($"tempo      : {elapsed:N1} ms");
        output.WriteLine($"por linha  : {elapsed * 1000.0 / lines:N2} µs");
        output.WriteLine($"por página : {elapsed / pages:N2} ms");

        Assert.True(pages >= 250, $"esperava um documento de ~300 páginas, vieram {pages}");
        Assert.True(
            elapsed < CeilingMilliseconds,
            $"paginar {pages} páginas levou {elapsed:N0}ms, além do teto de {CeilingMilliseconds}ms");
    }

    /// <summary>O que a justificação custa, contra a mesma paginação alinhada à esquerda.</summary>
    /// <remarks>
    /// Justificar parte cada linha nas fronteiras de branco — uma string por segmento, contra uma
    /// por run —, e é o preço de manter a invariante do caret sem esticar o texto dentro de um run.
    /// O número que importa é a razão entre os dois: se ela crescer, é aqui que aparece.
    /// </remarks>
    [Fact]
    public void Justificar_a_tese_inteira_cabe_no_mesmo_teto()
    {
        var source = LongDocument.Build();
        var measurer = new FakeTextMeasurer();
        var justified = TypographyPreset.Default with { Alignment = TextAlignment.Justify };

        var (leftMs, lines) = Time(source, TypographyPreset.Default, measurer);
        var (justifiedMs, _) = Time(source, justified, measurer);

        output.WriteLine($"linhas          : {lines:N0}");
        output.WriteLine($"à esquerda      : {leftMs:N1} ms   ({leftMs * 1000.0 / lines:N2} µs/linha)");
        output.WriteLine($"justificado     : {justifiedMs:N1} ms   ({justifiedMs * 1000.0 / lines:N2} µs/linha)");
        output.WriteLine($"custo do passe  : {justifiedMs / leftMs:N2}x");

        Assert.True(
            justifiedMs < CeilingMilliseconds,
            $"justificar levou {justifiedMs:N0}ms, além do teto de {CeilingMilliseconds}ms");
    }

    /// <summary>
    /// Uma rajada de teclas num documento de tese <b>justificado e com marcação</b> não remede o
    /// documento — em nenhuma das posições em que o autor digita.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Conta medições, não milissegundos</b>, e é o que faz dele um guard e não um termômetro:
    /// o número não depende da máquina, e o que ele fixa é a propriedade que importa — o caminho
    /// incremental foi tomado. Um teto de tempo diria "está lento" sem dizer por quê, e passaria
    /// verde numa máquina rápida com o reflow recusando toda tecla.
    /// </para>
    /// <para>
    /// <b>O fim do parágrafo está aqui porque era exatamente ele que recusava.</b> A guarda do
    /// caret comparava o offset pós-edição com o intervalo pré-edição do bloco, então toda tecla
    /// digitada no fim de um parágrafo — que é como se escreve — repaginava as 300 folhas. A seta
    /// está pela mesma razão: atravessar bloco recusava, e num editor em que uma linha da fonte é
    /// uma linha na página, cada ↑/↓ atravessa um bloco.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("meio")]
    [InlineData("fim de parágrafo")]
    [InlineData("seta")]
    public void Uma_tecla_num_documento_formatado_nao_remede_o_documento(string position)
    {
        // Folgado de propósito: um bloco requebrado custa dezenas de medições, e a paginação
        // completa custa centenas de milhares. Qualquer coisa entre os dois é recusa disfarçada.
        const int Ceiling = 2_000;

        var source = LongDocument.BuildWithMarkup();
        var justified = TypographyPreset.Default with { Alignment = TextAlignment.Justify };
        var measurer = new CountingMeasurer();

        var caret = position switch
        {
            "meio" => InsideLine(source, 0.5),
            "fim de parágrafo" => source.IndexOf('\n', InsideLine(source, 0.5)),
            _ => InsideLine(source, 0.5),
        };

        var full = Paginate(source, justified, measurer, caret);
        var whole = measurer.Reset();

        var text = source;
        var document = full;
        var worst = 0;

        for (var key = 0; key < 5; key++)
        {
            if (position == "seta")
            {
                // Texto igual, caret adiante: a travessia de bloco, sem edição nenhuma.
                caret = source.IndexOf('\n', caret) + 1;
                document = LayoutEngine.Layout(
                    MarkupParser.Parse(text, justified),
                    PageSettings.A4,
                    measurer,
                    caret,
                    LayoutReuse.Between(text, text, document),
                    justified);
            }
            else
            {
                var edited = text.Insert(caret, "x");
                var reuse = LayoutReuse.Between(text, edited, document);

                // Pós-edição, que é a convenção de quem digita: o caret anda e só então repagina.
                caret++;

                document = LayoutEngine.Layout(
                    MarkupParser.Parse(edited, justified),
                    PageSettings.A4,
                    measurer,
                    caret,
                    reuse,
                    justified);

                text = edited;
            }

            worst = Math.Max(worst, measurer.Reset());
        }

        output.WriteLine($"posição            : {position}");
        output.WriteLine($"paginação completa : {whole:N0} medições");
        output.WriteLine($"pior tecla         : {worst:N0} medições   ({(double)whole / worst:N0}x menos)");

        Assert.True(
            worst <= Ceiling,
            $"a pior tecla no {position} custou {worst:N0} medições, acima do teto de {Ceiling:N0} — "
                + $"a paginação completa custa {whole:N0}, então isto é o reflow incremental recusando");
    }

    /// <summary>Um offset dentro de uma linha de texto, nunca em cima de um <c>\n</c>.</summary>
    private static int InsideLine(string source, double fraction)
    {
        var at = Math.Clamp((int)(source.Length * fraction), 1, source.Length - 1);

        while (at < source.Length - 1 && (source[at] == '\n' || source[at - 1] == '\n'))
        {
            at++;
        }

        return at;
    }

    private static PaginatedDocument Paginate(
        string source,
        TypographyPreset preset,
        ITextMeasurer measurer,
        int caretOffset) =>
        LayoutEngine.Layout(
            MarkupParser.Parse(source, preset), PageSettings.A4, measurer, caretOffset, preset: preset);

    /// <summary>Conta quantos trechos chegaram ao medidor — a testemunha de quanto foi refeito.</summary>
    private sealed class CountingMeasurer : ITextMeasurer
    {
        private readonly FakeTextMeasurer _inner = new();
        private int _calls;

        public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style)
        {
            _calls++;
            return _inner.MeasureWidthPt(text, style);
        }

        public LineMetrics GetLineMetrics(TextStyle style) => _inner.GetLineMetrics(style);

        /// <summary>Devolve a contagem e zera, para que cada tecla seja medida sozinha.</summary>
        public int Reset()
        {
            var calls = _calls;
            _calls = 0;

            return calls;
        }
    }

    /// <summary>O melhor de cinco. Máquina compartilhada mede alto, nunca baixo.</summary>
    /// <remarks>
    /// Uma passada só dava 15% de variação entre execuções — mais do que a diferença que se quer
    /// enxergar. O mínimo é o número menos contaminado por outro processo tendo roubado o núcleo, e
    /// é o que torna a razão entre as duas colunas comparável de uma execução para outra.
    /// </remarks>
    private static (double Milliseconds, int Lines) Time(
        string source,
        TypographyPreset preset,
        ITextMeasurer measurer)
    {
        // Uma passada fora do relógio, pelo mesmo motivo do teste acima.
        var paginated = Paginate(source, preset, measurer);
        var best = double.MaxValue;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = Paginate(source, preset, measurer);
            stopwatch.Stop();

            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }

        return (best, paginated.Pages.Sum(page => page.Lines.Count));
    }

    private static PaginatedDocument Paginate(string source, TypographyPreset preset, ITextMeasurer measurer) =>
        LayoutEngine.Layout(MarkupParser.Parse(source, preset), PageSettings.A4, measurer, preset: preset);
}
