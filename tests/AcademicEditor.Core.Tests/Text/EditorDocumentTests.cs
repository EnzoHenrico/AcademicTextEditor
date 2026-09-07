using AcademicEditor.Core.Text;

namespace AcademicEditor.Core.Tests.Text;

public sealed class EditorDocumentTests
{
    [Fact]
    public void Insert_anuncia_o_que_entrou()
    {
        var document = new EditorDocument("abc");
        var edits = new List<TextEdit>();
        document.Changed += (_, edit) => edits.Add(edit);

        document.Insert(1, "XY");

        var edit = Assert.Single(edits);
        Assert.Equal(new TextEdit(1, RemovedLength: 0, "XY"), edit);
        Assert.Equal("aXYbc", document.CreateSnapshot().GetText());
    }

    [Fact]
    public void Delete_anuncia_o_que_saiu()
    {
        var document = new EditorDocument("abcdef");
        var edits = new List<TextEdit>();
        document.Changed += (_, edit) => edits.Add(edit);

        document.Delete(2, 3);

        Assert.Equal(new TextEdit(2, RemovedLength: 3, ""), Assert.Single(edits));
        Assert.Equal("abf", document.CreateSnapshot().GetText());
    }

    // Um evento por alteração que não alterou nada faria o layout recalcular à toa a cada tecla
    // morta — e, com debounce, ainda atrasaria a repaginação real.
    [Fact]
    public void Alteracao_vazia_nao_anuncia_nada()
    {
        var document = new EditorDocument("abc");
        var announced = 0;
        document.Changed += (_, _) => announced++;

        document.Insert(0, "");
        document.Delete(0, 0);

        Assert.Equal(0, announced);
        Assert.Equal(3, document.Length);
    }
}
