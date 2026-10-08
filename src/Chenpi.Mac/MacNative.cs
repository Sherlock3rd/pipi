using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Chenpi;

internal static class MacNative
{
    private const string ObjC="/usr/lib/libobjc.A.dylib",CG="/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics",CF="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] internal struct XY {public double X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct CGRect {public XY Origin,Size;}
    [StructLayout(LayoutKind.Sequential)] private struct Timebase {public uint Numer,Denom;}
    [DllImport(ObjC)] internal static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] internal static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern IntPtr Send(IntPtr obj,IntPtr sel);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern IntPtr SendPtr(IntPtr obj,IntPtr sel,IntPtr arg);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern void SendLong(IntPtr obj,IntPtr sel,long value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern void SendBool(IntPtr obj,IntPtr sel,[MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern XY SendPoint(IntPtr obj,IntPtr sel);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern void SendFloat(IntPtr obj,IntPtr sel,float value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern IntPtr InitSound(IntPtr obj,IntPtr sel,IntPtr path,[MarshalAs(UnmanagedType.I1)] bool byReference);
    [DllImport(CG)] private static extern int CGWindowLevelForKey(int key);
    [DllImport(CG)] private static extern uint CGMainDisplayID();
    [DllImport(CG)] private static extern CGRect CGDisplayBounds(uint id);
    [DllImport(CG)] private static extern IntPtr CGWindowListCopyWindowInfo(uint options,uint relative);
    [DllImport(CG)] [return:MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict,out CGRect rect);
    [DllImport(CF)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CF)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array,nint index);
    [DllImport(CF)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict,IntPtr key);
    [DllImport(CF)] [return:MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number,int type,out long value);
    [DllImport(CF)] internal static extern IntPtr CFStringCreateWithCString(IntPtr allocator,string text,uint encoding);
    [DllImport(CF)] internal static extern void CFRelease(IntPtr value);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern ulong mach_absolute_time();
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int mach_timebase_info(out Timebase info);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int sysctlbyname(string name,IntPtr old,ref nuint length,IntPtr value,nuint valueLength);
    private static readonly Dictionary<string,IntPtr> selectors=new();
    internal static IntPtr Selector(string name){if(!selectors.TryGetValue(name,out var s))selectors[name]=s=sel_registerName(name);return s;}
    internal static IntPtr String(string text)=>CFStringCreateWithCString(IntPtr.Zero,text,0x08000100);
    private static IntPtr Handle(Window window)
    {
        var h=window.TryGetPlatformHandle()??throw new InvalidOperationException("No native window");
        if(h.HandleDescriptor=="NSWindow")return h.Handle;
        if(h.HandleDescriptor=="NSView")return Send(h.Handle,Selector("window"));
        throw new NotSupportedException("Unexpected macOS handle: "+h.HandleDescriptor);
    }
    internal static void Configure(Window window,bool floating,bool hidden)
    {
        if(!OperatingSystem.IsMacOS())return;var h=Handle(window);
        SendBool(h,Selector("setHasShadow:"),false);SendBool(h,Selector("setHidesOnDeactivate:"),false);
        // Avalonia's native AvnWindow exposes this selector. Restrict it to the
        // pet window so clicking the cat does not steal typing focus.
        if(SendPtr(h,Selector("respondsToSelector:"),Selector("setCanBecomeKeyWindow:"))!=IntPtr.Zero)
            SendBool(h,Selector("setCanBecomeKeyWindow:"),false);
        // All ordinary Spaces, stationary on Mission Control, no key-window cycle.
        // Deliberately no fullScreenAuxiliary: fullscreen apps own their Space.
        SendLong(h,Selector("setCollectionBehavior:"),1|16|64);
        SendLong(h,Selector("setLevel:"),floating&&!hidden?CGWindowLevelForKey(5):CGWindowLevelForKey(2)+1);
    }
    internal static Point Pointer(Window window)
    {
        var at=SendPoint(Handle(window),Selector("mouseLocationOutsideOfEventStream"));
        return new(at.X,window.ClientSize.Height-at.Y);
    }
    internal static void PassThrough(Window window,bool pass)
    {if(OperatingSystem.IsMacOS())SendBool(Handle(window),Selector("setIgnoresMouseEvents:"),pass);}
    internal static void VerifyWindow(Window window)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var h=Handle(window);
        Configure(window,true,false);
        if(Send(h,Selector("level")).ToInt64()!=CGWindowLevelForKey(5))throw new InvalidOperationException("Floating layer read-back failed");
        Configure(window,false,false);
        if(Send(h,Selector("level")).ToInt64()!=CGWindowLevelForKey(2)+1)throw new InvalidOperationException("Desktop layer read-back failed");
        PassThrough(window,true);if(Send(h,Selector("ignoresMouseEvents"))==IntPtr.Zero)throw new InvalidOperationException("Click-through read-back failed");
        PassThrough(window,false);if(Send(h,Selector("ignoresMouseEvents"))!=IntPtr.Zero)throw new InvalidOperationException("Interactive read-back failed");
        if(Send(h,Selector("canBecomeKeyWindow"))!=IntPtr.Zero)throw new InvalidOperationException("Pet can steal keyboard focus");
        var p=Pointer(window);if(!double.IsFinite(p.X)||!double.IsFinite(p.Y))throw new InvalidOperationException("Invalid pointer coordinates");
    }
    internal static double AwakeSeconds
    {
        get {if(!OperatingSystem.IsMacOS())return Environment.TickCount64/1000d;mach_timebase_info(out var t);return mach_absolute_time()*(t.Numer/(double)t.Denom)/1e9;}
    }
    internal static string BootId()
    {
        if(!OperatingSystem.IsMacOS())return "preview-"+Environment.TickCount64;
        nuint length=128;var buffer=Marshal.AllocHGlobal((int)length);
        try{if(sysctlbyname("kern.bootsessionuuid",buffer,ref length,IntPtr.Zero,0)!=0)throw new IOException("Cannot read macOS boot identifier");return Marshal.PtrToStringUTF8(buffer)!;}
        finally{Marshal.FreeHGlobal(buffer);}
    }
    internal static bool FullscreenOnPrimary()
    {
        if(!OperatingSystem.IsMacOS())return false;
        var screen=CGDisplayBounds(CGMainDisplayID());var array=CGWindowListCopyWindowInfo(1|16,0);if(array==IntPtr.Zero)return false;
        var pidKey=String("kCGWindowOwnerPID");var layerKey=String("kCGWindowLayer");var boundsKey=String("kCGWindowBounds");var alphaKey=String("kCGWindowAlpha");
        try
        {
            for(nint i=0;i<CFArrayGetCount(array);i++)
            {
                var entry=CFArrayGetValueAtIndex(array,i);
                CFNumberGetValue(CFDictionaryGetValue(entry,pidKey),4,out long pid);
                CFNumberGetValue(CFDictionaryGetValue(entry,layerKey),4,out long layer);
                if(pid==Environment.ProcessId||layer!=0)continue;
                if(!CGRectMakeWithDictionaryRepresentation(CFDictionaryGetValue(entry,boundsKey),out var r))continue;
                // A normal foreground application on this display hides games
                // behind it; apps on other monitors never determine this result.
                if(r.Origin.X>=screen.Origin.X+screen.Size.X||r.Origin.X+r.Size.X<=screen.Origin.X||r.Origin.Y>=screen.Origin.Y+screen.Size.Y||r.Origin.Y+r.Size.Y<=screen.Origin.Y)continue;
                return r.Origin.X<=screen.Origin.X+2&&r.Origin.Y<=screen.Origin.Y+2&&r.Origin.X+r.Size.X>=screen.Origin.X+screen.Size.X-2&&r.Origin.Y+r.Size.Y>=screen.Origin.Y+screen.Size.Y-2;
            }
            return false;
        }
        finally{CFRelease(array);CFRelease(pidKey);CFRelease(layerKey);CFRelease(boundsKey);CFRelease(alphaKey);}
    }
}

internal sealed class MacVoice : IDisposable
{
    private readonly VoiceCues cues;
    private readonly Dictionary<string,IntPtr> sounds=new();
    private IntPtr active;
    internal MacVoice(string root)
    {
        var catalog=VoiceCatalog.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));cues=new(catalog);
        if(!OperatingSystem.IsMacOS())return;
        foreach(var sound in catalog.Bindings.SelectMany(b=>b.Sounds).DistinctBy(s=>s.File))
        {
            var path=MacNative.String(Path.Combine(root,sound.File));
            try
            {
                var allocated=MacNative.Send(MacNative.objc_getClass("NSSound"),MacNative.Selector("alloc"));
                var player=MacNative.InitSound(allocated,MacNative.Selector("initWithContentsOfFile:byReference:"),path,false);
                if(player==IntPtr.Zero)throw new IOException("Cannot load sound: "+sound.File);sounds.Add(sound.File,player);
            }
            finally{MacNative.CFRelease(path);}
        }
    }
    internal void Present(SpriteFrame? frame,long revision,bool audible,double volume)
    {
        var decision=frame is SpriteFrame f?cues.Observe(f.Clip,f.Index,revision,audible&&volume>0):cues.Observe("",0,revision,false);
        if(decision.Stop)Silence();
        if(decision.Start is VoiceSound sound&&sounds.TryGetValue(sound.File,out var player))
        {active=player;MacNative.SendFloat(player,MacNative.Selector("setVolume:"),(float)Math.Clamp(volume,0,1));MacNative.Send(player,MacNative.Selector("play"));}
    }
    internal void Silence(){if(active==IntPtr.Zero)return;MacNative.Send(active,MacNative.Selector("stop"));active=IntPtr.Zero;}
    internal void SetVolume(double volume){if(active==IntPtr.Zero)return;MacNative.SendFloat(active,MacNative.Selector("setVolume:"),(float)Math.Clamp(volume,0,1));if(volume<=0)Silence();}
    public void Dispose(){Silence();foreach(var p in sounds.Values)MacNative.Send(p,MacNative.Selector("release"));sounds.Clear();}
}
