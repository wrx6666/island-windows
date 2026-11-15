using System.IO;
namespace Island;
public static class UiTestProgram
{
    [STAThread]public static void Main()
    {
        var app=new App{DiagnosticHost=true,DiagnosticRunner=Run};app.InitializeComponent();app.Run();
    }
    private static async Task Run(App app,string[] args)
    {
        int i=Array.IndexOf(args,"--output");string directory=Path.GetFullPath(i>=0?args[i+1]:"artifacts/wpf-ui-tests");
        try {
            if(args.Contains("--browser-accessibility-check"))await BrowserAccessibilityChecks.Run(app,directory);
            else if(args.Contains("--updates-check"))await UpdateChecks.Run(app,directory);
            else if(args.Contains("--collapse-check"))await CollapseChecks.Run(app,directory);
            else if(args.Contains("--glass-check"))await GlassChecks.Run(app,directory);
            else if(args.Contains("--windows-check"))await WindowsChecks.Run(app,directory);
            else if(args.Contains("--media-check"))await MediaChecks.Run(app,directory);
            else if(args.Contains("--live-check"))await LiveChecks.Run(app,directory);
            else await WpfScenarios.Run(app,directory,args.Contains("--quick"));
        }
        catch(Exception ex){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"FAILED.txt"),ex.ToString());Environment.ExitCode=1;throw;}
    }
}
