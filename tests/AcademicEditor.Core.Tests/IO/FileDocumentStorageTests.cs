using System.Text;

using AcademicEditor.Core.IO;

namespace AcademicEditor.Core.Tests.IO;

public sealed class FileDocumentStorageTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"academiceditor-tests-{Guid.NewGuid():N}");

    private readonly FileDocumentStorage _storage = new();

    public FileDocumentStorageTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(DocumentEncoding.Utf8)]
    [InlineData(DocumentEncoding.Utf8Bom)]
    [InlineData(DocumentEncoding.Utf16Le)]
    [InlineData(DocumentEncoding.Utf16Be)]
    public async Task Round_trip_preserva_texto_e_codificacao(DocumentEncoding encoding)
    {
        // Acentuação e um par substituto: é onde uma codificação errada aparece.
        const string Content = "Introdução à análise — ção 😀\nsegunda linha";
        var path = Path.Combine(_directory, "doc.md");

        await _storage.SaveAsync(path, Content, encoding);
        var loaded = await _storage.LoadAsync(path);

        Assert.Equal(Content, loaded.Text);
        Assert.Equal(encoding, loaded.Encoding);
    }

    [Fact]
    public async Task Arquivo_sem_bom_e_lido_como_utf8()
    {
        var path = Path.Combine(_directory, "sem-bom.md");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("olá"));

        var loaded = await _storage.LoadAsync(path);

        Assert.Equal("olá", loaded.Text);
        Assert.Equal(DocumentEncoding.Utf8, loaded.Encoding);
    }

    [Fact]
    public async Task Bom_nao_vaza_para_o_texto()
    {
        var path = Path.Combine(_directory, "com-bom.md");
        await File.WriteAllBytesAsync(path, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("olá")]);

        var loaded = await _storage.LoadAsync(path);

        Assert.Equal("olá", loaded.Text);
        Assert.Equal(DocumentEncoding.Utf8Bom, loaded.Encoding);
        Assert.DoesNotContain('﻿', loaded.Text);
    }

    // O teste que justifica a escrita atômica. Um WriteAllText direto trunca o arquivo antes de
    // escrever: morrer no meio deixaria o original pela metade, e ele já não existiria para
    // recuperar. Aqui a escrita falha e o original tem de continuar exatamente como estava.
    [Fact]
    public async Task Falha_no_meio_da_escrita_nao_corrompe_o_original()
    {
        const string Original = "a tese inteira, íntegra";
        var path = Path.Combine(_directory, "tese.md");

        await _storage.SaveAsync(path, Original, DocumentEncoding.Utf8);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.SaveAsync(path, "conteúdo novo que nunca chegou", DocumentEncoding.Utf8, cancelled.Token));

        var loaded = await _storage.LoadAsync(path);

        Assert.Equal(Original, loaded.Text);
    }

    [Fact]
    public async Task Escrita_falha_nao_deixa_temporario_para_tras()
    {
        var path = Path.Combine(_directory, "tese.md");
        await _storage.SaveAsync(path, "conteúdo", DocumentEncoding.Utf8);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.SaveAsync(path, "outro", DocumentEncoding.Utf8, cancelled.Token));

        Assert.Equal(["tese.md"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Save_bem_sucedido_nao_deixa_temporario()
    {
        var path = Path.Combine(_directory, "doc.md");

        await _storage.SaveAsync(path, "um", DocumentEncoding.Utf8);
        await _storage.SaveAsync(path, "dois", DocumentEncoding.Utf8);

        Assert.Equal(["doc.md"], Directory.GetFiles(_directory).Select(Path.GetFileName));
        Assert.Equal("dois", (await _storage.LoadAsync(path)).Text);
    }

    // Sobrescrever com menos texto não pode deixar cauda do conteúdo anterior — o que aconteceria
    // se a gravação abrisse o arquivo sem truncar.
    [Fact]
    public async Task Sobrescrever_com_texto_menor_nao_deixa_sobra()
    {
        var path = Path.Combine(_directory, "doc.md");

        await _storage.SaveAsync(path, "conteúdo bem mais longo do que o próximo", DocumentEncoding.Utf8);
        await _storage.SaveAsync(path, "curto", DocumentEncoding.Utf8);

        Assert.Equal("curto", (await _storage.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task Salva_em_diretorio_que_ainda_nao_existe()
    {
        var path = Path.Combine(_directory, "capitulos", "um.md");

        await _storage.SaveAsync(path, "texto", DocumentEncoding.Utf8);

        Assert.Equal("texto", (await _storage.LoadAsync(path)).Text);
    }
}
