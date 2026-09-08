namespace AcademicEditor.Core.IO;

/// <summary>Como o texto estava codificado no arquivo, para gravá-lo de volta do mesmo jeito.</summary>
/// <remarks>
/// O fim de linha <b>não</b> entra aqui: o buffer é sempre LF e a gravação também. O que se
/// preserva é a codificação dos bytes, que é outra coisa — converter um arquivo de UTF-16 para
/// UTF-8 sem pedir seria alterar o que o autor não mandou alterar.
/// </remarks>
public enum DocumentEncoding
{
    /// <summary>UTF-8 sem marca de ordem de bytes. É o que um arquivo novo recebe.</summary>
    Utf8,

    /// <summary>UTF-8 com BOM. Comum em arquivo salvo por ferramenta da Microsoft.</summary>
    Utf8Bom,

    Utf16Le,

    Utf16Be,
}

/// <summary>Um documento lido do disco.</summary>
/// <param name="Text">O conteúdo. Quem o coloca num <c>EditorDocument</c> normaliza o fim de linha.</param>
public readonly record struct LoadedDocument(string Text, DocumentEncoding Encoding);

/// <summary>
/// Ler e gravar o documento. Interface porque os testes precisam de um armazenamento que falhe
/// no meio da escrita — o caso que decide se o original sobrevive.
/// </summary>
public interface IDocumentStorage
{
    Task<LoadedDocument> LoadAsync(string path, CancellationToken cancellationToken = default);

    Task SaveAsync(string path, string text, DocumentEncoding encoding, CancellationToken cancellationToken = default);
}
