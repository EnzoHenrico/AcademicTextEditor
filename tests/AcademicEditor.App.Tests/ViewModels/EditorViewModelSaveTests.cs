using AcademicEditor.App.ViewModels;

using AcademicEditor.Core.IO;
using AcademicEditor.Core.Layout;
using AcademicEditor.Core.Parsing;
using AcademicEditor.Core.Parsing.Ast;

namespace AcademicEditor.App.Tests.ViewModels;

/// <summary>
/// O que vai para o disco é o arquivo inteiro, cabeçalho de metadados incluído.
/// </summary>
public sealed class EditorViewModelSaveTests
{
    private const string Source = "---\ntitle: Uma tese\nauthor: Alguém\n---\ncorpo um\n\ncorpo dois";

    [Fact]
    public async Task Salvar_grava_o_cabecalho_de_metadados()
    {
        var storage = new CapturingStorage();
        var editor = Editor(Source, storage);

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal(Source, storage.Saved);
    }

    [Fact]
    public async Task Salvar_depois_de_digitar_no_corpo_mantem_o_cabecalho()
    {
        var storage = new CapturingStorage();
        var editor = Editor(Source, storage);

        editor.InsertText("X");

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal(Source.Insert(FrontMatter.BodyStart(Source), "X"), storage.Saved);
    }

    /// <summary>
    /// Digitar o cabeçalho do zero e gravar devolve exatamente o que foi digitado.
    /// </summary>
    /// <remarks>
    /// O caminho inverso do de cima: aqui não há cabeçalho para proteger até que a segunda cerca
    /// feche, e a partir dali o caret já está no começo do corpo por ter chegado lá digitando.
    /// </remarks>
    [Fact]
    public async Task Digitar_o_cabecalho_e_salvar_devolve_o_que_foi_digitado()
    {
        var storage = new CapturingStorage();
        var editor = Editor(string.Empty, storage);

        foreach (var line in (string[])["---", "title: Uma tese", "---", "# Introdução"])
        {
            editor.InsertText(line);
            editor.InsertLineBreak();
        }

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal("---\ntitle: Uma tese\n---\n# Introdução\n", storage.Saved);
    }

    /// <remarks>
    /// <c>Ctrl+A</c> pega o corpo, então substituir tudo não pode levar o cabeçalho junto. É o
    /// caminho mais curto para perder os metadados sem perceber: uma tecla depois do atalho.
    /// </remarks>
    [Fact]
    public async Task Selecionar_tudo_e_digitar_nao_apaga_o_cabecalho()
    {
        var storage = new CapturingStorage();
        var editor = Editor(Source, storage);

        editor.SelectAll();
        editor.InsertText("outro corpo");

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal("---\ntitle: Uma tese\nauthor: Alguém\n---\noutro corpo", storage.Saved);
    }

    [Fact]
    public async Task Backspace_no_comeco_do_corpo_nao_muda_o_arquivo()
    {
        var storage = new CapturingStorage();
        var editor = Editor(Source, storage);

        editor.DeleteBackward();

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal(Source, storage.Saved);
    }

    /// <summary>
    /// Arquivo CRLF: o cabeçalho sobrevive, e o offset do corpo é contado no texto normalizado.
    /// </summary>
    /// <remarks>
    /// Cada linha do cabeçalho tem um <c>\r</c> que o buffer não guarda. Contar o começo do corpo
    /// no texto cru o deixaria adiantado de um caractere por linha, e a primeira tecla cairia
    /// dentro do cabeçalho — o mesmo defeito, por outro caminho.
    /// </remarks>
    [Fact]
    public async Task Abrir_um_arquivo_CRLF_com_cabecalho_e_digitar_mantem_o_cabecalho()
    {
        var storage = new CapturingStorage
        {
            ToLoad = "---\r\ntitle: Uma tese\r\n---\r\ncorpo",
        };

        var editor = Editor(string.Empty, storage);

        await editor.OpenAsync("/tmp/tese.md");

        editor.InsertText("X");

        await editor.SaveAsAsync("/tmp/tese.md");

        // Gravação é sempre em LF, decidido na Fatia 4.2 da Fase 3.
        Assert.Equal("---\ntitle: Uma tese\n---\nXcorpo", storage.Saved);
    }

