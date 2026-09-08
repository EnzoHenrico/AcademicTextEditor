using System.Text;

namespace AcademicEditor.Core.IO;

/// <summary>
/// Lê e grava o documento no sistema de arquivos.
/// </summary>
/// <remarks>
/// <para>
/// <b>A gravação é atômica.</b> Escreve num arquivo temporário e só então o move por cima do
/// original. Um <c>File.WriteAllText</c> direto trunca o arquivo antes de escrever: se a máquina
/// cair, o disco encher ou o processo morrer no meio, o que sobra é um arquivo pela metade — e o
/// original, que estava íntegro, já não existe. Uma tese não pode ser perdida assim.
/// </para>
/// <para>
/// O temporário fica <b>no mesmo diretório</b>, e não em <c>Path.GetTempPath()</c>: mover só é
/// atômico dentro do mesmo volume. Entre volumes o move vira copiar-e-apagar, que é exatamente a
/// janela de corrupção que se queria fechar.
/// </para>
/// </remarks>
public sealed class FileDocumentStorage : IDocumentStorage
{
    public async Task<LoadedDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var encoding = DetectEncoding(bytes);

        return new LoadedDocument(Decode(bytes, encoding), encoding);
    }

    public async Task SaveAsync(
        string path,
        string text,
        DocumentEncoding encoding,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(text);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Nome único: dois saves simultâneos do mesmo arquivo não podem disputar o temporário.
        var temporary = Path.Combine(
            directory ?? ".",
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(temporary, Encode(text, encoding), cancellationToken)
                .ConfigureAwait(false);

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // O original continua onde estava; o que sobra para limpar é o temporário. Falhar aqui
            // não pode mascarar a exceção real, que é a que diz por que o save não aconteceu.
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // A marca de ordem de bytes é a única detecção confiável. Sem ela o arquivo é tratado como
    // UTF-8, que é o padrão da web, do git e de todo .md que se encontre por aí — adivinhar
    // codificação por estatística erra em texto curto e erra calado.
    private static DocumentEncoding DetectEncoding(byte[] bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => DocumentEncoding.Utf8Bom,
        [0xFF, 0xFE, ..] => DocumentEncoding.Utf16Le,
        [0xFE, 0xFF, ..] => DocumentEncoding.Utf16Be,
        _ => DocumentEncoding.Utf8,
    };

    private static string Decode(byte[] bytes, DocumentEncoding encoding) => encoding switch
    {
        DocumentEncoding.Utf8Bom => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3),
        DocumentEncoding.Utf16Le => Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
        DocumentEncoding.Utf16Be => Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2),
        _ => Encoding.UTF8.GetString(bytes),
    };

    private static byte[] Encode(string text, DocumentEncoding encoding) => encoding switch
    {
        DocumentEncoding.Utf8Bom => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
        DocumentEncoding.Utf16Le => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
        DocumentEncoding.Utf16Be => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
        _ => Encoding.UTF8.GetBytes(text),
    };
}
