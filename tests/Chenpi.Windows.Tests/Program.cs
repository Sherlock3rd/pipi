using System;
using System.Threading;
using System.Windows.Forms;
using Chenpi;

internal static class Program
{
    private static int checks;
    private static void Check(bool result,string label)
    {if(!result)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}

    // A temporary, non-activating native fixture. Never sends input to other apps,
    // changes desktop settings, or reads/writes the user's pet save.
    private sealed class Fixture : Form {protected override bool ShowWithoutActivation=>true;}
    private static void Settle()
    {for(int i=0;i<15;i++){Application.DoEvents();Thread.Sleep(20);}}

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using var window=new Fixture {Text="Chenpi native window regression",ShowInTaskbar=false,
            StartPosition=FormStartPosition.Manual,Bounds=new(100,100,600,400)};
        window.Show();Settle();
        var monitor=Screen.FromHandle(window.Handle).Bounds;
        var screen=new PixelBounds(monitor.Left,monitor.Top,monitor.Right,monitor.Bottom);
        FullscreenCandidate Read()=>Native.ReadFullscreenCandidate(window.Handle)??throw new Exception("fixture not readable");
        // Keep all native observations, changing only identity to exercise the foreign-app policy.
        bool WouldHide()=>FullscreenPolicy.Evaluate(Read() with {OwnProcess=false},screen).Hide;
        Check(Read().Visible&&!Read().Minimized&&!Read().Cloaked,"native visible normal window flags");
        Check(Read().OwnProcess&&!Native.CheckFullScreen(window.Handle,window.Handle).Hide,"own-process windows are excluded by production entry point");
        Check(!WouldHide(),"native ordinary window does not hide pet");
        window.WindowState=FormWindowState.Maximized;Settle();
        Check(Read().Maximized&&Read().HasCaption&&!WouldHide(),"native captioned maximized window stays visible");
        window.WindowState=FormWindowState.Normal;
        window.FormBorderStyle=FormBorderStyle.None;window.Bounds=monitor;Settle();
        Check(!Read().HasCaption&&Read().Bounds.Covers(screen)&&WouldHide(),"native borderless monitor-covering window hides pet");
        window.Bounds=new(monitor.Left+100,monitor.Top+100,600,400);Settle();
        Check(!WouldHide(),"native fullscreen exit restores pet decision");
        foreach(var other in Screen.AllScreens)
        {
            if(other.Bounds==monitor)continue;
            window.Bounds=other.Bounds;Settle();
            var otherScreen=new PixelBounds(other.Bounds.Left,other.Bounds.Top,other.Bounds.Right,other.Bounds.Bottom);
            Check(!WouldHide(),"native fullscreen on other monitor leaves original pet screen visible");
            Check(FullscreenPolicy.Evaluate(Read() with {OwnProcess=false},otherScreen).Hide,"native other-monitor fullscreen uses matching physical screen coordinates");
        }
        window.Bounds=monitor;Settle();window.Hide();Settle();
        Check(!Read().Visible&&!WouldHide(),"native hidden fullscreen window restores pet decision");
        window.Show();Settle();window.WindowState=FormWindowState.Minimized;Settle();
        Check(Read().Minimized&&!WouldHide(),"native minimized fullscreen window restores pet decision");
        Check(Native.ReadFullscreenCandidate(IntPtr.Zero) is null,"null foreground is safely ignored");
        var handle=window.Handle;window.Close();Settle();
        Check(Native.ReadFullscreenCandidate(handle) is null,"closed native handle is safely ignored");
        Console.WriteLine($"{checks} Windows checks passed. Screen count: {Screen.AllScreens.Length}.");
    }
}
