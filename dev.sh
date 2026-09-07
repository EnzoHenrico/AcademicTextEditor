#!/usr/bin/env bash
#
# dev.sh — script de desenvolvimento e release do AcademicEditor.
#
# Versionado: a lista de alvos do publish e as flags de build são decisões do projeto, não da
# máquina. O que continua local é o hook — .git/hooks/ nunca vem no clone, então quem clonar
# roda ./dev.sh install-hooks uma vez.
#
# Qualquer falha retorna exit code != 0, que é o que permite ao hook pre-commit bloquear.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

readonly SLN="AcademicEditor.slnx"
readonly CORE="src/AcademicEditor.Core/AcademicEditor.Core.csproj"
readonly APP="src/AcademicEditor.App/AcademicEditor.App.csproj"
readonly TESTS="tests/AcademicEditor.Core.Tests/AcademicEditor.Core.Tests.csproj"
# Alvos do publish. Só estes dois são verificáveis daqui: o Linux roda direto e o Windows roda
# pelo WSL. macOS compilaria, mas sem bundle .app nem assinatura o Gatekeeper recusa o binário,
# e não há como confirmar isso desta máquina — está no estacionamento de ideias do roadmap.
readonly RIDS=(linux-x64 win-x64)

if [[ -t 1 ]]; then
    readonly C_RESET=$'\033[0m' C_RED=$'\033[31m' C_GREEN=$'\033[32m' C_BLUE=$'\033[34m' C_BOLD=$'\033[1m'
else
    readonly C_RESET='' C_RED='' C_GREEN='' C_BLUE='' C_BOLD=''
fi

step()  { printf '%s==>%s %s%s%s\n' "$C_BLUE" "$C_RESET" "$C_BOLD" "$1" "$C_RESET"; }
ok()    { printf '%s  ok%s %s\n' "$C_GREEN" "$C_RESET" "$1"; }
fail()  { printf '%s FALHA%s %s\n' "$C_RED" "$C_RESET" "$1" >&2; exit 1; }

# O Core é a fundação testável e reutilizável do projeto: se ele passar a depender do
# Avalonia, perdemos os testes sem subsistema gráfico e o reuso num exportador PDF/CLI.
# Por isso a regra é verificada, não só documentada.
guard_static() {
    step "Guarda de arquitetura (referências diretas)"

    if grep -qiE '<PackageReference[^>]*Avalonia' "$CORE"; then
        fail "$CORE tem PackageReference do Avalonia. O Core não pode depender de UI."
    fi
    if grep -qiE '<ProjectReference[^>]*AcademicEditor\.App' "$CORE"; then
        fail "$CORE referencia o projeto App. A dependência é App -> Core, nunca o contrário."
    fi

    ok "Core sem referência direta a Avalonia"
}

guard_transitive() {
    step "Guarda de arquitetura (dependências transitivas)"

    local packages
    packages="$(dotnet list "$CORE" package --include-transitive 2>&1)"

    if grep -qi 'avalonia' <<<"$packages"; then
        printf '%s\n' "$packages" >&2
        fail "Avalonia chegou ao Core por dependência transitiva."
    fi

    ok "Nenhum vazamento transitivo de Avalonia"
}

# O padrão de diretórios (docs/Directories.md) só vale alguma coisa se for verificado.
# O IDE0130 do Roslyn cobre isso dentro da IDE, mas não é reportado em build de linha
# de comando nem com EnforceCodeStyleInBuild — daí a checagem viver aqui.
guard_namespaces() {
    step "Guarda de namespaces (pasta = namespace)"

    local violations=0
    local proj root_ns file rel dir expected declared

    for proj in "src/AcademicEditor.Core" "src/AcademicEditor.App" "tests/AcademicEditor.Core.Tests"; do
        root_ns="$(basename "$proj")"

        while IFS= read -r -d '' file; do
            rel="${file#"$proj/"}"
            dir="$(dirname "$rel")"

            if [[ "$dir" == "." ]]; then
                expected="$root_ns"
            else
                expected="$root_ns.${dir//\//.}"
            fi

            declared="$(grep -m1 -oP '^\s*namespace\s+\K[A-Za-z0-9_.]+' "$file" || true)"
            [[ -z "$declared" ]] && continue

            if [[ "$declared" != "$expected" ]]; then
                printf '  %s\n     declarado: %s\n     esperado:  %s\n' "$file" "$declared" "$expected" >&2
                violations=$((violations + 1))
            fi
        done < <(find "$proj" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0)
    done

    if (( violations > 0 )); then
        fail "$violations arquivo(s) com namespace fora do padrão de diretórios."
    fi

    ok "Namespaces espelham as pastas"
}

do_build() {
    step "Build ($SLN)"
    dotnet build "$SLN" || fail "Build falhou."
    ok "Build limpo (TreatWarningsAsErrors: zero warnings)"
}

# `dotnet test` retorna 0 quando não descobre nenhum teste — um gate que passa sem
# rodar nada é pior que gate nenhum, porque dá falsa confiança. Por isso a saída é
# inspecionada, não só o exit code.
do_test() {
    step "Testes do Core"

    local output
    if ! output="$(dotnet test "$TESTS" 2>&1)"; then
        printf '%s\n' "$output" >&2
        fail "Testes falharam."
    fi

    printf '%s\n' "$output"

    if grep -q 'No test is available' <<<"$output"; then
        fail "Nenhum teste foi descoberto — a descoberta de testes está quebrada."
    fi

    ok "Testes verdes"
}

