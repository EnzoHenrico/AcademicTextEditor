using AcademicEditor.Core.Layout.Model;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Até onde uma linha chega, nas três medidas que a regra da margem usa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe para ter um dono só.</b> Antes desta classe, "onde a linha termina" era recalculado
/// em cinco lugares com três semânticas diferentes — dois deles chamando de <i>ink</i> o que era
/// extensão crua. Foi por esse vão que a justificação da Fase 6, Fatia 4 voltou a pendurar grupos
/// inteiros de branco fora da margem sem que nenhum teste ficasse vermelho: quem quebrava a linha
/// e quem a alinhava mediam coisas diferentes e nenhum dos dois sabia disso.
/// </para>
/// <para>
/// A regra da margem, que estas três medidas servem: <b>nenhum glifo além da margem</b>
/// (<see cref="InkExtentPt"/>) e <b>nenhuma linha além dela por mais de um branco</b>
/// (<see cref="ExtentPt"/> contra <see cref="AlignExtentPt"/>).
/// </para>
/// </remarks>
public static class LineExtents
{
    /// <summary>Até onde a linha chega como foi assentada, com o branco final incluído.</summary>
    /// <remarks>Sem medir: o último run já está posicionado.</remarks>
    public static double ExtentPt(LaidOutLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.Runs.Count == 0 ? 0.0 : line.Runs[^1].XPt + line.Runs[^1].WidthPt;
    }

    /// <summary>Até onde a linha desenha alguma coisa.</summary>
    /// <remarks>
    /// O branco final é descontado de propósito: ocupa largura e não põe glifo nenhum no papel, e é
    /// essa distinção que faz centralizar uma linha terminada em espaço não deslocá-la meio
    /// caractere. Recua run a run — um run inteiramente branco no fim não é hipótese: basta a
    /// justificação partir a linha nas fronteiras de branco, ou o estilo mudar antes do espaço.
    /// </remarks>
    public static double InkExtentPt(LaidOutLine line, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(measurer);

        for (var index = line.Runs.Count - 1; index >= 0; index--)
        {
            var run = line.Runs[index];
            var ink = run.Text.AsSpan().TrimEnd();

            if (ink.Length > 0)
            {
                return run.XPt + measurer.MeasureWidthPt(ink, run.Style);
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Até onde a linha chega descontando <b>no máximo um</b> caractere em branco do fim — a
    /// extensão que tem de caber na margem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É a tolerância da Fase 3, Fatia 5.3 escrita como medida em vez de estado da caneta. O
    /// <c>LineBreaker</c> a aplica ao quebrar (pendura um branco e desce o resto do grupo), mas
    /// aplicá-la lá não bastava: o alinhamento roda depois e reposiciona tudo. Alinhar por
    /// <see cref="InkExtentPt"/> devolve o grupo <i>inteiro</i> de brancos à sobra e o reemite
    /// depois da margem — uma linha terminada em quatro espaços saía 40pt fora do papel.
    /// </para>
    /// <para>
    /// Um caractere, não uma largura: um TAB pendurado pendura a largura dele. É a mesma unidade
    /// em que a tolerância foi decidida, e a mesma que o <c>LineBreaker</c> conta.
    /// </para>
    /// </remarks>
    public static double AlignExtentPt(LaidOutLine line, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(measurer);

        var extentPt = ExtentPt(line);

        if (line.Runs.Count == 0)
        {
            return extentPt;
        }

        var last = line.Runs[^1];
        var text = last.Text;

        if (text.Length == 0 || !char.IsWhiteSpace(text[^1]))
        {
            return extentPt;
        }

        // Um branco nunca é par substituto, então a última unidade UTF-16 é o caractere inteiro.
        return extentPt - measurer.MeasureWidthPt(text.AsSpan(text.Length - 1), last.Style);
    }
}
