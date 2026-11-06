"""Local-only synthetic WebSocket fault injector for Windows CI; no accounts or real positions."""
import asyncio,json,time
from urllib.parse import urlparse,parse_qs
from websockets.asyncio.server import serve
clients={}
def stalled(entry):return entry['drop'] and time.monotonic()-entry['started']>=1
async def frame(entry):
 if stalled(entry):return
 peers=[dict(id=x['id'],nick=x['id'],**x['position']) for x in clients.values() if x.get('position')]
 await entry['ws'].send(json.dumps(dict(self=entry['id'],protocol=3,ack=entry['ack'],now=int(time.time()*1000),peers=peers)))
async def handler(ws):
 q=parse_qs(urlparse(ws.request.path).query);identity=q.get('id',['unknown'])[0]
 entry=dict(id=identity,ws=ws,drop=q.get('drop',['0'])[0]=='1',started=time.monotonic(),ack=0,rejected_final=False)
 clients[identity]=entry
 try:
  await frame(entry)
  await ws.send(json.dumps(dict(diagnostic=True,connections=[],padding='x'*(70000 if entry['drop'] else 300000))))
  async for text in ws:
   message=json.loads(text)
   if stalled(entry):continue
   if message.get('ping'):await frame(entry);continue
   if message.get('diagnostic'):
    await ws.send(json.dumps(dict(diagnostic=True,ack=entry['ack'],connections=[dict(nick=x['id'],received=x['ack'],active=True) for x in clients.values()])));continue
   if 'seq' in message:
    seq=message['seq']
    if seq==220 and not entry['rejected_final']:
     entry['rejected_final']=True;await frame(entry);continue
    entry['ack']=seq
    entry['position']=dict(x=message['x'],y=message['y'],z=message['z'],heading=message.get('heading'),at=int(time.time()*1000-message.get('age',0)))
    for target in list(clients.values()):
     try:await frame(target)
     except Exception:pass
 finally:
  if clients.get(identity) is entry:clients.pop(identity,None)
async def main():
 async with serve(handler,'127.0.0.1',8769,max_size=65536):
  print('READY',flush=True);await asyncio.Future()
if __name__=='__main__':asyncio.run(main())
