using AcademicEditor.Core.Layout;

namespace AcademicEditor.Core.Parsing;

/// <summary>
/// O que o cabeçalho do arquivo diz sobre o documento.
/// </summary>
/// <remarks>
/// <para>
/// <b>É lógica do Core que o App consulta</b>, e não um bloco que o motor desenha: o cabeçalho nunca
/// aparece na folha. O que ele alimenta — a norma tipográfica, o <c>{title}</c> do cabeçalho de
/// página, o caminho do <c>.bib</c> — são decisões que antes nasciam fixas no <c>MainWindow</c> e
/// não sobreviviam a fechar o aplicativo.
/// </para>
/// <para>
/// <b>Chave desconhecida não é erro</b>, e chave conhecida com valor que não resolve <b>é</b>: um
/// <c>preset: xyz</c> deixa <see cref="Typography"/> nulo e <see cref="PresetName"/> preenchido,
/// que é o que permite a quem chamou dizer na barra de status o que aconteceu em vez de compor o
/// documento na norma errada sem avisar.
/// </para>
/// </remarks>
public sealed record DocumentMetadata(
    string? Title,
    string? Author,
    string? Bibliography,
    string? PresetName,
    TypographyPreset? Typography)
{
    /// <summary>Documento sem cabeçalho: tudo nulo, e quem consulta usa os seus padrões.</summary>
    public static DocumentMetadata None { get; } = new(null, null, null, null, null);

    /// <summary>O título do trabalho, que é o que o <c>{title}</c> do cabeçalho resolve.</summary>
    public const string TitleKey = "title";

    public const string AuthorKey = "author";

    /// <summary>Caminho do <c>.bib</c>, relativo ao documento. Guardado desde já para a Fatia 5c.</summary>
    public const string BibliographyKey = "bib";

    /// <summary>Qual norma compõe o documento.</summary>
    public const string PresetKey = "preset";

    /// <summary>A norma que compõe o documento: a do cabeçalho, ou a de quem chamou.</summary>
    /// <remarks>
    /// <b>Um dono só para "o cabeçalho manda".</b> A regra é de uma linha, e é exatamente por isso
    /// que ela precisa de dono: escrita à mão em cada consumidor — o aplicativo, a captura de tela,
    /// um exportador — a primeira divergência seria uma conferência descrevendo um documento que
    /// ninguém abre.
    /// </remarks>
    public TypographyPreset NormOver(TypographyPreset fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        return Typography ?? fallback;
    }

    /// <summary>As faixas de quem chamou, com o <c>{title}</c> resolvido pelo cabeçalho.</summary>
    /// <remarks>
    /// Sem <c>title</c> no cabeçalho, o que sobra é o que o aplicativo já fazia: o nome do arquivo.
    /// Um trabalho chamado "Dissertacao_v3_FINAL" no alto de trezentas folhas é o sintoma que este
    /// campo existe para acabar.
    /// </remarks>
    public HeaderFooterSettings BandsOver(HeaderFooterSettings fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        return Title is { } title ? fallback with { DocumentTitle = title } : fallback;
    }

    public static DocumentMetadata From(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (FrontMatter.BodyStart(source) == 0)
        {
            return None;
        }

        var preset = FrontMatter.Read(source, PresetKey);

        return new DocumentMetadata(
            FrontMatter.Read(source, TitleKey),
            FrontMatter.Read(source, AuthorKey),
            FrontMatter.Read(source, BibliographyKey),
            preset,
            Resolve(preset));
    }

    /// <remarks>
    /// Dois nomes, que são os dois presets que existem. Tornar a tipografia <b>editável</b> — outra
    /// família, outro corpo, outro entrelinhamento — é "Normas configuráveis", que está no
    /// estacionamento de ideias e é uma fatia inteira: mexe na chave do cache de medição.
    /// </remarks>
    private static TypographyPreset? Resolve(string? name) => name?.ToLowerInvariant() switch
    {
        "abnt" => TypographyPreset.Abnt,
        "default" => TypographyPreset.Default,
        _ => null,
    };
}