    /// <summary>
    /// O fluxo de verdade: abrir, esperar a paginação, digitar, salvar.
    /// </summary>
    /// <remarks>
    /// Os outros testes editam <b>antes</b> do primeiro layout, que é a janela em que as duas
    /// falhas apareceram. Este edita depois, que é o que o autor faz — e é ele que diz se o caminho
    /// comum sempre esteve certo.
    /// </remarks>
    [Fact]
    public async Task Abrir_esperar_o_layout_digitar_e_salvar_mantem_o_cabecalho()
    {
        var storage = new CapturingStorage { ToLoad = Source };
        var editor = Editor(string.Empty, storage);

        await editor.OpenAsync("/tmp/tese.md");
        await WaitForLayout(editor);

        editor.InsertText("X");

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal(Source.Insert(FrontMatter.BodyStart(Source), "X"), storage.Saved);
    }

    /// <summary>
    /// Criar o cabeçalho digitando num documento que já tem texto, com o layout publicando entre
    /// as teclas — que é o roteiro de conferência que foi seguido à mão.
    /// </summary>
    [Fact]
    public async Task Digitar_o_cabecalho_no_topo_de_um_documento_existente_e_salvar()
    {
        var storage = new CapturingStorage { ToLoad = "corpo um\n\ncorpo dois" };
        var editor = Editor(string.Empty, storage);

        await editor.OpenAsync("/tmp/tese.md");
        await WaitForLayout(editor);

        foreach (var line in (string[])["---", "title: Uma tese", "---"])
        {
            editor.InsertText(line);
            editor.InsertLineBreak();

            await WaitForPublish(editor);
        }

        await editor.SaveAsAsync("/tmp/tese.md");

        Assert.Equal("---\ntitle: Uma tese\n---\ncorpo um\n\ncorpo dois", storage.Saved);
    }

    /// <summary>Espera a próxima publicação de layout, para que a tecla seguinte veja o estado novo.</summary>
    private static async Task WaitForPublish(EditorViewModel editor)
    {
        var published = new TaskCompletionSource();

        void Handler(object? sender, EventArgs args) => published.TrySetResult();

        editor.Invalidated += Handler;

        try
        {
            await Task.WhenAny(published.Task, Task.Delay(2000));
        }
        finally
        {
            editor.Invalidated -= Handler;
        }
    }

    /// <summary>Espera o layout em voo publicar, ou desiste.</summary>
    private static async Task WaitForLayout(EditorViewModel editor)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (editor.Paginated.Pages[0].Lines.Count > 0)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("a paginação não chegou");
    }

    private static EditorViewModel Editor(string text, IDocumentStorage storage) =>
        new(
            new StubMeasurer(),
            PageSettings.Uniform(widthPt: 200.0, heightPt: 200.0, marginPt: 10.0),
            text,
            storage);

    private sealed class CapturingStorage : IDocumentStorage
    {
        public string Saved { get; private set; } = string.Empty;

        public string ToLoad { get; init; } = string.Empty;

        public Task<LoadedDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoadedDocument(ToLoad, DocumentEncoding.Utf8));

        public Task SaveAsync(
            string path,
            string text,
            DocumentEncoding encoding,
            CancellationToken cancellationToken = default)
        {
            Saved = text;
            return Task.CompletedTask;
        }
    }

    // Larguras redondas, sem subsistema gráfico: o que estes testes afirmam é sobre o buffer, não
    // sobre a folha.
    private sealed class StubMeasurer : ITextMeasurer
    {
        public double MeasureWidthPt(ReadOnlySpan<char> text, TextStyle style) => text.Length * 10.0;

        public LineMetrics GetLineMetrics(TextStyle style) => new(20.0, 16.0);
    }
}
