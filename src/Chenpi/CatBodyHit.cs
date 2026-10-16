using System;

namespace Chenpi;

// Anatomical regions in the displayed opaque silhouette, never screen pixels.
// Alpha and foreground rejection belong to Scene; these weights partition only
// visible cat pixels. Side poses use authored orientation, not a stale walk flag.
public static class CatBodyHit
{
    public static CatBodyPart Resolve(double x,double y,string pose)
    {
        x=Math.Clamp(x,0,1);y=Math.Clamp(y,0,1);
        if(pose is "SL" or "WL" or "RL" or "HL")x=1-x;
        if(pose is "SR" or "SL" or "WR" or "WL" or "RR" or "RL" or "HR" or "HL")
        {
            if(x<.27)return CatBodyPart.Tail;
            if(x>.68&&y<.53)return CatBodyPart.Head;
            if(y>.76)return CatBodyPart.Paw;
            return y<.50?CatBodyPart.Back:CatBodyPart.Belly;
        }
        if(pose is "B" or "A" or "D" or "X" or "M" or "C")
        {
            if(x>.70&&y>.35)return CatBodyPart.Head;
            if(x<.15)return CatBodyPart.Tail;
            if(pose=="B")return y<.33?CatBodyPart.Paw:y>.82?CatBodyPart.Back:CatBodyPart.Belly;
            return y>.75?CatBodyPart.Paw:y<.38?CatBodyPart.Back:CatBodyPart.Belly;
        }
        if(y<.42)return CatBodyPart.Head;
        if(x<.22&&y>.63)return CatBodyPart.Tail;
        if(y>.86)return CatBodyPart.Paw;
        return y<.57?CatBodyPart.Back:CatBodyPart.Belly;
    }
}
