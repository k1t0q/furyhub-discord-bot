#!/usr/bin/env python3
"""Detect the unique red white-rim marker in a SCUM map screenshot. JSON on stdout."""
import json,sys
import cv2
import numpy as np
# 1920x1080 reference screenshot: square in-game map spans x=420..1500, full height.
BOUNDS=(420,0,1500,1080)
def dots(img):
    hsv=cv2.cvtColor(img,cv2.COLOR_BGR2HSV)
    mask=cv2.inRange(hsv,(0,125,110),(12,255,255))|cv2.inRange(hsv,(170,125,110),(179,255,255))
    n,_,stats,centres=cv2.connectedComponentsWithStats(mask)
    found=[]
    for k in range(1,n):
        x,y,w,h,area=map(int,stats[k])
        if not (35<=area<=330 and .7<w/max(h,1)<1.4 and 6<=w<=25 and 6<=h<=25):continue
        cx,cy=map(float,centres[k]);rim=[]
        for a in np.linspace(0,2*np.pi,24,endpoint=False):
            px=int(round(cx+max(w,h)*.55*np.cos(a)));py=int(round(cy+max(w,h)*.55*np.sin(a)))
            if 0<=px<img.shape[1] and 0<=py<img.shape[0]:
                hsvpx=hsv[py,px];rim.append(int(hsvpx[1])<90 and int(hsvpx[2])>175)
        score=float(np.mean(rim)) if rim else 0
        if score>=.80:found.append((cx,cy,score))
    return found
def locate(img,reference):
    x0,y0,x1,y1=BOUNDS
    base=reference[y0:y1,x0:x1]
    # SIFT is more reliable than ORB on cropped map fragments (even ~300x150px).
    detector=cv2.SIFT_create(nfeatures=8000)
    ka,da=detector.detectAndCompute(cv2.cvtColor(img,cv2.COLOR_BGR2GRAY),None)
    kb,db=detector.detectAndCompute(cv2.cvtColor(base,cv2.COLOR_BGR2GRAY),None)
    if da is None or db is None:return {'status':'no_features'}
    good=[a for a,b in cv2.BFMatcher(cv2.NORM_L2).knnMatch(da,db,k=2) if a.distance<.72*b.distance]
    if len(good)<22:return {'status':'insufficient_matches','matches':len(good)}
    H,m=cv2.findHomography(np.float32([ka[p.queryIdx].pt for p in good]),np.float32([kb[p.trainIdx].pt for p in good]),cv2.RANSAC,3)
    inliers=int(m.sum()) if m is not None else 0
    if H is None or inliers<18 or inliers/len(good)<.75:return {'status':'weak_alignment'}
    candidates=dots(img)
    if len(candidates)!=1:return {'status':'ambiguous_red_dots','candidates':len(candidates)}
    cx,cy,score=candidates[0]
    p=cv2.perspectiveTransform(np.float32([[[cx,cy]]]),H)[0,0]
    nx,ny=float(p[0]/base.shape[1]),float(p[1]/base.shape[0])
    if not (0<=nx<=1 and 0<=ny<=1):return {'status':'outside_map'}
    X=619646.8573-nx*1525015.8839
    Y=619659.7258-ny*1524017.2528
    return {'status':'detected','x':round(X,1),'y':round(Y,1),'sector':'DCBAZ'[min(4,int(ny*5))]+str(4-min(4,int(nx*5))),'matches':len(good),'inliers':inliers,'rim':round(score,2)}
if __name__=='__main__':
    try:
        if len(sys.argv)!=3:raise ValueError('Expected reference and image paths')
        ref=cv2.imread(sys.argv[1]);img=cv2.imread(sys.argv[2])
        if ref is None or img is None:raise ValueError('Image missing or unreadable')
        print(json.dumps(locate(img,ref)))
    except Exception as err:
        print(json.dumps({'status':'error','error':str(err)[:150]}))
