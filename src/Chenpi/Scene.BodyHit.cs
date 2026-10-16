using System;
#if AVALONIA
using Avalonia;
using HitGeometry=Avalonia.Media.Geometry;
#else
using System.Windows;
using HitGeometry=System.Windows.Media.Geometry;
#endif

namespace Chenpi;

internal sealed partial class Scene
{
    private CatBodyPart pressedBodyPart;
    private bool pressedBodyLeft;
    private void RememberBodyHit(Point p){pressedBodyPart=BodyPartAt(p);pressedBodyLeft=playback.HitPose is "SL" or "WL" or "RL" or "HL";}
    private static readonly HitGeometry hitNestRim=HitGeometry.Parse("M -98,-34 Q -82,-39 -67,-53 C -48,-46 -25,-46 0,-46 C 25,-46 48,-46 67,-53 Q 82,-39 98,-34 L 98,0 L -98,0 Z");
    private bool CatContains(Point point)
    {
        if(currentCatBounds is not Rect bounds||DisplayedDefinition is not SpriteClip d)return CatRect.Contains(point);
        double x=point.X-Engine.VisualPosition.X,y=point.Y-(Engine.VisualPosition.Y-lift-poseLift);
        if(Engine.FacingLeft&&d.MirrorWithFacing)x=-x;
        if(!bounds.Contains(new Point(x,y)))return false;
        var frames=GetFrames(DisplayedClip);
        if(frames is null||DisplayedFrame>=frames.Count)return false;
        var bitmap=frames[DisplayedFrame];
        int px=Math.Clamp((int)((x-bounds.X)/bounds.Width*bitmap.PixelWidth),0,bitmap.PixelWidth-1);
        int py=Math.Clamp((int)((y-bounds.Y)/bounds.Height*bitmap.PixelHeight),0,bitmap.PixelHeight-1);
        var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(px,py,1,1),pixel,4,0);
        if(pixel[3]<32)return false;
        if(Engine.Support.Kind=="litter"&&Engine.Support.Height>=InteractionGeometry.LitterSurfaceLift-.01)
        {
            double dx=(point.X-Engine.LitterSpot.X)/InteractionGeometry.LitterHalfWidth;
            if(Math.Abs(dx)<=1&&point.Y>=Engine.LitterSpot.Y-34-26*dx*dx&&point.Y<=Engine.LitterSpot.Y)return false;
        }
        // Same low front rim used by DrawNest: the elevated side arms are behind.
        if(nestOcclusion&&NestForegroundContains(point))return false;
        return true;
    }
    private CatBodyPart BodyPartAt(Point point)
    {
        if(currentCatBounds is not Rect b)return CatBodyPart.Head;
        double x=point.X-Engine.VisualPosition.X,y=point.Y-(Engine.VisualPosition.Y-lift-poseLift);
        if(Engine.FacingLeft&&DisplayedDefinition?.MirrorWithFacing==true)x=-x;
        return CatBodyHit.Resolve((x-b.X)/b.Width,(y-b.Y)/b.Height,playback.HitPose);
    }
    private bool NestForegroundContains(Point point)
    {
        double x=(point.X-Engine.Nest.X)/InteractionGeometry.NestScale;
        double y=(point.Y-Engine.Nest.Y)/InteractionGeometry.NestScale;
        return hitNestRim.FillContains(new Point(x,y));
    }
}
