"""Reviewed chest/flank and planted paw support, in the 768px canvas.

Eight equally spaced samples from contact-105/107/122/123/124; exclude a
sweeping tail, the extended rear paw of A, and suspended forepaws while curling.
"""
import numpy as np
KEYS={
    105:[582,582,580,584,595,598,600,601],
    107:[636,596,600,608,608,610,601,601],
    122:[610,607,592,591,587,593,593,593],
    123:[593,593,591,571,571,579,582,582],
    124:[582,582,568,568,582,593,593,593],
}
def support(n,count,first,last):
    keys=np.array(KEYS[n],dtype=float);keys[0]=first;keys[-1]=last
    phase=np.linspace(0,7,count);i=np.minimum(phase.astype(int),6);t=phase-i;t=t*t*(3-2*t)
    return keys[i]*(1-t)+keys[i+1]*t
