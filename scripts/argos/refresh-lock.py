"""Refresh public lock metadata from downloaded Windows CPython 3.12 wheels; never include wheels/models in Git."""
import hashlib,json,pathlib,urllib.request,zipfile,email
base=pathlib.Path(__file__).resolve().parents[2]
items=[]
for p in sorted((base/'.tools/argos-wheels').glob('*.whl')):
 with zipfile.ZipFile(p) as z:
  meta=email.message_from_bytes(z.read(next(n for n in z.namelist() if n.endswith('.dist-info/METADATA'))))
 info=json.load(urllib.request.urlopen(f"https://pypi.org/pypi/{meta['Name']}/{meta['Version']}/json"))
 remote=next(x for x in info['urls'] if x['filename']==p.name)
 digest=hashlib.sha256(p.read_bytes()).hexdigest()
 if digest!=remote['digests']['sha256']:raise ValueError('PyPI digest mismatch')
 items.append(dict(Name=p.name,Url=remote['url'],Sha256=digest,Size=p.stat().st_size,Target='site'))
for name,url,target in [('python-3.12.10-embed-amd64.zip','https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip','runtime'),('translate-en_zh-1_9.argosmodel','https://argos-net.com/v1/translate-en_zh-1_9.argosmodel','model')]:
 p=base/'.tools/argos-downloads'/name
 items.append(dict(Name=name,Url=url,Sha256=hashlib.sha256(p.read_bytes()).hexdigest(),Size=p.stat().st_size,Target=target))
output=dict(Version='argos-1.11.0-ct2-4.6.0-py312-win64-v2',Components=items)
(base/'src/LumaLyrics.Infrastructure/Resources/argos-engine-lock.json').write_text(json.dumps(output,indent=2)+'\n',encoding='utf-8')
print('Components:',len(items),'Download MB:',round(sum(x['Size'] for x in items)/1e6,1))
