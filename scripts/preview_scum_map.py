#!/usr/bin/env python3
"""Render a 570x570 preview around world X/Y on SCUM screenshot reference."""
import sys,cv2,numpy as np
from pathlib import Path
if len(sys.argv)!=5:raise SystemExit("Usage: preview.py reference X Y output")
ref=cv2.imread(sys.argv[1],cv2.IMREAD_COLOR)
if ref is None or ref.shape[1]!=1920 or ref.shape[0]!=1080:raise SystemExit("Reference must be the 1920x1080 full-map screenshot")
x,y=float(sys.argv[2]),float(sys.argv[3])
nx=(619646.8573-x)/1525015.8839
ny=(619659.7258-y)/1524017.2528
if not (0<=nx<=1 and 0<=ny<=1):raise SystemExit("Coordinates outside map bounds")
map_img=ref[:,420:1500]
h,w=map_img.shape[:2]
px=int(round(nx*(w-1)));py=int(round(ny*(h-1)))
size=570
left=max(0,min(px-size//2,w-size));top=max(0,min(py-size//2,h-size))
crop=map_img[top:top+size,left:left+size].copy()
cx=px-left;cy=py-top
cv2.circle(crop,(cx,cy),13,(255,255,255),-1,cv2.LINE_AA)
cv2.circle(crop,(cx,cy),9,(35,35,245),-1,cv2.LINE_AA)
cv2.circle(crop,(cx,cy),3,(255,255,255),-1,cv2.LINE_AA)
if not cv2.imwrite(sys.argv[4],crop):raise SystemExit("Failed to write preview")
