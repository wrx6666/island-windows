using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Velopack.Locators;

namespace Island;

public sealed record DistributionConfig
{
    public string Repository {get;init;}="";
    public bool IntegrationTest {get;init;}
    public static DistributionConfig Load()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"update-source.json");
        try{return File.Exists(path)?JsonSerializer.Deserialize<DistributionConfig>(File.ReadAllText(path))??new():new();}
        catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return new();}
    }
    public static string NormalizeRepository(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="github.com" || !uri.IsDefaultPort || uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="")return "";
        string path=uri.AbsolutePath.Trim('/');if(path.EndsWith(".git",StringComparison.OrdinalIgnoreCase))path=path[..^4];
        return Regex.IsMatch(path,@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$")?"https://github.com/"+path:"";
    }
}

public static class AppDistribution
{
    public static bool FirstRun {get;set;}
    public static bool Installed=>VelopackLocator.IsCurrentSet && VelopackLocator.Current.CurrentlyInstalledVersion!=null && !VelopackLocator.Current.IsPortable;
    private static string Root=>VelopackLocator.Current.RootAppDir??throw new IOException("Installation root unavailable");
    public static string DataDirectory=>DistributionConfig.Load().IntegrationTest?Path.Combine(Directory.GetParent(Root).FullName,"user-data"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Island","Data");
    // Velopack names the stable launcher after packTitle, not mainExe.
    public static string StableLauncher=>Path.Combine(Root,"Island.exe");
    private static string TestPrefix=>"Software\\Island\\DistributionTests\\"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Root)))[..16]+"\\";
    private static string Prefix=>DistributionConfig.Load().IntegrationTest?TestPrefix:"";
    private static string RunKey=>Prefix+@"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string[] HostKeys=>new[]{Prefix+@"Software\Yandex\NativeMessagingHosts\com.desktopisland.music",Prefix+@"Software\Google\Chrome\NativeMessagingHosts\com.desktopisland.music",Prefix+@"Software\Microsoft\Edge\NativeMessagingHosts\com.desktopisland.music"};
    public static void Register(bool firstInstall)
    {
        string content=AppContext.BaseDirectory;
        string host=Path.Combine(content,"Island.BrowserHost.exe");
        if(!File.Exists(host) || !File.Exists(Path.Combine(content,"browser-extension","manifest.json")))throw new IOException("Browser integration payload is incomplete");
        string manifest=Path.Combine(content,"browser-host.json");
        File.WriteAllText(manifest,JsonSerializer.Serialize(new{name="com.desktopisland.music",description="Island Music Bridge",path=host,type="stdio",allowed_origins=new[]{BrowserIdentity.Origin}}));
        foreach(string path in HostKeys){using var key=Registry.CurrentUser.CreateSubKey(path);key.SetValue("",manifest);}
        Directory.CreateDirectory(DataDirectory);
        string marker=Path.Combine(DataDirectory,"installed-once");
        if(firstInstall){
            using var run=Registry.CurrentUser.CreateSubKey(RunKey);
            // Enable once on fresh installs; migrate an enabled legacy launcher.
            // A reinstall preserves an explicitly disabled startup choice.
            if(!File.Exists(marker) || run.GetValue("Island.Desktop")!=null)run.SetValue("Island.Desktop","\""+StableLauncher+"\" --startup");
        }
        File.WriteAllText(marker,"1");
    }
    public static void Unregister()
    {
        string manifest=Path.Combine(AppContext.BaseDirectory,"browser-host.json");
        foreach(string path in HostKeys){using var key=Registry.CurrentUser.OpenSubKey(path);bool ours=string.Equals(key?.GetValue("") as string,manifest,StringComparison.OrdinalIgnoreCase);if(ours)Registry.CurrentUser.DeleteSubKey(path,false);}
        using var run=Registry.CurrentUser.OpenSubKey(RunKey,true);
        if((run?.GetValue("Island.Desktop") as string)?.StartsWith("\""+StableLauncher+"\"",StringComparison.OrdinalIgnoreCase)==true)run.DeleteValue("Island.Desktop",false);
    }
}
