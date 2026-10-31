from concurrent.futures import ThreadPoolExecutor
from urllib.request import urlopen
from PIL import Image
from pathlib import Path
import io
root=Path(__file__).resolve().parent;cache=root/'tiles-build';cache.mkdir(exist_ok=True)
def load(pair):
 x,y=pair;p=cache/f'{x}_{y}.webp'
 if not p.exists():
  with urlopen(f'https://cdn.scum-map.com/tiles/scum/island/v50.0.2025.06.17-scum-1.0/regular/4/{x}_{y}.webp',timeout=30) as r:p.write_bytes(r.read())
 return x,y,Image.open(p).convert('RGB')
out=Image.new('RGB',(5120,5120))
with ThreadPoolExecutor(max_workers=6) as pool:
 for x,y,im in pool.map(load,[(x,y) for y in range(4) for x in range(4)]):
  assert im.size==(1280,1280),im.size;out.paste(im,(x*1280,y*1280))
out.save(root/'map.jpg',quality=91,optimize=True);print('HD map assembled',out.size,(root/'map.jpg').stat().st_size)

from PIL import ImageDraw,ImageFont
icon=Image.new('RGBA',(256,256),(0,0,0,0));draw=ImageDraw.Draw(icon)
draw.rounded_rectangle((3,3,253,253),radius=48,fill='#e64046')
font=ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',125)
box=draw.textbbox((0,0),'SH',font=font)
draw.text(((256-(box[2]-box[0]))/2,(256-(box[3]-box[1]))/2-box[1]),'SH',font=font,fill='#101517')
icon.save(root/'SH.ico',sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
