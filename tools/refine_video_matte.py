"""Remove enclosed white-background leaks and relative edge spill, never blur fur.

This is deterministic matte repair, not animation or character regeneration.
RGB arrays use either RGB or BGR consistently; operations are channel symmetric.
"""
import cv2
import numpy as np


def refine_matte(original):
    mask=(original[:,:,3]>0).astype(np.uint8)
    x,y,w,h=cv2.boundingRect(mask)
    if not w or not h:return original.copy()
    x0=max(0,x-8);y0=max(0,y-8);x1=min(original.shape[1],x+w+8);y1=min(original.shape[0],y+h+8)
    result=original.copy()
    result[y0:y1,x0:x1]=refine_region(original[y0:y1,x0:x1])
    return result


def refine_region(original):
    result=original.copy();rgb=original[:,:,:3];alpha=original[:,:,3]
    low=rgb.min(axis=2);chroma=rgb.max(axis=2).astype(float)-low
    bright=(low>185)&(chroma<35)&(alpha>4)
    count,labels,stats,_=cv2.connectedComponentsWithStats(bright.astype(np.uint8),8)
    seeds=np.bincount(labels[low>230],minlength=count)
    selected=(stats[:,cv2.CC_STAT_AREA]>=24)&(seeds>=8);selected[0]=False
    # Tiny eye glints cannot seed removal of a whole region.
    holes=selected[labels]
    if holes.any():
        reference=(alpha>240)&(low<180)&~holes
        _,nearest=cv2.distanceTransformWithLabels((~reference).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
        palette=np.zeros((nearest.max()+1,3),np.float32);palette[nearest[reference]]=rgb[reference]
        color=palette[nearest];delta=250-color
        coverage=np.clip(np.sum((250-rgb.astype(float))*delta,axis=2)/np.maximum(np.sum(delta*delta,axis=2),400),0,1)
        result[:,:,:3][holes]=color[holes].astype(np.uint8)
        result[:,:,3][holes]=np.round(alpha[holes]*coverage[holes]).astype(np.uint8)
        result[holes&(low>225)]=0
    # Compare edge pixels with nearby interior fur, not a single global white
    # threshold. This catches gray-white spill below the old 220 cutoff.
    a=result[:,:,3];rgb=result[:,:,:3]
    inward=cv2.distanceTransform((a>127).astype(np.uint8),cv2.DIST_L2,5)
    core=(inward>=4)&(a>240)
    if core.any():
        _,nearest=cv2.distanceTransformWithLabels((~core).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
        palette=np.zeros((nearest.max()+1,3),np.float32);palette[nearest[core]]=rgb[core]
        color=palette[nearest]
        spill=(inward<3)&(a>0)&(rgb.min(2)>170)&((rgb.astype(float)-color).mean(2)>28)
        result[:,:,:3][spill]=color[spill].astype(np.uint8)
    # Preserve only the actual body; quantization may split very faint bridges.
    n,components,stats,_=cv2.connectedComponentsWithStats((result[:,:,3]>4).astype(np.uint8),8)
    if n>1:result[components!=(1+np.argmax(stats[1:,cv2.CC_STAT_AREA]))]=0
    result[result[:,:,3]==0]=0
    return final_spill(result)


def final_spill(result):
    # Restoring a hole creates a new silhouette. Audit its edge once more after
    # topology changes, otherwise a previously interior white pixel can survive.
    inward=cv2.distanceTransform((result[:,:,3]>127).astype(np.uint8),cv2.DIST_L2,5)
    low=result[:,:,:3].min(2)
    bright=(low>215)&(result[:,:,3]>0)&(inward<3)
    if bright.any():
        core=(inward>=3)&(low<200)&(result[:,:,3]>240)
        if core.any():
            _,nearest=cv2.distanceTransformWithLabels((~core).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
            palette=np.zeros((nearest.max()+1,3),np.uint8);palette[nearest[core]]=result[:,:,:3][core]
            result[:,:,:3][bright]=palette[nearest[bright]]
    return result
