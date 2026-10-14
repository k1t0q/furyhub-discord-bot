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
# Zoomed square around the point, proportionate to the source map resolution.
size=max(1,min(570,w,h))
left=max(0,min(px-size//2,w-size))
top=max(0,min(py-size//2,h-size))
crop=img[top:top+size,left:left+size].copy()
cx=px-left;cy=py-top
radius=max(7,round(size*0.022))
cv2.circle(crop,(cx,cy),radius+4,(255,255,255),-1,cv2.LINE_AA)
cv2.circle(crop,(cx,cy),radius,(25,40,230),-1,cv2.LINE_AA)
cv2.circle(crop,(cx,cy),max(2,radius//4),(255,255,255),-1,cv2.LINE_AA)
if not cv2.imwrite(sys.argv[4],crop):
    raise SystemExit("Failed to write preview")
