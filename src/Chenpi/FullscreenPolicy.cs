namespace Chenpi;

// Physical screen pixels throughout; never compare these with WPF logical sizes.
internal readonly record struct PixelBounds(int Left,int Top,int Right,int Bottom)
{
    public bool Valid=>Right>Left&&Bottom>Top;
    public bool Covers(PixelBounds other)=>Valid&&other.Valid&&
        Left<=other.Left+2&&Top<=other.Top+2&&Right>=other.Right-2&&Bottom>=other.Bottom-2;
}

internal sealed record FullscreenCandidate(PixelBounds Bounds,string ClassName,
    bool Visible,bool Minimized,bool Cloaked,bool Maximized,bool HasCaption,bool OwnProcess);

internal readonly record struct FullscreenDecision(bool Hide,string Reason);

internal static class FullscreenPolicy
{
    public static FullscreenDecision Evaluate(FullscreenCandidate? candidate,PixelBounds petScreen)
    {
        if(candidate is null)return new(false,"no-window");
        if(candidate.OwnProcess)return new(false,"own-window");
        if(!candidate.Visible||candidate.Minimized||candidate.Cloaked)return new(false,"not-visible");
        if(candidate.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return new(false,"desktop-shell");
        if(candidate.Maximized&&candidate.HasCaption)return new(false,"ordinary-maximized");
        if(!candidate.Bounds.Valid||!petScreen.Valid)return new(false,"invalid-bounds");
        return candidate.Bounds.Covers(petScreen)?new(true,"fullscreen-on-pet-screen"):new(false,"not-covering-pet-screen");
    }
}
