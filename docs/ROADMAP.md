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
- [x] `publish` multiplataforma: `linux-x64` e `win-x64` num comando, com o formato do binário
      conferido pelos bytes mágicos — `dotnet publish` devolve 0 sem checar isso
- [x] `dev.sh` passou a ser versionado: gerar release é decisão do projeto, não da máquina

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

A fase é atacada em **fatias verticais**, não camada por camada. O motivo é o risco: o
`ITextMeasurer` é implementado com Avalonia mas chamado de dentro do `LayoutEngine`, que roda
em `Task.Run`. Se as APIs de medição do Avalonia exigirem UI thread, a decisão "layout roda em
background" cai — e numa ordem horizontal isso só apareceria com o motor inteiro já escrito
em cima dela. A Fatia 2 põe texto na tela cedo justamente para resolver isso enquanto o motor
ainda é pequeno.

### Fatia 1 — Motor de layout no Core, sem UI ✅

Markup (subconjunto mínimo):
- [x] AST: `TextStyle` (enum de peso próprio do Core), `InlineRun` (Text, SourceStart, Style),
      `ParagraphNode`, `HeadingNode`, `PageBreakNode`, `DocumentNode`
- [x] `MarkupTokenizer` + `MarkupParser` — parágrafo, `#`..`######`, quebra explícita;
      cada bloco emite **um** `InlineRun`
- [x] `MarkupParserTests`: tabela markup → AST esperado

Layout/paginação:
- [x] `PageSettings` (A4/Letter + margens, em pontos; `ContentWidthPt`/`ContentHeightPt`;
      `HeaderReservedHeightPt`/`FooterReservedHeightPt` já no tipo, 0 no MVP)
- [x] `ITextMeasurer` no Core: `MeasureWidthPt(ReadOnlySpan<char>, TextStyle)` +
      `GetLineMetrics(TextStyle)` — `Span` para o line breaker medir fatias sem alocar
- [x] `LineBreaker` greedy word-wrap sobre a sequência de runs do bloco
- [x] `PageBreaker` empilhando linhas até `ContentHeightPt`, respeitando `PageBreakNode`
- [x] `LayoutEngine` produzindo `PaginatedDocument` imutável
- [x] Model imutável: `LaidOutRun`, `LaidOutLine` (com `SourceStart`/`SourceLength`),
      `PageLayout`, `PaginatedDocument`
- [x] `LineBreakerTests`/`PageBreakerTests` com `FakeTextMeasurer` determinístico
- [x] Casos de borda: palavra mais larga que a página, parágrafo vazio, quebra explícita,
      documento vazio produzindo 1 página (não 0)

### Fatia 2 — Página A4 na tela ✅

- [x] **Spike:** medição do Avalonia dentro de um `Task.Run`. **O risco não se materializou:**
      `TextLayout`, `FormattedText` e a soma de avanços de glifo funcionam fora da UI thread e
      devolvem exatamente o mesmo valor que nela, inclusive com 8 threads medindo em paralelo.
      A decisão "layout roda em background" fica de pé como está
- [x] `AvaloniaTextMeasurer` em `Rendering/`, medindo com o **mesmo `TextLayout` que desenha**.
      Avanços de glifo seriam mais baratos (180ms/20k medições e 2,7MB contra 251ms e 50,5MB),
      mas ignoram o shaping: mediram o mesmo texto 0,76% mais largo que o desenhado, meio
      caractere de deriva por linha de A4 até vazar a margem. `FormattedText` foi descartado
      por ser 4,6× mais lento que `TextLayout` sem vantagem nenhuma
- [x] Altura e baseline dependem só do estilo, nunca do texto — verificado — e ficam num
      `ConcurrentDictionary` por estilo
- [x] `PageRenderer` desenhando as folhas com espaçamento visual entre elas
- [x] Conversão pt → DIP numa constante única do `PageRenderer`
- [x] `PageSurface : Control` dentro de `ScrollViewer`; `Render` só desenha; `MeasureOverride`
      devolve a altura total da pilha
