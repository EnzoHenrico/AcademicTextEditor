# Roadmap — AcademicEditor

Documento vivo: marque os checkboxes conforme avança e mova o status da fase.
Contexto do produto em [`context.md`](context.md); padrão de diretórios em
[`Directories.md`](Directories.md); regras de arquitetura e estilo em
[`CLAUDE.md`](../CLAUDE.md).

**Status:** `✅ concluída` · `🔨 em andamento` · `⬜ planejada`

**Critério de conclusão de qualquer item:** `./dev.sh check` verde (guarda de arquitetura +
build sem warnings + testes). Um item não é dado como pronto com o gate vermelho.

---

## Fase 0 — Scaffold + Git ✅

Commit `c6ab249`.

- [x] Solução `.slnx` com Core (sem UI), App (Avalonia) e Tests
- [x] Referências ligadas: App → Core, Tests → Core
- [x] `Directory.Build.props` (net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors)
- [x] `.gitignore`, `.editorconfig`, `CLAUDE.md`
- [x] Repositório na branch `main`
- [x] Validado: Avalonia 12.1.2 compila em net10.0 sem warnings

---

## Fase 1 — Tooling de build e gate de qualidade ✅

Commit `55f1ed6`.

- [x] `dev.sh` local (não versionado) com `check` / `build` / `test` / `run` / `publish` / `clean` / `install-hooks`
- [x] Guarda de arquitetura em dois níveis (PackageReference direto + vazamento transitivo)
- [x] Hook `pre-commit` bloqueando commits sem build e testes verdes
- [x] `do_test` falha quando nenhum teste é descoberto (`dotnet test` retorna 0 nesse caso)
- [x] `ArchitectureTests` — mesma regra verificada no assembly compilado
- [x] Testes negativos do gate executados: violação de arquitetura e erro de compilação, ambos bloqueando commit
- [x] `publish` gera executável self-contained que roda sem o SDK

---

## Fase 2 — Reestruturação de diretórios ✅

Padrão definido em [`Directories.md`](Directories.md). Feito antes do MVP porque mover pastas
com o Core vazio é trivial; depois de 40 arquivos de motor, é refatoração cara.

- [x] `docs/` com `context.md`, `ROADMAP.md`, `Directories.md` (`git mv`, histórico preservado)
- [x] Pastas do Core: `Text/`, `Parsing/Ast/`, `Layout/Model/`, `IO/`, `Input/`, `State/`
- [x] Pastas do App: `Views/`, `ViewModels/`, `Controls/`, `Rendering/`, `Input/`, `Assets/`
- [x] `MainWindow` movida para `Views/` (`x:Class`, namespace e `App.axaml.cs` alinhados)
- [x] `Directories.md` revisado: `Layout/` e `Rendering/` acrescentados, `Interfaces/` descartada
- [x] Guarda de namespaces no `dev.sh` — pasta = namespace, barrado no `check` e no `pre-commit`
- [x] Teste negativo: namespace errado falha o gate com mensagem do que corrigir

---

## Fase 3 — MVP 🔨

**Objetivo:** abrir um `.md`, editar, ver a paginação recalcular, salvar, reabrir e obter o
mesmo conteúdo. É aqui que o diferencial do produto (paginação como layout) fica de pé.

### Buffer de texto
- [ ] `Piece` (`readonly struct`: Source, Start, Length) e `PieceTable` com `Insert`/`Delete`
- [ ] `TextBufferSnapshot` imutável, consumido pelo layout sem travar a digitação
- [ ] `UndoRedoStack` guardando delta estrutural da piece list, não cópias de texto
- [ ] `EditorDocument` orquestrando buffer + histórico + evento de mudança (`TextEdit`)
- [ ] `PieceTableTests`: insert início/meio/fim, delete cruzando pieces, undo/redo encadeado
- [ ] Teste diferencial de stress: N edições aleatórias vs. `StringBuilder` de referência

### Markup (subconjunto mínimo)
- [ ] `MarkupTokenizer` + `MarkupParser`
- [ ] AST: `ParagraphNode`, `HeadingNode`, `InlineRun` (span com estilo), quebra de página explícita
- [ ] `MarkupParserTests`: tabela markup → AST esperado

