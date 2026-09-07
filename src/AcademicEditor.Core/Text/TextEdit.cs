namespace AcademicEditor.Core.Text;

/// <summary>
/// O que mudou no documento. Descreve a alteração, não o estado — é a partir dele que o reflow
/// incremental da Fase 4 saberá quais parágrafos reparsear em vez de refazer o documento inteiro.
/// </summary>
/// <param name="Offset">Onde a alteração começou.</param>
/// <param name="RemovedLength">Quantos caracteres saíram.</param>
/// <param name="InsertedText">O que entrou no lugar.</param>
public readonly record struct TextEdit(int Offset, int RemovedLength, string InsertedText);