- [x] `EditorViewModel` (MVVM leve, sem framework) guardando o `PaginatedDocument` corrente
- [x] Critério: `./dev.sh run` mostra folha A4 com margens e texto fixo já paginado

Duas coisas que a fatia expôs e ficam registradas:

- **Falta espaçamento entre blocos.** As linhas saem com o entrelinhamento natural da fonte e
  nada separa um parágrafo do seguinte, então um heading encosta no corpo. Não é bug do motor:
  `BlockNode` simplesmente não tem `SpaceBeforePt`/`SpaceAfterPt`, e entrelinhamento de 1,5 é
  parte do preset de norma da Fase 5. Entra junto com ele
- **O bug que só o app real pegou:** dentro de um `ScrollViewer` o `availableSize` do
  `MeasureOverride` chega infinito, e devolvê-lo aborta o passo de layout. Nenhum teste do Core
  pegaria isso — é o primeiro argumento concreto a favor do `tests/AcademicEditor.App.Tests/`
  previsto para a Fase 4

### Fatia 3 — Buffer editável e digitação

- [ ] `Piece` (`readonly struct`: Source, Start, Length) e `PieceTable` com `Insert`/`Delete`
- [ ] `TextBufferSnapshot` imutável, consumido pelo layout sem travar a digitação
- [ ] `EditorDocument` orquestrando buffer + evento de mudança (`TextEdit`)
- [ ] `PieceTableTests`: insert início/meio/fim, delete dentro/cruzando/consumindo pieces
- [ ] Teste diferencial de stress: N edições aleatórias com seed fixo vs. `StringBuilder`
- [ ] Texto digitado vindo do evento `TextInput` (não `KeyDown.Key`), por IME e layouts
      internacionais
- [ ] Recompute em background (`Task.Run`) + publicação via `Dispatcher.UIThread.Post` +
      debounce; layout obsoleto cancelado e número de geração impedindo publicação fora de ordem

### Fatia 4 — Caret e navegação (no Core)

- [ ] `Caret` em `State/`: `Offset` no buffer + `DesiredColumnPt` (coluna alvo que sobrevive
      a ↑/↓ passando por linhas curtas)
- [ ] `CaretNavigator` — funções puras `(offset, PaginatedDocument) → offset` para setas,
      Home/End, PageUp/PageDown, usando `LaidOutLine.SourceStart`/`SourceLength`
- [ ] `CaretNavigatorTests`: bordas do documento, coluna alvo preservada, Home/End em linha
      com wrap (limite visual, não do parágrafo), navegação cruzando fronteira de página
- [ ] Caret desenhado pelo `PageRenderer`; `PageSurface` só traduz tecla em chamada ao Core

### Fatia 5 — Undo/redo, arquivo e atalhos

- [ ] `UndoRedoStack` guardando delta estrutural da piece list, não cópias de texto
- [ ] Testes de undo/redo encadeado e de redo invalidado por edição nova
- [ ] `IDocumentStorage` + `FileDocumentStorage` async
- [ ] Save atômico: `.tmp` no mesmo diretório (mesmo volume) e `File.Move(..., overwrite: true)`
- [ ] Detecção de BOM/encoding e normalização CRLF/LF
- [ ] `FileDocumentStorageTests`: falha no meio da escrita não corrompe o original; round-trip
- [ ] `CommandId`, `ShortcutScope`, `ChordSequence`, `KeyBindingRegistry` (já suportando N passos)
- [ ] `ShortcutDispatcher` no `PreviewKeyDown` (tunneling) + `FocusScopeTracker`
- [ ] Bindings: `Ctrl+S`, `Ctrl+O`, `Ctrl+Z`/`Ctrl+Y`
- [ ] Diálogos nativos via `IStorageProvider`

### Fatia 6 — Fechamento da fase
- [ ] Teste manual end-to-end via `./dev.sh run`
- [ ] Documento longo (~300 páginas) para medir latência de repaginação e decidir se o
      reflow incremental precisa ser antecipado da Fase 4. Registrar o número medido aqui

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
