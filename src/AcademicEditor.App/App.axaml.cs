using AcademicEditor.App.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AcademicEditor.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // `AcademicEditor tese.md` abre o arquivo direto. O primeiro argumento que não começa
            // com '-' é o caminho; as opções são dos modos de diagnóstico, tratados no Program.
            var path = desktop.Args?.FirstOrDefault(argument => !argument.StartsWith('-'));

            desktop.MainWindow = new MainWindow(path);
        }

        base.OnFrameworkInitializationCompleted();
    }
}