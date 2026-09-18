using System;

namespace Chenpi;

// Straight BGRA in/out. Repair white-matte contamination before WPF premultiplies
// and scales the texture. Only a narrow silhouette band may change.
public static class AlphaMatte
{
    public static byte[] Clean(byte[] pixels,int width,int height)
    {
        if(width<=0||height<=0||pixels.Length!=width*height*4)throw new ArgumentException("Invalid BGRA image");
        int n=width*height;var distance=new int[n];var nearest=new int[n];var queue=new int[n];
        Array.Fill(nearest,-1);
        for(int i=0;i<n;i++)distance[i]=pixels[i*4+3]<128?0:10000;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {int i=y*width+x;if(x>0)distance[i]=Math.Min(distance[i],distance[i-1]+1);if(y>0)distance[i]=Math.Min(distance[i],distance[i-width]+1);}
        for(int y=height-1;y>=0;y--)for(int x=width-1;x>=0;x--)
        {int i=y*width+x;if(x+1<width)distance[i]=Math.Min(distance[i],distance[i+1]+1);if(y+1<height)distance[i]=Math.Min(distance[i],distance[i+width]+1);}
        int head=0,tail=0;
        for(int i=0;i<n;i++)if(distance[i]>=4&&pixels[i*4+3]>=250){nearest[i]=i;queue[tail++]=i;}
        void Visit(int from,int next)
        {if(nearest[next]<0&&pixels[next*4+3]>0){nearest[next]=nearest[from];queue[tail++]=next;}}
        while(head<tail)
        {
            int i=queue[head++],x=i%width,y=i/width;
            if(x>0)Visit(i,i-1);if(x+1<width)Visit(i,i+1);
            if(y>0)Visit(i,i-width);if(y+1<height)Visit(i,i+width);
        }
        var output=(byte[])pixels.Clone();
        for(int i=0;i<n;i++)
        {
            int p=i*4;
            if(pixels[p+3]==0){output[p]=output[p+1]=output[p+2]=0;continue;}
            if(distance[i]>3||nearest[i]<0)continue;
            int q=nearest[i]*4;double numerator=0,denominator=0,brightness=0;
            for(int c=0;c<3;c++)
            {double f=255-pixels[q+c];numerator+=(255-pixels[p+c])*f;denominator+=f*f;brightness+=pixels[p+c]-pixels[q+c];}
            double ratio=denominator>1?Math.Clamp(numerator/denominator,0,1):1;
            double alpha=pixels[p+3]/255d;
            // Do not flatten genuine darker fur/texture; remove only lighter matte spill.
            if(brightness>18&&ratio<.94)
            {
                alpha*=ratio*ratio;
                for(int c=0;c<3;c++)output[p+c]=pixels[q+c];
            }
            output[p+3]=(byte)Math.Clamp(Math.Round(alpha*255),0,255);
            if(output[p+3]<4)output[p]=output[p+1]=output[p+2]=output[p+3]=0;
        }
        // Filter coverage and premultiplied color together. The old hard inward
        // erosion amplified single-pixel stairs when the 256px source was enlarged.
        // Restrict reconstruction to the silhouette; solid fur stays untouched.
        var smooth=(byte[])output.Clone();
        int[] weights={1,6,1};
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x,p=i*4;
            if(distance[i]>2)continue;
            double a=0,b=0,g=0,r=0;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {
                int xx=x+dx,yy=y+dy;if(xx<0||xx>=width||yy<0||yy>=height)continue;
                int q=(yy*width+xx)*4;double coverage=output[q+3]*weights[dx+1]*weights[dy+1]/64d;
                a+=coverage;b+=output[q]*coverage;g+=output[q+1]*coverage;r+=output[q+2]*coverage;
            }
            smooth[p+3]=(byte)Math.Clamp(Math.Round(a),0,255);
            if(a>0){smooth[p]=(byte)Math.Round(b/a);smooth[p+1]=(byte)Math.Round(g/a);smooth[p+2]=(byte)Math.Round(r/a);}
            else smooth[p]=smooth[p+1]=smooth[p+2]=0;
        }
        return smooth;
    }
}
