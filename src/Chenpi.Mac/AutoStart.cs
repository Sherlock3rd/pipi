using System.Xml.Linq;

namespace Chenpi;
internal static class AutoStart
{
    private static string FileName=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","LaunchAgents","com.chenpi.desktop.plist");
    public static bool Enabled=>File.Exists(FileName);
    public static void Set(bool enabled)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        if(!enabled){if(File.Exists(FileName))File.Delete(FileName);return;}
        string host=Environment.ProcessPath??throw new InvalidOperationException("找不到应用路径");
        if(!host.Contains(".app/Contents/MacOS/",StringComparison.Ordinal))throw new InvalidOperationException("请先把陈皮.app 移至应用程序文件夹，再开启自启。");
        var doc=new XDocument(new XDeclaration("1.0","UTF-8",null),new XDocumentType("plist","-//Apple//DTD PLIST 1.0//EN","http://www.apple.com/DTDs/PropertyList-1.0.dtd",null),
            new XElement("plist",new XAttribute("version","1.0"),new XElement("dict",
                new XElement("key","Label"),new XElement("string","com.chenpi.desktop"),
                new XElement("key","ProgramArguments"),new XElement("array",new XElement("string",host),new XElement("string","--autostart")),
                new XElement("key","RunAtLoad"),new XElement("true"),new XElement("key","LimitLoadToSessionType"),new XElement("string","Aqua"))));
        Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);doc.Save(FileName+".tmp");File.Move(FileName+".tmp",FileName,true);
    }
}
