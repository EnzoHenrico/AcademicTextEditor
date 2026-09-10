using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.Core.Parsing;

public enum MarkupTokenKind
{
    /// <summary>Linha de texto comum. Cada uma vira um bloco próprio.</summary>
    Text,

    /// <summary>Linha vazia ou só com espaços. Ocupa uma linha na página, como qualquer outra.</summary>
    BlankLine,

    /// <summary>Linha iniciada por 1 a 6 <c>#</c> seguidos de espaço.</summary>
    Heading,

    /// <summary>Linha contendo apenas <c>\page</c>: quebra de página explícita.</summary>
    PageBreak,

    /// <summary>Linha iniciada por <c>\note id </c>: definição de nota de rodapé.</summary>
    Footnote,
}

/// <summary>
/// Uma linha da fonte já classificada. O tokenizer trabalha em granularidade de linha porque
/// toda a marcação de bloco do MVP é decidida no início da linha.
/// </summary>
/// <remarks>
/// Guarda offsets, nunca substrings: classificar um documento de 300 páginas não deve alocar
/// uma string por linha. Quem precisa do texto fatia a fonte original uma única vez, no parser.
/// </remarks>
/// <param name="Kind">Classificação da linha.</param>
/// <param name="LineStart">Offset do primeiro caractere da linha, incluindo a marcação.</param>
/// <param name="LineLength">Comprimento da linha sem o terminador (<c>\n</c> ou <c>\r\n</c>).</param>
/// <param name="ContentStart">Offset do texto útil: depois do <c>## </c> num heading, igual a
/// <paramref name="LineStart"/> nos demais.</param>
/// <param name="Level">Nível do heading (1 a 6); 0 nos demais.</param>
/// <param name="Alignment">
/// O alinhamento que a marcação da linha pediu, ou <c>null</c> quando não há marcação — e aí quem
/// decide é o preset. Nulo e <paramref name="AlignmentLength"/> zero andam juntos.
/// </param>
/// <param name="AlignmentLength">
/// Quantos caracteres a marcação de alinhamento ocupa no início da linha, incluindo o espaço que a
/// separa do conteúdo. É o que o parser precisa para emitir o run de marcação sem reprocessar a
/// linha — e para saber onde começa a marcação do heading, quando as duas convivem.
/// </param>
/// <param name="IdLength">
/// Comprimento do rótulo de uma definição de nota; 0 nos demais. Ele começa logo depois do
/// marcador e do espaço que o segue, em
/// <c>LineStart + AlignmentLength + MarkupTokenizer.FootnoteMarker.Length + 1</c>.
/// </param>
public readonly record struct MarkupToken(
    MarkupTokenKind Kind,
    int LineStart,
    int LineLength,
    int ContentStart,
    int Level,
    TextAlignment? Alignment = null,
    int AlignmentLength = 0,
    int IdLength = 0)
{
    /// <summary>Comprimento do texto útil. O conteúdo sempre termina junto com a linha.</summary>
    public int ContentLength => LineStart + LineLength - ContentStart;
}
