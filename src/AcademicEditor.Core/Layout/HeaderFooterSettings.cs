using System.Globalization;

namespace AcademicEditor.Core.Layout;

/// <summary>
/// Os três campos de uma faixa de cabeçalho ou rodapé: alinhados à esquerda, ao centro e à
/// direita da área de conteúdo.
/// </summary>
/// <remarks>
/// Três campos, e não uma string com alinhamento à parte, porque é o que todo processador de
/// texto oferece e é o que a norma pede: número à direita, título ao centro, autor à esquerda —
/// numa linha só.
/// </remarks>
public readonly record struct PageBand(string Left, string Center, string Right)
{
    public static readonly PageBand Empty = new(string.Empty, string.Empty, string.Empty);

    public bool IsEmpty => Left.Length == 0 && Center.Length == 0 && Right.Length == 0;
}

/// <summary>
/// O que aparece no cabeçalho e no rodapé de cada folha, como modelo a ser resolvido por página.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só o texto; a altura é do <see cref="PageSettings"/>.</b> A faixa é desenhada dentro do
/// espaço que <c>HeaderReservedHeightPt</c>/<c>FooterReservedHeightPt</c> reservaram — reserva
/// zero significa que não há onde desenhar, e nada é desenhado. Duas fontes de verdade para a
/// mesma altura seria uma para divergir da outra; aqui a geometria tem um dono só, que é o mesmo
/// que já decide onde o conteúdo começa.
/// </para>
/// <para>
/// <b>Numerar página não é um recurso à parte</b>: é o caso mais simples do mesmo modelo, um
/// <c>{page}</c> num dos campos.
/// </para>
/// </remarks>
/// <param name="DocumentTitle">
/// O que <c>{title}</c> resolve. Fica aqui, e não numa configuração de norma à parte, porque é o
/// que a faixa precisa saber para se montar — e trocá-lo é trocar o texto do cabeçalho.
/// </param>
public sealed record HeaderFooterSettings(PageBand Header, PageBand Footer, string DocumentTitle = "")
{
    /// <summary>O número desta folha, começando em 1.</summary>
    public const string PageMarker = "{page}";

    /// <summary>Quantas folhas o documento tem.</summary>
    public const string PageCountMarker = "{pages}";

    /// <summary>O <see cref="DocumentTitle"/>.</summary>
    public const string TitleMarker = "{title}";

    /// <summary>Sem cabeçalho e sem rodapé.</summary>
    public static HeaderFooterSettings None { get; } = new(PageBand.Empty, PageBand.Empty);

    /// <summary>ABNT: o número da folha no canto superior direito, e nada mais.</summary>
    public static HeaderFooterSettings Abnt { get; } =
        new(PageBand.Empty with { Right = PageMarker }, PageBand.Empty);

    public bool IsEmpty => Header.IsEmpty && Footer.IsEmpty;

    /// <summary>Resolve os marcadores de um campo para uma folha.</summary>
    /// <remarks>
    /// <c>string.Replace</c> devolve a <b>mesma instância</b> quando não encontra nada, então um
    /// campo de texto literal atravessa isto sem alocar — e um campo vazio nem chega aqui.
    /// </remarks>
    public string Resolve(string field, int pageNumber, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.Length == 0)
        {
            return string.Empty;
        }

        return field
            .Replace(PageMarker, pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(PageCountMarker, pageCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(TitleMarker, DocumentTitle, StringComparison.Ordinal);
    }
}