do_run() {
    step "Executando o app"
    if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
        fail "Sem DISPLAY/WAYLAND_DISPLAY — não há servidor gráfico para abrir a janela."
    fi
    dotnet run --project "$APP"
}

# O executável final tem nome diferente por plataforma. A mensagem de sucesso precisa apontar
# para o arquivo que realmente existe, senão manda procurar o que não está lá.
artifact_name() {
    if [[ "$1" == win-* ]]; then
        printf 'AcademicEditor.App.exe'
    else
        printf 'AcademicEditor.App'
    fi
}

# `dotnet publish` devolve 0 sem conferir se o binário saiu no formato do alvo pedido. Um
# cross-publish silenciosamente errado passaria no exit code e só apareceria na máquina de quem
# fosse rodar. Os bytes mágicos respondem isso sem depender de ferramenta externa — MZ para PE,
# \x7fELF para ELF — e o file(1), quando existe, só enfeita a mensagem.
verify_artifact() {
    local rid="$1" path="$2"

    [[ -f "$path" ]] || fail "$path não foi gerado."

    local size
    size="$(stat -c %s "$path")"
    if (( size < 20 * 1024 * 1024 )); then
        fail "$path tem $size bytes — um self-contained do Avalonia não é tão pequeno."
    fi

    local magic expected label
    magic="$(od -A n -t x1 -N 4 "$path" | tr -d ' \n')"

    case "$rid" in
        win-*)   expected='4d5a'     ; label='PE (Windows)'   ;;
        linux-*) expected='7f454c46' ; label='ELF (Linux)'    ;;
        osx-*)   expected='cffaedfe' ; label='Mach-O (macOS)' ;;
        *)       ok "formato de $rid não é verificado" ; return ;;
    esac

    if [[ "$magic" != "$expected"* ]]; then
        fail "$path não é $label — bytes iniciais: $magic."
    fi

    local description="$label"
    if command -v file >/dev/null; then
        description="$(file -b "$path")"
    fi

    ok "$(numfmt --to=iec --suffix=B "$size") — $description"
}

# Sem trimming: o XAML do Avalonia resolve tipos por reflexão e o trimmer quebra a app
# em runtime, com erros difíceis de diagnosticar. IncludeNativeLibrariesForSelfExtract
# embute SkiaSharp/HarfBuzz no executável em vez de deixar .so soltos ao lado.
publish_one() {
    local rid="$1"
    local out="artifacts/$rid"
    local path
    path="$out/$(artifact_name "$rid")"

    step "Publish self-contained ($rid)"
    dotnet publish "$APP" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$out" || fail "Publish de $rid falhou."

    verify_artifact "$rid" "$path"
    printf '       %s\n' "$path"

    # No WSL, o caminho UNC é o que se cola no Explorer para rodar o .exe do lado Windows.
    if [[ "$rid" == win-* ]] && command -v wslpath >/dev/null; then
        local unc
        if unc="$(wslpath -w "$PWD/$path" 2>/dev/null)"; then
            printf '       %s\n' "$unc"
        fi
    fi
}

do_publish() {
    local rids=("$@")

    if (( ${#rids[@]} == 0 )); then
        rids=("${RIDS[@]}")
    fi

    local rid
    for rid in "${rids[@]}"; do
        publish_one "$rid"
    done
}

do_clean() {
    step "Limpando"
    find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
    rm -rf artifacts
    ok "bin/, obj/ e artifacts/ removidos"
}

do_check() {
    guard_static
    guard_namespaces
    do_build
    guard_transitive
    do_test
    printf '\n%s tudo verde%s\n' "$C_GREEN$C_BOLD" "$C_RESET"
}

install_hooks() {
    step "Instalando hook pre-commit"

    local hook=".git/hooks/pre-commit"
    cat > "$hook" <<'HOOK'
#!/usr/bin/env bash
# Gate do projeto: nenhum commit entra sem build e testes verdes.
# Instalado por ./dev.sh install-hooks — reinstale após clonar o repositório.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

if [[ ! -x ./dev.sh ]]; then
    echo "pre-commit: ./dev.sh não encontrado ou sem permissão de execução." >&2
    exit 1
fi

echo "pre-commit: rodando ./dev.sh check"
./dev.sh check
HOOK
    chmod +x "$hook"

    ok "$hook instalado"
}

usage() {
    cat <<EOF
${C_BOLD}dev.sh${C_RESET} — desenvolvimento e release do AcademicEditor

  ${C_BOLD}check${C_RESET}          guarda de arquitetura + build + testes (padrão; é o que o pre-commit roda)
  ${C_BOLD}build${C_RESET}          compila a solução em Debug
  ${C_BOLD}test${C_RESET}           roda os testes do Core
  ${C_BOLD}run${C_RESET}            compila e abre o app
  ${C_BOLD}publish${C_RESET} [rid]  executável self-contained em artifacts/<rid>
                 sem argumento, gera todos: ${RIDS[*]}
  ${C_BOLD}clean${C_RESET}          remove bin/, obj/ e artifacts/
  ${C_BOLD}install-hooks${C_RESET}  (re)instala o hook pre-commit
EOF
}

case "${1:-check}" in
    check)         do_check ;;
    build)         do_build ;;
    test)          do_test ;;
    run)           do_run ;;
    publish)       shift; do_publish "$@" ;;
    clean)         do_clean ;;
    install-hooks) install_hooks ;;
    -h|--help|help) usage ;;
    *)             printf '%sComando desconhecido: %s%s\n\n' "$C_RED" "$1" "$C_RESET" >&2; usage >&2; exit 1 ;;
esac
