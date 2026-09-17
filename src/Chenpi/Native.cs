using System;
using System.Diagnostics;
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
            SetWindowLong(h,-20,GetWindowLong(h,-20)&~(0x00000080|0x08000000));
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
    public static bool IsFullScreen(IntPtr foreground,IntPtr self)
    {
        if(foreground==IntPtr.Zero||foreground==self)return false;
        GetWindowThreadProcessId(foreground,out uint process);if(process==Environment.ProcessId)return false;
        string cls=ClassName(foreground);if(cls is "Progman" or "WorkerW" or "Shell_TrayWnd")return false;
        if(!GetWindowRect(foreground,out var r))return false;
        var screen=System.Windows.Forms.Screen.FromHandle(foreground).Bounds;
        var work=System.Windows.Forms.Screen.FromHandle(foreground).WorkingArea;
        // A normal maximized window covers working area, not the monitor including taskbar.
        bool covers=r.Left<=screen.Left+2&&r.Top<=screen.Top+2&&r.Right>=screen.Right-2&&r.Bottom>=screen.Bottom-2;
        if(IsZoomed(foreground)&&((GetWindowLong(foreground,-16)&0x00C00000)==0x00C00000))return false;
        return covers;
    }
}
