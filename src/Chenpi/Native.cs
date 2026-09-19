using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Chenpi;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct RECT {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] internal struct POINT {public int X,Y;}
    [DllImport("kernel32.dll")] private static extern bool QueryUnbiasedInterruptTime(out ulong value);
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int kind,IntPtr buffer,int size,out int returned);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string? cls,string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string? cls,string? name);
    private delegate bool EnumProc(IntPtr handle,IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr param);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr handle,int index,int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr handle,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] private static extern int DwmBounds(IntPtr window,int attribute,out RECT value,int size);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] private static extern int DwmFlags(IntPtr window,int attribute,out int value,int size);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window,ref POINT point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    public static double AwakeSeconds {get {if(!QueryUnbiasedInterruptTime(out var n))throw new InvalidOperationException("读取系统有效运行时间失败");return n/1e7;}}
    public static string BootIdentifier()
    {
        var memory=Marshal.AllocHGlobal(64);
        try {if(NtQuerySystemInformation(90,memory,64,out _)>=0){byte[] bytes=new byte[16];Marshal.Copy(memory,bytes,0,16);return new Guid(bytes).ToString();}}
        finally {Marshal.FreeHGlobal(memory);}
        // A conservative fallback: mark every launch as a new session rather than invent uptime.
        return "unavailable-"+Guid.NewGuid();
    }
    public static IntPtr DesktopHost()
    {
        IntPtr result=IntPtr.Zero;
        EnumWindows((h,_)=>{if(FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero){result=h;return false;}return true;},IntPtr.Zero);
        return result;
    }
    public static string ClassName(IntPtr hwnd){var s=new StringBuilder(256);GetClassName(hwnd,s,s.Capacity);return s.ToString();}
    public static object WindowDiagnostics(Window window)
    {
        var h=new WindowInteropHelper(window).Handle;GetWindowRect(h,out var r);
        return new {handle=h.ToInt64(),parent=GetParent(h).ToInt64(),style=GetWindowLong(h,-16).ToString("X8"),extendedStyle=GetWindowLong(h,-20).ToString("X8"),visible=IsWindowVisible(h),left=r.Left,top=r.Top,right=r.Right,bottom=r.Bottom,state=window.WindowState.ToString(),visibility=window.Visibility.ToString(),window.ActualWidth,window.ActualHeight};
    }
    public static bool Attach(Window window,bool floating)
    {
        var h=new WindowInteropHelper(window).Handle;
        var parent=floating?IntPtr.Zero:DesktopHost();
        if(!floating&&parent==IntPtr.Zero)return false;
        window.Topmost=floating;
        if(floating)
        {
            SetParent(h,IntPtr.Zero);
            var style=GetWindowLong(h,-16);SetWindowLong(h,-16,(style&~0x40000000)|unchecked((int)0x80000000));
            // Mouse interaction must not steal foreground activation from a game.
            SetWindowLong(h,-20,(GetWindowLong(h,-20)&~0x00000080)|0x08000000);
        }
        else
        {
            int oldStyle=GetWindowLong(h,-16);
            SetWindowLong(h,-16,(oldStyle&~unchecked((int)0x80000000))|0x40000000);
            SetParent(h,parent);
            SetWindowLong(h,-20,GetWindowLong(h,-20)|0x00000080|0x08000000);
        }
        var position=new POINT{X=(int)Math.Round(window.Left*window.Dpi()),Y=(int)Math.Round(window.Top*window.Dpi())};
        if(!floating)ScreenToClient(parent,ref position);
        bool placed=SetWindowPos(h,floating?new IntPtr(-1):IntPtr.Zero,position.X,position.Y,(int)(window.Width*window.Dpi()),(int)(window.Height*window.Dpi()),0x0010|0x0020|0x0040);
        window.InvalidateVisual();
        return placed&&(floating?(GetWindowLong(h,-16)&0x40000000)==0:GetParent(h)==parent);
    }
    private static double Dpi(this Window w)=>System.Windows.Media.VisualTreeHelper.GetDpi(w).DpiScaleX;
    internal static FullscreenCandidate? ReadFullscreenCandidate(IntPtr window)
    {
        if(window==IntPtr.Zero||!IsWindow(window)||!GetWindowRect(window,out var r))return null;
        // DWM excludes invisible resize borders. The app manifest is per-monitor DPI aware,
        // so the GetWindowRect fallback and monitor bounds use the same physical coordinates.
        if(DwmBounds(window,9,out var visible,Marshal.SizeOf<RECT>())>=0&&visible.Right>visible.Left&&visible.Bottom>visible.Top)r=visible;
        bool cloaked=DwmFlags(window,14,out int flags,sizeof(int))>=0&&flags!=0;
        GetWindowThreadProcessId(window,out uint process);
        return new(new(r.Left,r.Top,r.Right,r.Bottom),ClassName(window),IsWindowVisible(window),IsIconic(window),
            cloaked,IsZoomed(window),(GetWindowLong(window,-16)&0x00C00000)==0x00C00000,process==Environment.ProcessId);
    }
    public static FullscreenDecision CheckFullScreen(IntPtr foreground,IntPtr self)
    {
        var screen=System.Windows.Forms.Screen.FromHandle(self).Bounds;
        return CheckFullScreenOnScreen(foreground,new(screen.Left,screen.Top,screen.Right,screen.Bottom));
    }
    internal static FullscreenDecision CheckFullScreenOnScreen(IntPtr foreground,PixelBounds screen)
    {
        var candidates=new List<FullscreenCandidate>();
        EnumWindows((h,_)=>{
            // Ignore transparent/nonactivating utility overlays (including us).
            if((GetWindowLong(h,-20)&(0x00000020|0x00000080|0x08000000))!=0)return true;
            if(ReadFullscreenCandidate(h) is {} candidate)candidates.Add(candidate);
            return true;
        },IntPtr.Zero);
        return FullscreenPolicy.EvaluateVisible(ReadFullscreenCandidate(foreground),candidates,screen);
    }
}
