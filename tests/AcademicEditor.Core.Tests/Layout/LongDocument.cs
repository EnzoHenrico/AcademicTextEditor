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