### Motor de layout / paginação
- [ ] `PageSettings` (A4/Letter + margens, em pontos; `ContentWidthPt`/`ContentHeightPt`)
- [ ] `ITextMeasurer` no Core + `AvaloniaTextMeasurer` no App
- [ ] `LineBreaker` greedy word-wrap; `LaidOutLine` guardando `SourceStart`/`SourceLength`
- [ ] `PageBreaker` com `HeaderReservedHeightPt`/`FooterReservedHeightPt` reservados (0 no MVP)
- [ ] `LayoutEngine` produzindo `PaginatedDocument` imutável
- [ ] Recompute em background (`Task.Run`) + publicação via `Dispatcher.UIThread.Post` + debounce
- [ ] `LineBreakerTests`/`PageBreakerTests` com `FakeTextMeasurer` determinístico
- [ ] Casos de borda: palavra mais larga que a página, parágrafo vazio, quebra explícita

### Renderização e input
- [ ] `PageSurface : Control` em `Controls/`, dentro de `ScrollViewer`; `Render` só desenha
- [ ] `PageRenderer` desenhando páginas com espaçamento visual entre elas
- [ ] Conversão pt → DIP (`* 96/72`) isolada na camada de renderização
- [ ] Digitação via evento `TextInput` (não `KeyDown.Key`), por causa de IME e layouts internacionais
- [ ] Caret e navegação básica (setas, Home/End, PageUp/PageDown)

### Arquivo
- [ ] `IDocumentStorage` + `FileDocumentStorage` async
- [ ] Save atômico: escreve `.tmp` no mesmo diretório e `File.Move(..., overwrite: true)`
- [ ] Detecção de BOM/encoding e normalização CRLF/LF
- [ ] Diálogos nativos via `IStorageProvider`
- [ ] `FileDocumentStorageTests`: falha no meio da escrita não corrompe o original

### Atalhos
- [ ] `CommandId`, `ShortcutScope`, `ChordSequence`, `KeyBindingRegistry` (já suportando N passos)
- [ ] `ShortcutDispatcher` no `PreviewKeyDown` (tunneling) + `FocusScopeTracker`
- [ ] Bindings: `Ctrl+S`, `Ctrl+O`, `Ctrl+Z`/`Ctrl+Y`

### Fechamento da fase
- [ ] Teste manual end-to-end via `./dev.sh run`
- [ ] Documento longo (~300 páginas) para medir latência de repaginação e decidir se o
      reflow incremental precisa ser antecipado da Fase 4

---

## Fase 4 — Editor de verdade ⬜

- [ ] Reflow incremental (dirty-range em 3 níveis: parser → line-breaker → page-breaker)
- [ ] Highlighting em tempo real reaproveitando os `InlineRun` do AST (sem motor separado)
- [ ] Seleção múltipla (`IReadOnlyList<SelectionRange>`)
- [ ] Geometria exata do caret via `LaidOutLine.SourceStart` ↔ `TextLayout`
- [ ] Chords reais registrados (ex: `Ctrl+K, Ctrl+S`)
- [ ] Culling de páginas fora do viewport no `Render`

---

## Fase 5 — Documento acadêmico ⬜

- [ ] Cabeçalho/rodapé (populando os campos reservados desde a Fase 3)
- [ ] Numeração de página
- [ ] Extensões acadêmicas do markup: notas de rodapé, `[@cite]`, `$math$`, legendas
- [ ] Notas de rodapé no layout (segundo passe do page-breaker)
- [ ] Sumário automático (dois passes de layout)
- [ ] **Exportação PDF**, consumindo o mesmo `PaginatedDocument` — possível porque o motor
      de layout nasceu independente de tela

---

## Ideias fora de escopo (por enquanto)

Nada aqui está prometido; é estacionamento para não perder a ideia nem inflar as fases acima.

- Justificação de texto Knuth-Plass (o MVP usa greedy word-wrap)
- Árvore balanceada na piece list (só se o profiling exigir)
- Exportação DOCX
- Normas configuráveis (ABNT/APA) como presets de `PageSettings`
- Trimming/ReadyToRun no publish para reduzir os 95MB do executável
