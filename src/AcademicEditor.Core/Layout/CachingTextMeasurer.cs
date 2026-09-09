using System.Collections.Concurrent;

using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Guarda a largura já medida de cada trecho, por estilo.
/// </summary>
/// <remarks>
/// <para>
/// Existe por uma medição, não por intuição. Na Fatia 6, uma repaginação de 300 páginas levou
/// 874ms, dos quais <b>97,9% dentro do <see cref="ITextMeasurer"/></b> — e <b>96% das 245.480
/// medições eram repetição</b>. O line breaker mede palavra a palavra, e palavra se repete: um
/// documento de 1 milhão de caracteres tem menos de 10 mil trechos distintos.
/// </para>
/// <para>
/// Decorador, e no Core, por dois motivos. A política de cache fica testável sem subsistema
/// gráfico — que é a razão de <see cref="ITextMeasurer"/> existir — e um exportador PDF a herda
/// junto com o motor de layout, em vez de ela morar dentro da implementação Avalonia.
/// </para>
/// <para>
/// <b>Só a largura.</b> As métricas de linha dependem apenas do estilo: são meia dúzia de entradas,
/// não têm como crescer e já são cacheadas por quem as produz. A largura é que tem espaço de
/// chaves ilimitado, e é ela que precisa de teto.
/// </para>
/// </remarks>
public sealed class CachingTextMeasurer : ITextMeasurer
{
    /// <summary>Teto de trechos guardados por estilo.</summary>
    /// <remarks>
    /// O conjunto de trabalho é o vocabulário do documento — menos de dez mil numa tese. O que
    /// passa disso são os prefixos transitórios da busca binária que posiciona o caret, e é deles
    /// que o teto protege.
    /// </remarks>
    public const int DefaultCapacity = 32_768;

    private readonly ITextMeasurer _inner;
    private readonly int _capacity;
    private readonly ConcurrentDictionary<TextStyle, WidthCache> _byStyle = new();

    public CachingTextMeasurer(ITextMeasurer inner, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _inner = inner;
        _capacity = capacity;
    }

    public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style) =>
        text.IsEmpty
            ? 0.0
            : _byStyle.GetOrAdd(style, static _ => new WidthCache()).Measure(text, style, _inner, _capacity);

    public LineMetrics GetLineMetrics(TextStyle style) => _inner.GetLineMetrics(style);

    /// <remarks>
    /// <c>ConcurrentDictionary</c>, e não <c>Dictionary</c>, porque o layout roda em background e
    /// nada impede uma segunda repaginação de começar antes de a anterior ser descartada — as duas
    /// medem sobre este mesmo cache.
    /// </remarks>
    private sealed class WidthCache
    {
        private readonly ConcurrentDictionary<string, double> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, double>.AlternateLookup<ReadOnlySpan<char>> _lookup;

        public WidthCache() => _lookup = _entries.GetAlternateLookup<ReadOnlySpan<char>>();

        public double Measure(ReadOnlySpan<char> text, TextStyle style, ITextMeasurer inner, int capacity)
        {
            // O acerto não aloca: a busca alternada compara o span com as chaves sem materializar
            // string nenhuma. É metade do ganho — a outra metade é não construir o TextLayout.
            if (_lookup.TryGetValue(text, out var cached))
            {
                return cached;
            }

            var width = inner.MeasureWidthPt(text, style);

            // Sem política de despejo: ao estourar, limpa e recomeça. O conjunto de trabalho se
            // reconstrói numa repaginação, e um LRU custaria mais em contabilidade do que
            // economizaria num cache que quase nunca chega ao teto.
            if (_entries.Count >= capacity)
            {
                _entries.Clear();
            }

            _entries[new string(text)] = width;

            return width;
        }
    }
}
