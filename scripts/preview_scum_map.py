#!/usr/bin/env python3
"""Render the complete SCUM HUB sector with a marker at its actual X/Y position."""
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
if img is None:
    raise SystemExit("SCUM HUB base map is unreadable")
h,w=img.shape[:2]
x,y=float(sys.argv[2]),float(sys.argv[3])
nx=(619646.8573-x)/(619646.8573-(-905369.0266))
ny=(619659.7258-y)/(619659.7258-(-904357.527))
if not (0<=nx<=1 and 0<=ny<=1):
    raise SystemExit("Coordinates outside map bounds")

# SCUM HUB uses the 5x5 grid, rows D,C,B,A,Z; columns 4,3,2,1,0.
col=min(4,int(nx*5))
row=min(4,int(ny*5))
sector="DCBAZ"[row]+str(4-col)

# Fixed sector boundaries, NOT a crop centered on the marker.
x0=round(col*w/5)
x1=round((col+1)*w/5)
y0=round(row*h/5)
y1=round((row+1)*h/5)
if x1<=x0 or y1<=y0:
    raise SystemExit("Invalid sector bounds")
crop=img[y0:y1,x0:x1].copy()

# Locate the marker in the selected sector, then scale image and marker together.
local_x=(nx*w-x0)/(x1-x0)
local_y=(ny*h-y0)/(y1-y0)
px=max(0,min(569,round(local_x*570)))
py=max(0,min(569,round(local_y*570)))
crop=cv2.resize(crop,(570,570),interpolation=cv2.INTER_CUBIC)

# Full-sector frame, title and point. The point can be anywhere within the square.
cv2.rectangle(crop,(1,1),(568,568),(220,220,220),2,cv2.LINE_AA)
cv2.putText(crop,sector,(18,58),cv2.FONT_HERSHEY_SIMPLEX,1.7,(10,10,10),8,cv2.LINE_AA)
cv2.putText(crop,sector,(18,58),cv2.FONT_HERSHEY_SIMPLEX,1.7,(245,245,245),4,cv2.LINE_AA)
cv2.circle(crop,(px,py),17,(255,255,255),-1,cv2.LINE_AA)
cv2.circle(crop,(px,py),12,(25,40,230),-1,cv2.LINE_AA)
cv2.circle(crop,(px,py),4,(255,255,255),-1,cv2.LINE_AA)
if not cv2.imwrite(sys.argv[4],crop):
    raise SystemExit("Failed to write preview")
