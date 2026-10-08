using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Chenpi;

public sealed class FrameSequence<T> : IReadOnlyList<T> where T:class
{
    private readonly string[] paths;
    private readonly FrameCache<T> cache;
    private int last=-1;
    public FrameSequence(IEnumerable<string> paths,FrameCache<T> cache){this.paths=paths.ToArray();this.cache=cache;}
    public int Count=>paths.Length;
    public T this[int index]
    {
        get
        {
            var value=cache.Get(paths[index]);
            if(last!=index)
            {
                int direction=last>=0&&index==last-1?-1:1;last=index;
                cache.Prefetch(Enumerable.Range(1,12).Select(n=>paths[(index+direction*n%Count+Count)%Count]).Distinct().ToArray());
            }
            return value;
        }
    }
    public IEnumerator<T> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}
