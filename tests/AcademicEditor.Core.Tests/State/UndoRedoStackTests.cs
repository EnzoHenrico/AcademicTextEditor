using AcademicEditor.Core.State;
using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.State;

public sealed class UndoRedoStackTests
{
    [Fact]
    public void Sem_historico_nao_ha_o_que_desfazer()
    {
        var (document, undo) = New("abc");

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
        Assert.Null(undo.Undo());
        Assert.Equal("abc", Text(document));
    }

    [Fact]
    public void Undo_e_redo_devolvem_o_texto_e_o_caret()
    {
        var (document, undo) = New("abc");

        undo.Record(document.Insert(1, "XY"), EditKind.Other, caretBefore: 1, caretAfter: 3);
        Assert.Equal("aXYbc", Text(document));

        Assert.Equal(1, undo.Undo());
        Assert.Equal("abc", Text(document));

        Assert.Equal(3, undo.Redo());
        Assert.Equal("aXYbc", Text(document));
    }

    // A razão de existir do agrupamento: desfazer letra por letra é insuportável.
    [Fact]
    public void Digitacao_seguida_vira_um_grupo_so()
    {
        var (document, undo) = New("");

        for (var i = 0; i < 5; i++)
        {
            undo.Record(document.Insert(i, "a"), EditKind.Typing, caretBefore: i, caretAfter: i + 1);
        }

        Assert.Equal("aaaaa", Text(document));
        Assert.Equal(0, undo.Undo());
        Assert.Equal("", Text(document));
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Mover_o_caret_fecha_o_grupo()
    {
        var (document, undo) = New("");

        undo.Record(document.Insert(0, "a"), EditKind.Typing, 0, 1);
        undo.Break();
        undo.Record(document.Insert(1, "b"), EditKind.Typing, 1, 2);

        Assert.Equal(1, undo.Undo());
        Assert.Equal("a", Text(document));

        Assert.Equal(0, undo.Undo());
        Assert.Equal("", Text(document));
    }

    // Digitar num ponto e depois noutro não pode virar um grupo, mesmo sem Break: o segundo trecho
    // não continua de onde o primeiro parou.
    [Fact]
    public void Digitacao_em_outro_ponto_abre_grupo_novo()
    {
        var (document, undo) = New("abc");

        undo.Record(document.Insert(3, "X"), EditKind.Typing, 3, 4);
        undo.Record(document.Insert(0, "Y"), EditKind.Typing, 0, 1);

        Assert.Equal("YabcX", Text(document));
        Assert.Equal(0, undo.Undo());
        Assert.Equal("abcX", Text(document));
    }

    [Fact]
    public void Enter_nao_se_junta_a_digitacao()
    {
        var (document, undo) = New("");

        undo.Record(document.Insert(0, "a"), EditKind.Typing, 0, 1);
        undo.Record(document.Insert(1, "\n"), EditKind.Other, 1, 2);
        undo.Record(document.Insert(2, "b"), EditKind.Typing, 2, 3);

        Assert.Equal("a\nb", Text(document));
        undo.Undo();
        Assert.Equal("a\n", Text(document));
        undo.Undo();
        Assert.Equal("a", Text(document));
    }

    // Editar depois de desfazer abandona o que havia sido desfeito: aquela história descreve peças
    // que não existem mais nesta lista, e refazê-la corromperia o buffer.
    [Fact]
    public void Edicao_nova_invalida_o_redo()
    {
        var (document, undo) = New("abc");

        undo.Record(document.Insert(3, "X"), EditKind.Other, 3, 4);
        undo.Undo();

        Assert.True(undo.CanRedo);

        undo.Record(document.Insert(0, "Z"), EditKind.Other, 0, 1);

        Assert.False(undo.CanRedo);
        Assert.Equal("Zabc", Text(document));
    }

    [Fact]
    public void Undo_encadeado_ate_o_documento_original()
    {
        var (document, undo) = New("inicial");
        var states = new List<string> { "inicial" };

        var edits = new (int Offset, string Text)[] { (0, "A"), (4, "BB"), (2, "C"), (9, "DDD") };

        foreach (var (offset, text) in edits)
        {
            undo.Record(document.Insert(offset, text), EditKind.Other, offset, offset + text.Length);
            states.Add(Text(document));
        }

        for (var i = states.Count - 1; i > 0; i--)
        {
            Assert.Equal(states[i], Text(document));
            undo.Undo();
        }

        Assert.Equal("inicial", Text(document));

        foreach (var state in states.Skip(1))
        {
            undo.Redo();
            Assert.Equal(state, Text(document));
        }
    }

    // O teste que de fato prova o delta estrutural. Milhares de edições e desfazeres aleatórios
    // contra um modelo ingênuo: se um índice de peça ficar obsoleto, o texto diverge aqui.
    [Theory]
    [InlineData(7)]
    [InlineData(2024)]
    [InlineData(int.MaxValue)]
    public void Concorda_com_o_modelo_ingenuo_apos_milhares_de_operacoes(int seed)
    {
        var random = new Random(seed);
        var (document, undo) = New("começo");

        // O modelo: a pilha de estados que o undo deveria reproduzir, em texto puro.
        var done = new List<string>();
        var undone = new List<string>();
        var current = "começo";

        for (var step = 0; step < 2_000; step++)
        {
            switch (random.Next(10))
            {
                case <= 4:
                {
                    var offset = random.Next(current.Length + 1);
                    var text = new string((char)('a' + random.Next(26)), random.Next(1, 4));

                    undo.Record(document.Insert(offset, text), EditKind.Other, offset, offset + text.Length);
                    done.Add(current);
                    undone.Clear();
                    current = current[..offset] + text + current[offset..];
                    break;
                }

                case <= 6 when current.Length > 0:
                {
                    var offset = random.Next(current.Length);
                    var length = random.Next(1, Math.Min(5, current.Length - offset) + 1);

                    undo.Record(document.Delete(offset, length), EditKind.Other, offset + length, offset);
                    done.Add(current);
                    undone.Clear();
                    current = current[..offset] + current[(offset + length)..];
                    break;
                }

                // Substituir um trecho: duas edições num grupo só, que é o que digitar ou colar
                // sobre uma seleção faz. É a operação que o RecordCompound existe para registrar,
                // e a que depende de o Undo reverter na ordem inversa.
                case 7 when current.Length > 0:
                {
                    var offset = random.Next(current.Length);
                    var length = random.Next(1, Math.Min(5, current.Length - offset) + 1);
                    var text = new string((char)('A' + random.Next(26)), random.Next(1, 4));

                    var removal = document.Delete(offset, length);
                    var insertion = document.Insert(offset, text);

                    undo.RecordCompound([removal, insertion], offset + length, offset + text.Length);
                    done.Add(current);
                    undone.Clear();
                    current = current[..offset] + text + current[(offset + length)..];
                    break;
                }

                case <= 8 when done.Count > 0:
                {
                    undo.Undo();
                    undone.Add(current);
                    current = done[^1];
                    done.RemoveAt(done.Count - 1);
                    break;
                }

                case 9 when undone.Count > 0:
                {
                    undo.Redo();
                    done.Add(current);
                    current = undone[^1];
                    undone.RemoveAt(undone.Count - 1);
                    break;
                }
            }

            Assert.Equal(current, Text(document));
        }
    }

    // Substituir a seleção são duas edições — apagar e inserir —, e as duas são EditKind.Other.
    // Pelo caminho do Record virariam dois grupos, e um Ctrl+Z devolveria o texto digitado sem
    // devolver o que foi apagado.
    [Fact]
    public void Substituir_um_trecho_e_um_undo_so()
    {
        var (document, undo) = New("abcdef");

        var removal = document.Delete(1, 3);
        var insertion = document.Insert(1, "XY");

        Assert.Equal("aXYef", Text(document));

        // O trecho 1..4 estava selecionado com o caret no fim dele; depois da substituição o
        // caret fica no fim do que entrou.
        undo.RecordCompound([removal, insertion], caretBefore: 4, caretAfter: 3);

        Assert.Equal(4, undo.Undo());
        Assert.Equal("abcdef", Text(document));

        Assert.Equal(3, undo.Redo());
        Assert.Equal("aXYef", Text(document));
    }

    // Nada se junta a uma substituição: a tecla seguinte abre grupo próprio, e desfazer devolve a
    // substituição inteira em vez de levar junto o que veio depois.
    [Fact]
    public void Substituicao_fecha_o_grupo()
    {
        var (document, undo) = New("abc");

        undo.RecordCompound([document.Delete(0, 3), document.Insert(0, "X")], 3, 1);
        undo.Record(document.Insert(1, "y"), EditKind.Typing, 1, 2);

        Assert.Equal("Xy", Text(document));

        undo.Undo();
        Assert.Equal("X", Text(document));

        undo.Undo();
        Assert.Equal("abc", Text(document));
    }

    [Fact]
    public void Substituicao_vazia_nao_entra_no_historico()
    {
        var (document, undo) = New("abc");

        undo.RecordCompound([PieceEdit.Empty], 0, 0);

        Assert.False(undo.CanUndo);
    }

    private static (EditorDocument Document, UndoRedoStack Undo) New(string text)
    {
        var document = new EditorDocument(text);

        return (document, new UndoRedoStack(document));
    }

    private static string Text(EditorDocument document) => document.CreateSnapshot().GetText();
}
