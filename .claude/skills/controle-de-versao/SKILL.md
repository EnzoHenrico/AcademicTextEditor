---
name: controle-de-versao
description: Como este projeto usa git — o gate que bloqueia commits, nomes de branch, o estilo dos commits (assunto e corpo em prosa que explica o porquê), o fechamento de uma Fatia com roadmap e PR, e o merge de uma Fase. Use ao commitar, criar branch, abrir PR, ou fechar qualquer entrega. Também ao revisar uma mensagem de commit antes de gravá-la.
---

# Controle de versão no AcademicEditor

O histórico deste repositório é documentação. Quem chega daqui a um ano precisa entender
**por que** uma decisão foi tomada, não só o que mudou — o diff já diz o quê. As regras
abaixo saíram do histórico existente; ao ficar em dúvida, `git log` é a fonte.

## 1. O gate — nada entra sem `./dev.sh check` verde

Guarda de arquitetura + guarda de namespaces + build sem warnings + testes dos dois projetos.
Um hook `pre-commit` roda o gate e **bloqueia o commit** se ele falhar.

- O hook não vem no clone (`.git/hooks/` nunca vem). Depois de clonar: `./dev.sh install-hooks`.
- **Nunca use `--no-verify`.** O gate vermelho significa que o trabalho não está pronto.
- `dev.sh` é versionado — alvos de publish e flags de build são decisões do projeto, não da
  máquina. O hook não é.

## 2. Branches

`feature/<slug>` · `fix/<slug>` · `chore/<slug>` — slug em português, kebab-case.

```
feature/preset-tipografico     fix/margem-com-tolerancia
```

Trabalho de código **sempre** em branch. Vão direto para `main` apenas commits `docs:` que
não tocam em código.

## 3. Commits

### Assunto

```
tipo: o que passou a ser verdade, em português, minúsculo, sem ponto final
```

Tipos em uso, com a frequência real no histórico: `feat` · `fix` · `perf` · `docs` · `chore`
· `refactor` · `test` · `merge`. (O `CLAUDE.md` lista um subconjunto; `perf:` e `merge:` também
valem, e o histórico os usa.)

O assunto nomeia o **resultado**, nunca o arquivo mexido. Um `fix:` pode nomear o sintoma e a
causa, separados por travessão:

```
feat: quebra de página como linha atômica visível
fix: digitação travada — coalescer a repaginação em vez de cancelá-la
perf: cache de medição de texto por (texto, estilo)
```

Não: `feat: adicionar TypographyPreset`, `fix: corrigir bug no LineBreaker`.

### Corpo — é aqui que mora o valor

Prosa em parágrafos, quebrada em ~76 colunas, linha em branco entre parágrafos. **Sem listas
de arquivos alterados e sem bullets resumindo o diff.** Sem crases: é mensagem de commit, não
markdown — identificadores aparecem nus (`ITextMeasurer`, e não com acento grave).

A ordem que o histórico segue:

1. **Abra pelo problema ou pela medição que motivou a mudança**, não pelo que mudou.
   *"O line breaker assumia que espaço nunca provoca quebra: o que não cabia era anexado à
   linha assim mesmo e passava da margem, sem limite."*
2. **Diga por que este desenho e não o óbvio.** Nomeie a alternativa recusada e o motivo.
   É o parágrafo que impede a discussão de reabrir daqui a seis meses.
3. **Números em bloco indentado de 4 espaços**, alinhados, com unidade — quando houver.
4. **Feche pela consequência aceita**, pelo que ficou devendo, ou pela garantia que passou a
   existir ("duas garantias, cada uma com seu teste").
5. `Testes: N -> M.` quando a contagem mudou.

Exemplo real, na íntegra (`b74e4cd`):

```
fix: margem com tolerância de um caractere em branco

O line breaker assumia que espaço nunca provoca quebra: o que não cabia era
anexado à linha assim mesmo e passava da margem, sem limite. Invisível não é
o mesmo que ilimitado — um grupo de brancos projetava a linha arbitrariamente
para fora do papel, e o caret ia junto ao pousar depois deles.

A margem passa a valer para o branco também, com tolerância de UM caractere:
a linha leva o que cabe mais um branco, e o resto do grupo desce. Na quebra
comum, "palavra espaço palavra", o que cabe é zero — então o espaço fica
pendurado e a linha de baixo começa na palavra, sem a indentação que a margem
estrita produzia. Não era caso raro: a folga que sobra numa quebra gulosa é
uniforme entre zero e a largura da próxima palavra, então cai abaixo da
largura de um espaço em cerca de uma quebra a cada seis.

Duas garantias, cada uma com seu teste sobre os mesmos seis textos: nenhum
glifo além da margem, e nenhuma linha além dela por mais de um branco.
```

Com medição, o bloco de números (`bd600a2`):

```
    sem cache            909,7 ms
    cache frio (abrir)   308,7 ms   2,9x
    cache quente (tecla)  68,1 ms  13,4x
```

### Rodapé

Toda mensagem termina com as linhas de atribuição que o harness informa na sessão —
`Co-Authored-By:` e `Claude-Session:`. Elas mudam por sessão; use as da sessão corrente.

## 4. Fechando uma Fatia

Uma Fatia do roadmap é a unidade de entrega. Fechá-la é uma sequência, e nenhum passo é
opcional:

1. `./dev.sh check` verde.
2. **Atualizar `docs/ROADMAP.md`** — é parte da entrega, não um extra. Marcar os checkboxes,
   trocar ⬜ por ✅ no título da fatia, e escrever o bloco **"Registrado desta fatia"** com o
   que se descobriu e que não estava no plano: decisão revertida e por quê, buraco encontrado,
   número medido, ressalva honesta do que não foi verificado. Se o texto do checkbox descrevia
   um desenho que a implementação mudou, corrija o texto — o roadmap é documento vivo.
3. Commitar na branch.
4. `git push -u origin <branch>`.
5. **Abrir o PR** contra `main`, com um roteiro de **o que testar à mão** no corpo — é assim
   que a revisão acontece.
6. **Não fazer o merge.** A aprovação é do autor do projeto.

O corpo do PR segue o mesmo espírito do commit (problema → desenho → achados), mais duas
seções que o commit não tem: **o que testar à mão** (checklist acionável, começando pelo
`./dev.sh run`) e **ressalvas honestas** — o que não foi verificado e por quê.

## 5. Fechando uma Fase

A Fase entra na `main` por **commit de merge**, não por squash: as fatias individuais são o
registro de como se chegou lá.

```
merge: mouse e seleção (Fase 5)
```

O corpo resume as fatias, destaca as duas ou três medições que importam, e diz o que mudou de
lugar no roadmap. Termina com `Testes: N -> M.`

## 6. O que nunca entra num commit

- `--no-verify`, ou qualquer contorno do gate.
- Arquivo de build (`bin/`, `obj/`) — o `.gitignore` cobre, confira antes de `git add -A`.
- Rascunho de trabalho. Corpo de PR e afins vão em `.git/`, que não é versionado.
- Mudança que o assunto do commit não descreve. Se o diff faz duas coisas, são dois commits.

## 7. Checagem antes de gravar

- O assunto diz o que passou a ser verdade, e não o arquivo mexido?
- O corpo abre pelo problema, e não pelo que mudou?
- A alternativa recusada está nomeada?
- Se houve medição, os números estão no corpo?
- O gate está verde?
- Sendo fim de Fatia: roadmap atualizado com "Registrado desta fatia", e PR aberto?
