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

# Extra source packages are independent of the stable English runtime lock.
extra=[]
for code,version in [('fr','1_9'),('de','1_3'),('es','1_9'),('ja','1_1'),('ko','1_1')]:
 name=f'translate-{code}_en-{version}.argosmodel'
 p=base/'.tools/argos-downloads'/name
 with zipfile.ZipFile(p) as z:
  metadata=json.loads(z.read(next(n for n in z.namelist() if n.endswith('/metadata.json'))))
  if metadata['from_code']!=code or metadata['to_code']!='en':raise ValueError('Unexpected source package direction')
 extra.append(dict(Name=name,Url='https://argos-net.com/v1/'+name,Sha256=hashlib.sha256(p.read_bytes()).hexdigest(),Size=p.stat().st_size,Target='model'))
(base/'src/LumaLyrics.Infrastructure/Resources/argos-model-lock.json').write_text(json.dumps(dict(Version='argos-source-models-v1',Components=extra),indent=2)+'\n',encoding='utf-8')
print('Additional source model MB:',round(sum(x['Size'] for x in extra)/1e6,1))
