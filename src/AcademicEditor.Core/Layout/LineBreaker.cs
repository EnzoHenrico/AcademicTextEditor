using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Quebra os runs de um bloco em linhas visuais que caibam numa largura dada (word-wrap greedy).
/// </summary>
/// <remarks>
/// <para>
/// Greedy, não Knuth-Plass: cada linha recebe o máximo de palavras que couber, sem otimizar o
/// documento como um todo. É o que permite quebrar em tempo de digitação — e a justificação
/// ótima está no estacionamento de ideias do roadmap, não no MVP.
/// </para>
/// <para>
/// Trabalha sobre a <b>sequência</b> de runs, não sobre um run de cada vez: uma linha pode
/// misturar estilos diferentes. O parser do MVP emite um run por linha da fonte, mas o motor já
/// está pronto para <c>**negrito**</c> no meio da frase.
/// </para>
/// </remarks>
public static class LineBreaker
{
    /// <param name="includeMarkup">
    /// Se a marcação de bloco entra na linha. Verdadeiro só para o bloco onde o caret está — é o
    /// que revela o <c>## </c> de um título enquanto se escreve nele.
    /// </param>
    public static List<LaidOutLine> BreakIntoLines(
        IReadOnlyList<InlineRun> runs,
        double maxWidthPt,
        ITextMeasurer measurer,
        bool includeMarkup = false)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidthPt);

        var builder = new LineAccumulator(runs, measurer, includeMarkup);
        var chunks = BuildChunks(runs, includeMarkup);
        var index = 0;

        while (index < chunks.Count)
        {
            var chunk = chunks[index];
            var run = runs[chunk.RunIndex];
            var text = run.Text.AsSpan(chunk.Start, chunk.Length);
            var width = measurer.MeasureWidthPt(text, run.Style);

            // Espaço nunca provoca quebra. Consequência desejada: um espaço sobrando no fim da
            // linha pode passar da margem, mas não empurra a próxima palavra para outra linha —
            // é invisível na tela e faz parte do texto que a linha cobre.
            if (chunk.IsSpace)
            {
                builder.Append(chunk, width);
                index++;
                continue;
            }

            if (builder.PenXPt + width <= maxWidthPt)
            {
                builder.Append(chunk, width);
                index++;
                continue;
            }

            if (!builder.HasContent)
            {
                // Palavra mais larga que a página inteira (URL, fórmula). Não há para onde
                // empurrá-la: parte no maior prefixo que couber e continua o resto na linha
                // seguinte. O prefixo tem ao menos um caractere, o que garante progresso.
                var fit = LargestPrefixThatFits(text, run.Style, maxWidthPt, measurer);

                builder.Append(chunk with { Length = fit }, measurer.MeasureWidthPt(text[..fit], run.Style));
                chunks[index] = chunk with { Start = chunk.Start + fit, Length = chunk.Length - fit };
                builder.EndLine();
                continue;
            }

            // Não avança o índice: a palavra que não coube abre a linha seguinte.
            builder.EndLine();
        }

        // Sempre fecha a última linha, mesmo vazia: um parágrafo em branco ou um heading recém
        // aberto ocupam uma linha na página, e o caret precisa de uma linha onde pousar.
        builder.EndLine();

        return builder.Lines;
    }

    /// <summary>
    /// Maior prefixo de <paramref name="text"/> que cabe em <paramref name="maxWidthPt"/>, com no
    /// mínimo um caractere. Busca binária: a largura cresce monotonicamente com o prefixo, então
    /// bastam O(log n) medições em vez de uma por caractere.
    /// </summary>
    private static int LargestPrefixThatFits(
        ReadOnlySpan<char> text,
        TextStyle style,
        double maxWidthPt,
        ITextMeasurer measurer)
    {
        var low = 1;
        var high = text.Length;
        var best = 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (measurer.MeasureWidthPt(text[..mid], style) <= maxWidthPt)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Partir entre os dois halves de um par substituto quebraria o caractere em duas metades
        // inválidas, e cada uma viraria um losango na tela. Recua um; se não há para onde recuar,
        // avança e deixa o par inteiro estourar a margem — em ambos os casos o corte progride.
        if (best < text.Length && char.IsHighSurrogate(text[best - 1]))
        {
            best = best == 1 ? Math.Min(2, text.Length) : best - 1;
        }

        return best;
    }

    /// <summary>
    /// Fatia os runs em blocos alternados de palavra e espaço. É a granularidade em que a decisão
    /// de quebra é tomada; guarda índices, não substrings, para não alocar por palavra.
    /// </summary>
    private static List<Chunk> BuildChunks(IReadOnlyList<InlineRun> runs, bool includeMarkup)
    {
        var chunks = new List<Chunk>();

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            // Descartar aqui, e não montar uma lista filtrada antes, evita uma alocação por bloco
            // a cada repaginação — e são dezenas de milhares de blocos numa tese.
            if (runs[runIndex].IsMarkup && !includeMarkup)
            {
                continue;
            }

            var text = runs[runIndex].Text;
            var position = 0;

            while (position < text.Length)
            {
                var isSpace = char.IsWhiteSpace(text[position]);
                var end = position + 1;

                while (end < text.Length && char.IsWhiteSpace(text[end]) == isSpace)
                {
                    end++;
                }

                chunks.Add(new Chunk(runIndex, position, end - position, isSpace));
                position = end;
            }
        }

        return chunks;
    }

    private readonly record struct Chunk(int RunIndex, int Start, int Length, bool IsSpace);

    /// <summary>
    /// Monta uma linha por vez. Chunks contíguos do mesmo run são fundidos num único
    /// <see cref="LaidOutRun"/>: sem isso, "abc def" viraria três runs e três chamadas de desenho
    /// onde uma basta.
    /// </summary>
    private sealed class LineAccumulator(
        IReadOnlyList<InlineRun> runs,
        ITextMeasurer measurer,
        bool includeMarkup)
    {
        private readonly List<LaidOutRun> _lineRuns = [];

        private int _segmentRun = -1;
        private int _segmentStart;
        private int _segmentLength;
        private double _segmentXPt;
        private double _segmentWidthPt;

        private double _heightPt;
        private double _baselinePt;
        private int _sourceStart = -1;
        private int _sourceEnd = -1;

        public List<LaidOutLine> Lines { get; } = [];

        /// <summary>Largura já ocupada na linha corrente, incluindo espaços.</summary>
        public double PenXPt { get; private set; }

        public bool HasContent => _lineRuns.Count > 0 || _segmentRun >= 0;

        public void Append(Chunk chunk, double widthPt)
        {
            var run = runs[chunk.RunIndex];

            if (_segmentRun == chunk.RunIndex && _segmentStart + _segmentLength == chunk.Start)
            {
                _segmentLength += chunk.Length;
                _segmentWidthPt += widthPt;
            }
            else
            {
                FlushSegment();
                _segmentRun = chunk.RunIndex;
                _segmentStart = chunk.Start;
                _segmentLength = chunk.Length;
                _segmentXPt = PenXPt;
                _segmentWidthPt = widthPt;
            }

            PenXPt += widthPt;
            GrowToFit(run.Style);

            var absoluteStart = run.SourceStart + chunk.Start;
            if (_sourceStart < 0)
            {
                _sourceStart = absoluteStart;
            }

            _sourceEnd = absoluteStart + chunk.Length;
        }

        public void EndLine()
        {
            FlushSegment();

            // Linha sem nenhum chunk (bloco vazio) ainda tem altura e uma posição no buffer:
            // é onde o caret fica quando o autor abre um parágrafo e ainda não digitou nada.
            // A âncora é o primeiro run que a linha de fato usa: com a marcação escondida, um
            // "# " recém-aberto tem de reivindicar o offset do texto, não o do sustenido.
            var anchor = Anchor();

            if (_sourceStart < 0)
            {
                _sourceStart = anchor?.SourceStart ?? 0;
                _sourceEnd = _sourceStart;
            }

            if (_heightPt <= 0)
            {
                GrowToFit(anchor?.Style ?? TextStyle.Body);
            }

            Lines.Add(new LaidOutLine(
                YPt: 0.0,
                _heightPt,
                _baselinePt,
                _lineRuns.ToArray(),
                _sourceStart,
                _sourceEnd - _sourceStart));

            _lineRuns.Clear();
            PenXPt = 0.0;
            _heightPt = 0.0;
            _baselinePt = 0.0;
            _sourceStart = -1;
            _sourceEnd = -1;
        }

        private InlineRun? Anchor()
        {
            foreach (var run in runs)
            {
                if (includeMarkup || !run.IsMarkup)
                {
                    return run;
                }
            }

            return null;
        }

        // A linha acompanha o maior estilo que a compõe: uma palavra em 20pt no meio de texto de
        // 11pt precisa de espaço vertical para os dois, senão os glifos se sobrepõem.
        private void GrowToFit(TextStyle style)
        {
            var metrics = measurer.GetLineMetrics(style);
            _heightPt = Math.Max(_heightPt, metrics.HeightPt);
            _baselinePt = Math.Max(_baselinePt, metrics.BaselinePt);
        }

        private void FlushSegment()
        {
            if (_segmentRun < 0)
            {
                return;
            }

            var run = runs[_segmentRun];

            _lineRuns.Add(new LaidOutRun(
                run.Text.Substring(_segmentStart, _segmentLength),
                run.Style,
                _segmentXPt,
                _segmentWidthPt,
                run.SourceStart + _segmentStart));

            _segmentRun = -1;
        }
    }
}
