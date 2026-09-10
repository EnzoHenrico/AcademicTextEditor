using System.Text;

namespace AcademicEditor.Core.Tests.Layout;

/// <summary>
/// O documento do tamanho de uma tese que as medições usam.
/// </summary>
/// <remarks>
/// Parágrafos separados por linha em branco — a forma que o autor escreve e a que o parser vê:
/// uma linha de fonte por parágrafo, quebrada pela largura da página. Palavras de comprimentos
/// variados de propósito: com palavra de tamanho fixo a quebra cai sempre no mesmo lugar e o
/// line breaker nunca exercita o caminho da palavra que não coube.
/// </remarks>
internal static class LongDocument
{
    private static readonly string[] Words =
    [
        "paginação", "documento", "acadêmico", "layout", "de", "texto", "em", "tempo", "real",
        "com", "quebra", "por", "largura", "da", "página", "e", "medição", "tipográfica",
    ];

    public const int Paragraphs = 640;
    public const int WordsPerParagraph = 100;

    /// <summary>
    /// O mesmo documento com a marcação que o aplicativo suporta.
    /// </summary>
    /// <remarks>
    /// <b>Existe porque medir prosa lisa mede um documento que ninguém escreve.</b> Os números da
    /// Fase 6 saíram todos do <see cref="Build"/>, num corpus sem um único <c>**negrito**</c>,
    /// <c>$fórmula$</c> ou <c>[^nota]</c> — e foi assim que o custo por tecla de um documento
    /// formatado passou três fatias sem ser medido. <see cref="Build"/> fica intacto para os
    /// números antigos continuarem comparáveis.
    /// </remarks>
    public static string BuildWithMarkup(
        int paragraphs = Paragraphs,
        int wordsPerParagraph = WordsPerParagraph)
    {
        var builder = new StringBuilder();
        var word = 0;
        var note = 0;

        for (var paragraph = 0; paragraph < paragraphs; paragraph++)
        {
            if (paragraph % 8 == 0)
            {
                builder.Append("## Um título qualquer\n\n");
            }

            if (paragraph % 20 == 19)
            {
                builder.Append(":-: ");
            }

            for (var index = 0; index < wordsPerParagraph; index++)
            {
                var text = Words[word++ % Words.Length];

                // Uma palavra enfatizada a cada trinta, que é densidade de prosa acadêmica.
                builder.Append((word % 30) switch
                {
                    7 => $"**{text}**",
                    23 => $"*{text}*",
                    _ => text,
                });

                builder.Append(index == wordsPerParagraph - 1 ? string.Empty : " ");
            }

            if (paragraph % 10 == 3)
            {
                builder.Append(" $E = mc^2$");
            }

            if (paragraph % 6 == 1)
            {
                builder.Append($"[^{++note}]");
            }

            builder.Append("\n\n");
        }

        return builder.ToString();
    }

    public static string Build(int paragraphs = Paragraphs, int wordsPerParagraph = WordsPerParagraph)
    {
        var builder = new StringBuilder();
        var word = 0;

        for (var paragraph = 0; paragraph < paragraphs; paragraph++)
        {
            for (var index = 0; index < wordsPerParagraph; index++)
            {
                builder.Append(Words[word++ % Words.Length]);
                builder.Append(index == wordsPerParagraph - 1 ? '\n' : ' ');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}
