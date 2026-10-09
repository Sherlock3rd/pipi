using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Chenpi;

internal static class MacNative
{
    private sealed class WindowState {public bool Initialized;public long? Level;public bool? Pass;}
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window,WindowState> windowStates=new();
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
    [DllImport(ObjC,EntryPoint="objc_msgSend")] [return:MarshalAs(UnmanagedType.I1)] private static extern bool ReadBool(IntPtr obj,IntPtr sel);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] [return:MarshalAs(UnmanagedType.I1)] private static extern bool Responds(IntPtr obj,IntPtr sel,IntPtr selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern XY SendPoint(IntPtr obj,IntPtr sel);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] private static extern XY ConvertPoint(IntPtr obj,IntPtr sel,XY point);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern void SendFloat(IntPtr obj,IntPtr sel,float value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")] internal static extern IntPtr BeginActivity(IntPtr obj,IntPtr sel,ulong options,IntPtr reason);
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
        var state=windowStates.GetOrCreateValue(window);
        if(!state.Initialized)
        {
        SendBool(h,Selector("setHasShadow:"),false);SendBool(h,Selector("setHidesOnDeactivate:"),false);
        // Avalonia's native AvnWindow exposes this selector. Restrict it to the
        // pet window so clicking the cat does not steal typing focus.
        if(Responds(h,Selector("respondsToSelector:"),Selector("setCanBecomeKeyWindow:")))
            SendBool(h,Selector("setCanBecomeKeyWindow:"),false);
        // All ordinary Spaces, stationary on Mission Control, no key-window cycle.
        // Deliberately no fullScreenAuxiliary: fullscreen apps own their Space.
        SendLong(h,Selector("setCollectionBehavior:"),1|16|64);
        state.Initialized=true;
        }
        // Finder's transparent desktop icon window receives input above the
        // wallpaper layer. A visible pet below that window cannot be clicked.
        // Keep the desktop pet above icons but below ordinary application windows.
        long level=floating&&!hidden?CGWindowLevelForKey(5):CGWindowLevelForKey(18)+1;
        if(Program.Args.Contains("--audit-legacy-layer")&&!floating&&!hidden)level=CGWindowLevelForKey(2)+1;
        if(state.Level!=level){SendLong(h,Selector("setLevel:"),level);state.Level=level;}
    }
    internal static Point Pointer(Window window)
    {
        var at=SendPoint(Handle(window),Selector("mouseLocationOutsideOfEventStream"));
        return new(at.X,window.ClientSize.Height-at.Y);
    }
    internal static Point EventPoint(Window window,Point client)
    {
        var p=ConvertPoint(Handle(window),Selector("convertPointToScreen:"),new XY{X=client.X,Y=window.ClientSize.Height-client.Y});
        return new(p.X,CGDisplayBounds(CGMainDisplayID()).Size.Y-p.Y);
    }
    internal static object InputState(Window window)=>new{Level=Send(Handle(window),Selector("level")).ToInt64(),DesktopIconLevel=CGWindowLevelForKey(18),NormalLevel=CGWindowLevelForKey(4),PassThrough=ReadBool(Handle(window),Selector("ignoresMouseEvents")),Key=ReadBool(Handle(window),Selector("isKeyWindow")),Pointer=Pointer(window)};
    internal static void PassThrough(Window window,bool pass)
    {if(OperatingSystem.IsMacOS()){var state=windowStates.GetOrCreateValue(window);if(state.Pass!=pass){SendBool(Handle(window),Selector("setIgnoresMouseEvents:"),pass);state.Pass=pass;}}}
    internal static bool WindowVisible(Window window)=>!OperatingSystem.IsMacOS()||ReadBool(Handle(window),Selector("isVisible"));
    internal static void VerifyWindow(Window window)
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var h=Handle(window);
        Configure(window,true,false);
        if(Send(h,Selector("level")).ToInt64()!=CGWindowLevelForKey(5))throw new InvalidOperationException("Floating layer read-back failed");
        Configure(window,false,false);
        long desktopLevel=Send(h,Selector("level")).ToInt64();
        if(desktopLevel<=CGWindowLevelForKey(18)||desktopLevel>=CGWindowLevelForKey(4))throw new InvalidOperationException("Desktop pet must be above Finder icons and below ordinary windows");
        PassThrough(window,true);if(!ReadBool(h,Selector("ignoresMouseEvents")))throw new InvalidOperationException("Click-through read-back failed");
        PassThrough(window,false);if(ReadBool(h,Selector("ignoresMouseEvents")))throw new InvalidOperationException("Interactive read-back failed");
        if(ReadBool(h,Selector("canBecomeKeyWindow")))throw new InvalidOperationException("Pet can steal keyboard focus");
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

// Visible desktop animation must not be timer-throttled as background work.
// This scoped activity still allows display/system idle sleep, and is released
// whenever the pet is hidden, a fullscreen app takes over, or the app exits.
internal sealed class MacAnimationActivity : IDisposable
{
    private IntPtr token,info;
    public void SetActive(bool active)
    {
        if(!OperatingSystem.IsMacOS())return;
        if(active&&token==IntPtr.Zero)
        {
            info=MacNative.Send(MacNative.objc_getClass("NSProcessInfo"),MacNative.Selector("processInfo"));
            var reason=MacNative.String("Visible desktop pet animation");
            try
            {
                const ulong userInitiatedAllowingIdleSystemSleep=0x00FFFFFFUL&~(1UL<<20);
                token=MacNative.BeginActivity(info,MacNative.Selector("beginActivityWithOptions:reason:"),userInitiatedAllowingIdleSystemSleep,reason);
                if(token!=IntPtr.Zero)MacNative.Send(token,MacNative.Selector("retain"));
            }
            finally{MacNative.CFRelease(reason);}
        }
        else if(!active&&token!=IntPtr.Zero)
        {MacNative.SendPtr(info,MacNative.Selector("endActivity:"),token);MacNative.Send(token,MacNative.Selector("release"));token=IntPtr.Zero;}
    }
    public void Dispose()=>SetActive(false);
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
