# AcademicEditor

Editor de texto desktop multiplataforma (Windows/Linux/macOS) em C# + Avalonia, voltado
para redação de trabalhos acadêmicos (artigos, teses). O autor escreve em Markdown com
extensões acadêmicas e vê o documento **paginado em tempo real**, como ficará impresso.

Ver `context.md` para o contexto original do produto e `ROADMAP.md` para as fases —
o roadmap é documento vivo: atualize os checkboxes conforme o trabalho avança.

## Regra permanente: nada é aceito sem build verde

**Nenhum desenvolvimento é considerado concluído sem `./dev.sh check` passando** —
guarda de arquitetura + build sem warnings + testes. Um hook `pre-commit` bloqueia
commits que violem isso. Se o gate falhar, o trabalho não está pronto: corrija antes
de reportar conclusão.

## Comandos

`dev.sh` é local (não versionado, listado em `.git/info/exclude`). Após clonar,
rode `./dev.sh install-hooks` para reinstalar o hook.

```bash
./dev.sh check          # o gate: arquitetura + build + testes
./dev.sh build          # compila a solução (Debug)
./dev.sh test           # testes do Core, sem Avalonia
./dev.sh run            # compila e abre o app
./dev.sh publish        # executável self-contained em artifacts/linux-x64
./dev.sh clean
```

## Arquitetura

- `src/AcademicEditor.Core` — buffer de texto, parser de markup, motor de layout/paginação, I/O.
- `src/AcademicEditor.App` — Avalonia: renderização, input, janela.
- `tests/AcademicEditor.Core.Tests` — xUnit, referencia só o Core.

**Regra inegociável: `AcademicEditor.Core` nunca referencia `Avalonia.*`.**
Isso garante testes unitários sem subsistema gráfico e permite reusar o motor de layout
num exportador PDF/CLI depois. A única costura Core↔App é a interface `ITextMeasurer`
(definida no Core, implementada no App via `Avalonia.Media.TextLayout`).

### Decisões já tomadas (não reabrir sem motivo)

- **AvaloniaEdit foi descartado**: é um code-editor de rolagem contínua, sem noção de página/margem.
- **Buffer: Piece Table** — `Piece` é `readonly struct`; digitação só faz append, undo/redo é
  delta estrutural da piece list, sem copiar texto.
- **Unidade interna do layout: pontos (1/72")**. Conversão para DIP (`pt * 96/72`) acontece
  **somente** na camada de renderização — é o que mantém o motor independente de tela.
- **Layout roda em background** (`Task.Run`) e publica via `Dispatcher.UIThread.Post`.
  `PaginatedDocument` e filhos são imutáveis: troca de referência atômica, sem locks.
- **`Render(DrawingContext)` só desenha** — nunca faz I/O nem recalcula layout.
- **Texto digitado vem do evento `TextInput`**, não de `KeyDown.Key` (IME e layouts internacionais).
- **Save é atômico**: escreve em `.tmp` no mesmo diretório e faz `File.Move(..., overwrite: true)`.

## Estilo de código

- Simples e legível acima de tudo; evitar abstrações desnecessárias.
- **Early return**; evitar aninhamento profundo.
- Explicar comportamento de memória/GC e roteamento de eventos quando relevante — sem "caixas pretas".

## Git

- Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `chore:`, `docs:`.
- Branches: `feature/<slug>`, `fix/<slug>`, `chore/<slug>`.
