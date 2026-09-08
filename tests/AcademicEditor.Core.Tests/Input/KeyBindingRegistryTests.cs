using AcademicEditor.Core.Input;

namespace AcademicEditor.Core.Tests.Input;

public sealed class KeyBindingRegistryTests
{
    private static readonly CommandId Save = new("save");
    private static readonly CommandId Other = new("other");

    [Fact]
    public void Atalho_de_um_passo_resolve()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Save);

        var resolution = registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.S)]);

        Assert.Equal(ChordStatus.Resolved, resolution.Status);
        Assert.Equal(Save, resolution.Command);
    }

    [Fact]
    public void Tecla_sem_binding_nao_resolve_nada()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(ChordStatus.None, registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.Q)]).Status);
    }

    // O modificador faz parte do passo: Ctrl+S e S são atalhos diferentes, senão digitar "s"
    // salvaria o arquivo.
    [Fact]
    public void Modificador_faz_parte_do_passo()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(ChordStatus.None, registry.Resolve(ShortcutScope.Global, [new KeyStroke(KeyCode.S)]).Status);
    }

    // A razão de ChordSequence nascer com N passos: o primeiro passo não executa nada, só deixa a
    // sequência pendente. Sem isso o despachante teria de ser reescrito para o primeiro chord real.
    [Fact]
    public void Atalho_de_dois_passos_fica_pendente_no_primeiro()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)), Save);

        Assert.Equal(ChordStatus.Pending, registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.K)]).Status);

        var resolved = registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.K), Ctrl(KeyCode.S)]);

        Assert.Equal(ChordStatus.Resolved, resolved.Status);
        Assert.Equal(Save, resolved.Command);
    }

    [Fact]
    public void Segundo_passo_errado_abandona_a_sequencia()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)), Save);

        Assert.Equal(
            ChordStatus.None,
            registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.K), Ctrl(KeyCode.Z)]).Status);
    }

    [Fact]
    public void Escopo_do_editor_enxerga_os_atalhos_globais()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(ChordStatus.Resolved, registry.Resolve(ShortcutScope.Editor, [Ctrl(KeyCode.S)]).Status);
    }

    [Fact]
    public void Escopo_global_nao_enxerga_os_do_editor()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Editor, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(ChordStatus.None, registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.S)]).Status);
    }

    [Fact]
    public void O_mais_especifico_ganha()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Other);
        registry.Bind(ShortcutScope.Editor, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(Save, registry.Resolve(ShortcutScope.Editor, [Ctrl(KeyCode.S)]).Command);
    }

    // Reconfigurar uma tecla não pode depender de o binding antigo ter sido removido antes, senão
    // qual dos dois ganha vira ordem de carregamento.
    [Fact]
    public void Registrar_de_novo_substitui()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Other);
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(Save, registry.Resolve(ShortcutScope.Global, [Ctrl(KeyCode.S)]).Command);
    }

    [Fact]
    public void Sequencia_vazia_nao_resolve()
    {
        var registry = new KeyBindingRegistry();
        registry.Bind(ShortcutScope.Global, Chord(Ctrl(KeyCode.S)), Save);

        Assert.Equal(ChordStatus.None, registry.Resolve(ShortcutScope.Global, []).Status);
    }

    [Fact]
    public void Atalho_sem_passo_algum_e_erro_de_programacao()
    {
        Assert.Throws<ArgumentException>(() => new ChordSequence());
    }

    [Fact]
    public void Descricao_legivel_serve_para_menu_e_diagnostico()
    {
        Assert.Equal("Ctrl+K, Ctrl+S", Chord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)).ToString());
        Assert.Equal("Ctrl+Shift+Z", Chord(new KeyStroke(KeyCode.Z, ModifierKeys.Control | ModifierKeys.Shift)).ToString());
    }

    private static ChordSequence Chord(params KeyStroke[] strokes) => new(strokes);

    private static KeyStroke Ctrl(KeyCode key) => new(key, ModifierKeys.Control);
}
