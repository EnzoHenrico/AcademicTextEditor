using AcademicEditor.Core.Layout.Model;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Distribui na largura útil as linhas que o <see cref="LineBreaker"/> já quebrou.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só mexe em posição, nunca em texto</b>, e essa é a regra que sustenta o resto do motor.
/// <c>CaretGeometry.ColumnPt</c> e <c>OffsetInRun</c> acham a coluna medindo um <b>prefixo dentro
/// do run</b>: a invariante é que, dentro de um run, o prefixo medido é a posição desenhada. Por
/// isso justificar não estica o texto de um run — ele <b>parte a linha nas fronteiras de branco</b>
/// e reposiciona cada pedaço. Cada segmento continua sendo texto contíguo medido de ponta a ponta,
/// e caret, seleção e hit test continuam valendo sem saber que a linha foi justificada.
/// </para>
/// <para>
/// Roda dentro do <see cref="LineBreaker"/>, e não num passe do <c>LayoutEngine</c>, para que o
/// caminho incremental o herde: quando o reflow requebra o bloco sujo, ele o requebra alinhado.
/// </para>
/// </remarks>
internal static class LineAlignment
{
    public static void Apply(
        List<LaidOutLine> lines,
        TextAlignment alignment,
        double maxWidthPt,
        ITextMeasurer measurer)
    {
        // Esquerda é o que o line breaker já produz: as linhas nascem encostadas na origem.
        if (alignment == TextAlignment.Left)
        {
            return;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (line.Runs.Count == 0)
            {
                continue;
            }

            var slackPt = maxWidthPt - InkEndPt(line, measurer);

            if (slackPt <= 0.0)
            {
                continue;
            }

            lines[index] = alignment switch
            {
                TextAlignment.Center => Shift(line, slackPt / 2.0),
                TextAlignment.Right => Shift(line, slackPt),

                // A última linha do bloco não é justificada: ela não foi interrompida pela margem,
                // terminou porque o parágrafo acabou. Esticá-la espalharia duas palavras pela folha.
                TextAlignment.Justify when index < lines.Count - 1 => Justify(line, slackPt, measurer),

                _ => line,
            };
        }
    }

    /// <summary>Onde a <b>tinta</b> da linha termina — o branco do fim não conta.</summary>
    /// <remarks>
    /// É o corolário da tolerância de margem da Fatia 5.3: uma linha quebrada pela largura quase
    /// sempre termina num branco pendurado, invisível. Centralizar pela extensão crua deslocaria a
    /// linha meio espaço para a esquerda, e justificar por ela deixaria a margem direita irregular
    /// justamente onde ela devia ficar reta.
    /// <para>
    /// <b>Subtrair é exato:</b> a largura do run é a soma das larguras dos chunks que o compõem, e
    /// o branco do fim foi medido como um chunk próprio, no mesmo estilo. E ele é quase sempre UM
    /// espaço — uma entrada só no cache de medição, reusada por toda linha do documento.
    /// </para>
    /// </remarks>
    private static double InkEndPt(LaidOutLine line, ITextMeasurer measurer)
    {
        var last = line.Runs[^1];
        var text = last.Text.AsSpan();
        var ink = text.TrimEnd();

        return ink.Length == text.Length
            ? last.XPt + last.WidthPt
            : last.XPt + last.WidthPt - measurer.MeasureWidthPt(text[ink.Length..], last.Style);
    }

    /// <summary>A mesma linha, deslocada para a direita. Nenhum run é partido.</summary>
    private static LaidOutLine Shift(LaidOutLine line, double deltaPt)
    {
        var runs = new LaidOutRun[line.Runs.Count];

        for (var index = 0; index < runs.Length; index++)
        {
            runs[index] = line.Runs[index] with { XPt = line.Runs[index].XPt + deltaPt };
        }

        return line with { Runs = runs };
    }

    /// <summary>Distribui a sobra igualmente entre os vãos entre palavras.</summary>
    /// <remarks>
    /// <para>
    /// Igualmente, e não proporcional à largura do vão: é o que todo compositor faz, e é o que
    /// mantém o espaço entre palavras uniforme na linha. A variação <i>entre</i> linhas é o preço
    /// da margem reta, e o que a reduziria é o Knuth-Plass, que está no estacionamento de ideias.
    /// </para>
    /// <para>
    /// <b>Duas passadas, uma alocação.</b> A primeira conta os segmentos e os brancos; a segunda
    /// parte, mede e posiciona direto no array do tamanho certo. A versão com <c>List</c> crescendo
    /// custava caro: uma linha de prosa dá ~16 segmentos, e a lista dobrava quatro vezes por linha.
    /// </para>
    /// </remarks>
    private static LaidOutLine Justify(LaidOutLine line, double slackPt, ITextMeasurer measurer)
    {
        var (segments, blanks) = Count(line);

        // O branco do fim é o pendurado da tolerância de margem: já está fora dela por decisão, e
        // esticá-lo levaria a linha adiante do papel. Não é vão, então não entra na divisão.
        var last = line.Runs[^1].Text;
        var gaps = char.IsWhiteSpace(last[^1]) ? blanks - 1 : blanks;

        // Uma palavra só, ou uma linha sem vão nenhum: não há onde distribuir, e esticar o texto em
        // si romperia a invariante que este arquivo existe para preservar.
        if (gaps <= 0)
        {
            return line;
        }

        var extraPt = slackPt / gaps;
        var runs = new LaidOutRun[segments];
        var penXPt = line.Runs[0].XPt;
        var index = 0;

        foreach (var run in line.Runs)
        {
            var position = 0;

            while (position < run.Text.Length)
            {
                var isBlank = char.IsWhiteSpace(run.Text[position]);
                var end = position + 1;

                while (end < run.Text.Length && char.IsWhiteSpace(run.Text[end]) == isBlank)
                {
                    end++;
                }

                // A medição já está no cache: é a mesma granularidade — chunk a chunk — em que o
                // line breaker mediu esta linha para decidir onde quebrá-la.
                var widthPt = measurer.MeasureWidthPt(run.Text.AsSpan(position, end - position), run.Style);

                if (isBlank && index < segments - 1)
                {
                    widthPt += extraPt;
                }

                runs[index++] = new LaidOutRun(
                    run.Text[position..end],
                    run.Style,
                    penXPt,
                    widthPt,
                    run.SourceStart + position);

                penXPt += widthPt;
                position = end;
            }
        }

        return line with { Runs = runs };
    }

    /// <summary>Quantos segmentos a linha tem, e quantos deles são de branco.</summary>
    private static (int Segments, int Blanks) Count(LaidOutLine line)
    {
        var segments = 0;
        var blanks = 0;

        foreach (var run in line.Runs)
        {
            var position = 0;

            while (position < run.Text.Length)
            {
                var isBlank = char.IsWhiteSpace(run.Text[position]);
                position++;

                while (position < run.Text.Length && char.IsWhiteSpace(run.Text[position]) == isBlank)
                {
                    position++;
                }

                segments++;

                if (isBlank)
                {
                    blanks++;
                }
            }
        }

        return (segments, blanks);
    }

}
