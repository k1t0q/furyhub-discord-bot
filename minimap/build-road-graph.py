"""Compile factual road polylines into SCUM HUB's offline routing graph.
Source: HellBz/SCUM-Walker, src/roads.json (project README: MIT).
No synthetic long-distance links: only merge junction gaps <= 6 metres.
"""
import json, math, struct, collections, urllib.request, base64, hashlib
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.spatial import cKDTree

ROOT=Path(__file__).resolve().parent
TARGET=ROOT if (ROOT/'PositionProbe.cs').exists() else ROOT.parent/'scum-minimap'
source=ROOT/'source-roads.json'
if not source.exists():
    with urllib.request.urlopen('https://api.github.com/repos/HellBz/SCUM-Walker/git/blobs/e915011c30fc0d1d0d4cc60dda056de1671edef1',timeout=60) as response:
        source.write_bytes(base64.b64decode(json.load(response)['content']))
raw=source.read_bytes()
if hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest()!='e915011c30fc0d1d0d4cc60dda056de1671edef1':
    raise ValueError('Road source does not match the pinned revision')
data=json.loads(raw)
width,height=data['image']['width'],data['image']['height']
SX,SY=15250.158839,15240.172528
roads=[r for net in data['networks'] for r in net['roads'] if r['type'] in ('primary','secondary')]
points=[]; lookup={}; segments=[]; endpoints=[]
def node(x,y):
    key=(round(x,5),round(y,5))
    if key not in lookup:
        lookup[key]=len(points);points.append((x,y))
    return lookup[key]
for road in roads:
    ns=[node(x/width*SX,y/height*SY) for x,y in road['points']]
    if len(ns)<2:continue
    endpoints.extend([ns[0],ns[-1]])
    segments.extend((a,b,road['type']=='secondary') for a,b in zip(ns,ns[1:]) if a!=b)
samples=[]; owners=[]
for i,(a,b,_) in enumerate(segments):
    pa,pb=np.array(points[a]),np.array(points[b]); length=np.linalg.norm(pb-pa)
    for t in np.linspace(0,1,max(2,math.ceil(length/6)+1)):
        samples.append(pa+(pb-pa)*t);owners.append(i)
tree=cKDTree(samples);cuts=collections.defaultdict(list)
for endpoint in set(endpoints):
    p=np.array(points[endpoint]);_,near=tree.query(p,k=24)
    best=None
    for i in set(owners[int(j)] for j in near):
        a,b,secondary=segments[i]
        if endpoint in (a,b):continue
        pa,pb=np.array(points[a]),np.array(points[b]);delta=pb-pa
        t=float(np.dot(p-pa,delta)/np.dot(delta,delta));t=max(0,min(1,t))
        q=pa+delta*t;distance=float(np.linalg.norm(q-p))
        if distance<=6 and (best is None or distance<best[0]):best=(distance,i,t,q)
    if best:
        _,i,t,q=best; join=node(*q);cuts[i].append((t,join));
        # A short junction connector, not a shortcut between distant roads.
        if join!=endpoint:segments.append((endpoint,join,False))
parent=list(range(len(points)))
def find(a):
    while parent[a]!=a:parent[a]=parent[parent[a]];a=parent[a]
    return a
def union(a,b):
    a,b=find(a),find(b)
    if a!=b:parent[b]=a
for a,b in cKDTree(points).query_pairs(2.5):union(a,b)
edges={}
for i,(a,b,secondary) in enumerate(segments):
    chain=[(0,a)]+cuts[i]+[(1,b)];chain.sort()
    for (_,aa),(_,bb) in zip(chain,chain[1:]):
        aa,bb=find(aa),find(bb)
        if aa==bb:continue
        key=tuple(sorted((aa,bb)));edges[key]=min(edges.get(key,1),int(secondary))
adj=collections.defaultdict(list)
for a,b in edges:adj[a].append(b);adj[b].append(a)
seen=set();components=[]
for n in adj:
    if n in seen:continue
    todo=[n];seen.add(n);component=[]
    while todo:
        a=todo.pop();component.append(a)
        for b in adj[a]:
            if b not in seen:seen.add(b);todo.append(b)
    components.append(component)
keep=set()
for comp in components:
    total=sum(math.dist(points[a],points[b]) for a in comp for b in adj[a])/2
    if total>=100:keep.update(comp)
ids={old:i for i,old in enumerate(sorted(keep))};valid=[(a,b,k) for (a,b),k in edges.items() if a in ids and b in ids]
out=TARGET/'roads.bin'
with out.open('wb') as f:
    f.write(b'SHRN1');f.write(struct.pack('<ii',len(ids),len(valid)))
    for old in sorted(keep):f.write(struct.pack('<ff',points[old][0]/SX,points[old][1]/SY))
    for a,b,k in valid:f.write(struct.pack('<iiB',ids[a],ids[b],k))
    # Water is used only to validate off-road approaches, never to cut roads.
    rgb=np.array(Image.open(TARGET/'map.jpg').resize((1024,1024)),dtype=float)
    r,g,b=rgb[:,:,0],rgb[:,:,1],rgb[:,:,2]
    water=(b>r*1.22)&(g>r*1.08)&(b>g*1.02)
    bits=np.packbits(water.reshape(-1),bitorder='little').tobytes()
    f.write(b'WATR');f.write(struct.pack('<iii',1024,1024,len(bits)));f.write(bits)
print(json.dumps({'source_roads':len(roads),'nodes':len(ids),'edges':len(valid),'junction_connectors':sum(len(c) for c in cuts.values()),'largest_components':sorted([len(c) for c in components],reverse=True)[:12],'bytes':out.stat().st_size}))
