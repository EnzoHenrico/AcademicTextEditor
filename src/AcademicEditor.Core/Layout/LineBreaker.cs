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
    /// <param name="preset">
    /// A norma tipográfica, de onde sai o entrelinhamento. <c>null</c> usa o
    /// <see cref="TypographyPreset.Default"/>.
    /// </param>
    /// <param name="alignment">
    /// Como distribuir as linhas na largura. O alinhamento é aplicado <b>aqui</b>, e não num passe
    /// do <c>LayoutEngine</c>, para que o reflow incremental o herde: requebrar o bloco sujo tem de
    /// devolvê-lo alinhado como estava.
    /// </param>
    public static List<LaidOutLine> BreakIntoLines(
        IReadOnlyList<InlineRun> runs,
        double maxWidthPt,
        ITextMeasurer measurer,
        bool includeMarkup = false,
        TypographyPreset? preset = null,
        TextAlignment alignment = TextAlignment.Left,
        IReadOnlyList<FootnoteCall>? calls = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidthPt);

        var builder = new LineAccumulator(runs, measurer, includeMarkup, preset ?? TypographyPreset.Default);
        var chunks = BuildChunks(runs, includeMarkup);
        var index = 0;

        while (index < chunks.Count)
        {
            var chunk = chunks[index];
            var run = runs[chunk.RunIndex];
            var text = run.Text.AsSpan(chunk.Start, chunk.Length);
            var width = measurer.MeasureWidthPt(text, run.Style);

            if (builder.PenXPt + width <= maxWidthPt)
            {
                builder.Append(chunk, width);
                index++;
                continue;
            }

            // Não coube — e a margem vale para o branco também, com uma tolerância de UM caractere.
            //
            // É o que resolve a quebra comum, "palavra espaço palavra": ali o que sobra é um espaço
            // só, ele fica pendurado na margem sem desenhar nada, e a linha de baixo começa na
            // palavra. Descê-lo faria a linha nascer indentada por um caractere invisível — numa
            // quebra gulosa a folga que sobra é uniforme entre zero e a largura da próxima palavra,
            // então isso cai em cerca de uma quebra a cada seis. O que passa de um caractere desce:
            // são brancos que o autor digitou de propósito, e eles têm lugar na tela.
            //
            // A tolerância não se acumula: com a linha já além da margem, o branco segue o caminho
            // estrito e desce inteiro.
            if (chunk.IsSpace && builder.PenXPt <= maxWidthPt)
            {
                var fits = LargestPrefixThatFits(
                    text,
                    run.Style,
                    maxWidthPt - builder.PenXPt,
                    measurer,
                    minimum: 0);

                // Sempre há o "+1": se o grupo inteiro coubesse, o teste acima o teria levado.
                var taken = fits + 1;

                builder.Append(chunk with { Length = taken }, measurer.MeasureWidthPt(text[..taken], run.Style));

                if (taken == chunk.Length)
                {
                    // Grupo consumido inteiro pela tolerância. Quem fecha a linha é a palavra
                    // seguinte, que já não cabe — fechar aqui emitiria uma linha vazia se este
                    // fosse o último chunk do bloco.
                    index++;
                    continue;
                }

                chunks[index] = chunk with { Start = chunk.Start + taken, Length = chunk.Length - taken };
                builder.EndLine();
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
            }

            // Não avança o índice: a palavra que não coube abre a linha seguinte.
            builder.EndLine();
        }

        // Sempre fecha a última linha, mesmo vazia: um parágrafo em branco ou um heading recém
        // aberto ocupam uma linha na página, e o caret precisa de uma linha onde pousar.
        builder.EndLine();

        ClaimHiddenGaps(builder.Lines, runs);
        AttachCalls(builder.Lines, calls);

        LineAlignment.Apply(builder.Lines, alignment, maxWidthPt, measurer);

        return builder.Lines;
    }

    /// <summary>
    /// Distribui as chamadas de nota do bloco pelas linhas que as contêm.
    /// </summary>
    /// <remarks>
    /// <b>A nota estreia na folha onde a chamada é desenhada</b>, e a chamada está numa linha e não
    /// num bloco: um parágrafo longo cai em duas folhas, e as notas que ele chama se dividem entre
    /// elas. As chamadas vêm em ordem e as linhas também, então uma passada casada resolve — e o
    /// bloco sem chamada nenhuma, que é a esmagadora maioria, sai na primeira comparação.
    /// </remarks>
    private static void AttachCalls(List<LaidOutLine> lines, IReadOnlyList<FootnoteCall>? calls)
    {
        if (calls is null || calls.Count == 0)
        {
            return;
        }

        var at = 0;

        for (var index = 0; index < lines.Count && at < calls.Count; index++)
        {
            var line = lines[index];
            List<string>? mine = null;

            // A última linha leva o que sobrar: uma chamada colada no fim do bloco pode cair fora
            // do trecho desenhado quando a marcação em volta dela está escondida.
            var last = index == lines.Count - 1;

            while (at < calls.Count && (last || calls[at].SourceStart <= line.SourceEnd))
            {
                mine ??= [];
                mine.Add(calls[at++].Id);
            }

            if (mine is not null)
            {
                lines[index] = line with { FootnoteCalls = mine };
            }
        }
    }

    /// <summary>
    /// As linhas do bloco passam a cobri-lo <b>inteiro</b>, sem buraco onde a marcação escondida
    /// não pôs run nenhum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O mapa offset → linha tem de ser total, e não era.</b> Com a marcação escondida os runs
    /// dela são descartados em <see cref="BuildChunks"/>, e cada linha nascia ancorada no primeiro
    /// chunk que de fato usou. Num bloco que começa com <c>#&#160;</c>, <c>:-:&#160;</c> ou
    /// <c>**negrito**</c>, os primeiros offsets do bloco não pertenciam a linha nenhuma — e quem
    /// procura a linha de um offset descoberto acha a <b>última linha do bloco anterior</b>. O
    /// caret era desenhado no parágrafo de cima, longe de onde se errou.
    /// </para>
    /// <para>
    /// <b>O buraco não é só das pontas.</b> Quando a quebra por largura cai em cima de uma marcação
    /// escondida no meio do bloco, a linha de cima termina antes dela e a de baixo começa depois:
    /// o vão fica no meio do parágrafo. Daí a regra ser uma só — <i>cada linha reivindica para trás
    /// até onde a anterior terminou</i>, e a primeira até o começo do bloco.
    /// </para>
    /// <para>
    /// Reivindicar para trás, e não para a frente, tem consequência e ela é desejável: a fronteira
    /// entre as duas linhas passa a ser <b>compartilhada</b>, exatamente como numa quebra por
    /// largura comum, e a <c>CaretAffinity</c> já sabe desempatar. E o offset da marcação de uma
    /// palavra fica com a linha em que a palavra está.
    /// </para>
    /// <para>
    /// As pontas do bloco saem dos próprios runs, e não de um parâmetro novo: a invariante do
    /// <see cref="InlineRun"/> é que todo caractere do bloco entra em exatamente um run, na ordem.
    /// </para>
    /// </remarks>
    private static void ClaimHiddenGaps(List<LaidOutLine> lines, IReadOnlyList<InlineRun> runs)
    {
        if (lines.Count == 0 || runs.Count == 0)
        {
            return;
        }

        var previousEnd = runs[0].SourceStart;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (line.SourceStart > previousEnd)
            {
                // Os runs são relativos ao começo da linha, então recuar o começo empurra todos
                // eles o mesmo tanto. É a única cópia de runs que sobrou nesta classe, e ela custa
                // por bloco requebrado — não por linha reaproveitada, que é o caminho da tecla.
                line = line with
                {
                    SourceStart = previousEnd,
                    SourceLength = line.SourceEnd - previousEnd,
                    Runs = ShiftIntoLine(line.Runs, line.SourceStart - previousEnd),
                };

                lines[index] = line;
            }

            previousEnd = line.SourceEnd;
        }

        // A última também para a frente: um bloco terminado em "**" tem os dois últimos offsets
        // depois do último chunk desenhado.
        var last = lines[^1];

        if (last.SourceEnd < runs[^1].SourceEnd)
        {
            // Só o comprimento: esticar o fim não move o começo, e é do começo que os runs
            // dependem.
            lines[^1] = last with { SourceLength = runs[^1].SourceEnd - last.SourceStart };
        }
    }

    /// <summary>Os mesmos runs, algumas posições adiante dentro da linha.</summary>
    private static LaidOutRun[] ShiftIntoLine(IReadOnlyList<LaidOutRun> runs, int delta)
    {
        var moved = new LaidOutRun[runs.Count];

        for (var index = 0; index < moved.Length; index++)
        {
            moved[index] = runs[index] with { LineOffset = runs[index].LineOffset + delta };
        }

        return moved;
    }

    /// <summary>
    /// Maior prefixo de <paramref name="text"/> que cabe em <paramref name="maxWidthPt"/>. Busca
    /// binária: a largura cresce monotonicamente com o prefixo, então bastam O(log n) medições em
    /// vez de uma por caractere.
    /// </summary>
    /// <param name="minimum">
    /// Menor corte aceitável. Um garante progresso quando não há para onde empurrar o trecho; zero
    /// só vale para o branco, onde "nada cabe" é resposta legítima porque quem leva a linha adiante
    /// é o caractere da tolerância.
    /// </param>
    private static int LargestPrefixThatFits(
        ReadOnlySpan<char> text,
        TextStyle style,
        double maxWidthPt,
        ITextMeasurer measurer,
        int minimum = 1)
    {
        var low = minimum;
        var high = text.Length;
        var best = minimum;

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
        if (best > 0 && best < text.Length && char.IsHighSurrogate(text[best - 1]))
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
        bool includeMarkup,
        TypographyPreset preset)
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
                GrowToFit(anchor?.Style ?? preset.Body);
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
        //
        // O entrelinhamento da norma é aplicado AQUI, onde a altura da linha é decidida, e nunca no
        // desenho: quem consome LaidOutLine — page breaker, caret, seleção, exportador — tem de ver
        // a mesma caixa que a renderização vê. Ver TypographyPreset.Apply.
        private void GrowToFit(TextStyle style)
        {
            var metrics = preset.Apply(measurer.GetLineMetrics(style));
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

            // Relativo ao começo da linha, e não ao buffer: _sourceStart já está preenchido aqui,
            // porque o primeiro Append o define antes de qualquer segmento ser fechado.
            _lineRuns.Add(new LaidOutRun(
                run.Text.Substring(_segmentStart, _segmentLength),
                run.Style,
                _segmentXPt,
                _segmentWidthPt,
                run.SourceStart + _segmentStart - _sourceStart));

            _segmentRun = -1;
        }
    }
}
