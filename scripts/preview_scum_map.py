#!/usr/bin/env python3
"""Private square marker preview using the same base map as SCUM HUB."""
import sys
import cv2
from pathlib import Path
from urllib.request import Request, urlopen

if len(sys.argv)!=5:
    raise SystemExit("Usage: preview_scum_map.py reference_path X Y output")
reference=Path(sys.argv[1])
cache=reference.with_name("scum_hub_map.png")
if not cache.is_file():
    urls=[
        "https://k1t0q.github.io/scum-map-k1t0/scum_map_fallback.png",
        "https://raw.githubusercontent.com/k1t0q/scum-map-k1t0/main/scum_map_fallback.png"
    ]
    for url in urls:
        try:
            with urlopen(Request(url,headers={"User-Agent":"SCUM-HUB-Bot/1.0"}),timeout=12) as response:
                data=response.read(15*1024*1024+1)
            if len(data)>15*1024*1024 or not data.startswith(b"\x89PNG\r\n\x1a\n"):
                raise ValueError("Invalid map PNG")
            cache.parent.mkdir(parents=True,exist_ok=True)
            temporary=cache.with_suffix(".tmp")
            temporary.write_bytes(data)
            temporary.replace(cache)
            break
        except Exception as e:
            print("Map download failed: "+str(e),file=sys.stderr)
    if not cache.is_file():
        raise SystemExit("Could not download SCUM HUB base map")

img=cv2.imread(str(cache),cv2.IMREAD_COLOR)
if img is None:raise SystemExit("SCUM HUB base map is unreadable")
h,w=img.shape[:2]
x,y=float(sys.argv[2]),float(sys.argv[3])
nx=(619646.8573-x)/(619646.8573-(-905369.0266))
ny=(619659.7258-y)/(619659.7258-(-904357.527))
if not (0<=nx<=1 and 0<=ny<=1):
    raise SystemExit("Coordinates outside map bounds")
px=int(round(nx*(w-1)));py=int(round(ny*(h-1)))
# Show roughly one map sector across the preview, with the marker always centered.
# Cropping 570 source pixels produced a zoomed-out image and pushed northern markers to the edge.
import numpy as np
side=max(64,round(min(w,h)/5))
half=side//2
start_x=px-half
start_y=py-half
end_x=start_x+side
end_y=start_y+side
crop=np.full((side,side,3), (12,12,12),dtype=np.uint8)
sx0=max(0,start_x);sy0=max(0,start_y)
sx1=min(w,end_x);sy1=min(h,end_y)
if sx1>sx0 and sy1>sy0:
    crop[sy0-start_y:sy1-start_y,sx0-start_x:sx1-start_x]=img[sy0:sy1,sx0:sx1]
crop=cv2.resize(crop,(570,570),interpolation=cv2.INTER_CUBIC)
center=(285,285)
cv2.circle(crop,center,17,(255,255,255),-1,cv2.LINE_AA)
cv2.circle(crop,center,12,(25,40,230),-1,cv2.LINE_AA)
cv2.circle(crop,center,4,(255,255,255),-1,cv2.LINE_AA)
if not cv2.imwrite(sys.argv[4],crop):
    raise SystemExit("Failed to write preview")
