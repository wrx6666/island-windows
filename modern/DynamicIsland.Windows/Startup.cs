using Microsoft.Win32;
namespace Island;
public static class Startup
{
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run", Name="Island.Desktop";
    public static string Executable { get; set; } = Environment.ProcessPath;
    private static string Command => "\""+Executable+"\" --startup";
    public static bool Enabled { get { try {using var key=Registry.CurrentUser.OpenSubKey(RunKey); var value=key?.GetValue(Name) as string; return string.Equals(value,Command,StringComparison.OrdinalIgnoreCase) || string.Equals(value,"\""+Executable+"\"",StringComparison.OrdinalIgnoreCase);}catch(Exception ex)when(ex is System.Security.SecurityException or UnauthorizedAccessException){return false;} } }
    public static void Set(bool enabled) { using var key=Registry.CurrentUser.CreateSubKey(RunKey); if(enabled) key.SetValue(Name,Command); else key.DeleteValue(Name,false); }
}
