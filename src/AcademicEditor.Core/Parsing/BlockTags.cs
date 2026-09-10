namespace AcademicEditor.Core.Parsing;

/// <summary>
/// As tags de bloco do editor — <c>\page</c>, <c>\toc</c> — e a regra que decide o que é uma.
/// </summary>
/// <remarks>
/// <para>
/// <b>Uma regra, e não um <c>if</c> por marcador.</b> Enquanto havia só o <c>\page</c>, reconhecê-lo
/// era uma comparação de string no tokenizer; o <c>\toc</c> seria a segunda, e a cópia dela no
/// <c>BlockAlignment</c> já era a terceira. Cinco cópias de "onde a linha termina", com três
/// semânticas, foi o que deixou a Fatia 4 violar a Fatia 5.3 — a mesma armadilha, e a resposta é a
/// mesma: um dono só.
/// </para>
/// <para>
/// <b>A gramática tem três formas, e só a primeira tem máquina:</b>
/// </para>
/// <list type="bullet">
/// <item><description><c>\tag</c> — sozinha numa linha, o bloco inteiro. É a única implementada;
/// <c>\page</c> e <c>\toc</c> são dela.</description></item>
/// <item><description><c>\tag\</c> … <c>/tag/</c> — abertura e fechamento de um bloco com conteúdo.
/// <b>Reservada, sem máquina.</b> O cabeçalho de metadados, que seria o primeiro cliente, ficou em
/// <c>---</c> para ser lido por Pandoc e Obsidian — então a forma pareada não tem nenhum, e
/// construí-la antes do segundo membro é a indireção que este projeto recusa em outros
/// lugares.</description></item>
/// </list>
/// <para>
/// <b>Tag desconhecida ou malformada é texto literal na folha.</b> <c>\meta</c> e <c>\meta\</c>
/// diferem por um caractere, e um erro de digitação que some da tela é a wrongness silenciosa que
/// este editor evita: quem escreveu vê o que escreveu, e corrige.
/// </para>
/// <para>
/// A linha inteira, aparada, tem de ser exatamente a tag — mesma regra que o <c>\page</c> já
/// seguia, e é ela que impede uma data ou um caminho de arquivo de virar marcador.
/// </para>
/// </remarks>
public static class BlockTags
{
    /// <summary>Quebra de página explícita.</summary>
    public const string PageBreak = "page";

    /// <summary>Sumário automático.</summary>
    public const string TableOfContents = "toc";

    private const char Sigil = '\\';

    /// <summary>
    /// Lê o nome da tag de uma linha, conhecida ou não.
    /// </summary>
    /// <remarks>
    /// Devolve o nome mesmo quando ele não significa nada — é o tokenizer que decide o que fazer
    /// com <c>\naoexiste</c>, e ele decide texto literal. Separar as duas perguntas é o que permite
    /// a esta classe não saber o que cada tag faz.
    /// </remarks>
    public static bool TryRead(ReadOnlySpan<char> line, out ReadOnlySpan<char> name)
    {
        var trimmed = line.Trim();

        name = default;

        if (trimmed.Length < 2 || trimmed[0] != Sigil)
        {
            return false;
        }

        var rest = trimmed[1..];

        // Só letras: é o que separa "\page" de "\page\" (reservada), de "\*" (escape de marcação,
        // se um dia entrar) e de qualquer coisa que o autor tenha digitado por acidente.
        foreach (var character in rest)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        name = rest;

        return true;
    }

    /// <summary>A linha é exatamente esta tag?</summary>
    public static bool Is(ReadOnlySpan<char> line, string tag) =>
        TryRead(line, out var name) && name.SequenceEqual(tag);

    /// <summary>
    /// A linha é um marcador de bloco de alguma tag <b>conhecida</b>?
    /// </summary>
    /// <remarks>
    /// A pergunta que o <c>BlockAlignment</c> faz: marcar um <c>\page</c> ou um <c>\toc</c> com
    /// alinhamento faria dele texto, e desfaria a quebra de página ou o sumário. Uma tag
    /// desconhecida é texto, e texto se alinha.
    /// </remarks>
    public static bool IsMarker(ReadOnlySpan<char> line) =>
        TryRead(line, out var name)
        && (name.SequenceEqual(PageBreak) || name.SequenceEqual(TableOfContents));
}
