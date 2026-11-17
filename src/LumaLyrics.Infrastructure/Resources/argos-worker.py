"""Argos package/tokenizer + CTranslate2; one original lyric group per translation unit."""
import sys, os, json, socket, gc

def deny_network(*args, **kwargs):
    raise RuntimeError('NETWORK_DISABLED')
socket.socket.connect = deny_network
socket.socket.connect_ex = deny_network
socket.socket.sendto = deny_network
socket.create_connection = deny_network
socket.getaddrinfo = deny_network
os.environ['ARGOS_CHUNK_TYPE']='NONE'
os.environ['ARGOS_DEVICE_TYPE']='cpu'
os.environ['OMP_NUM_THREADS']='2'
os.environ['MKL_NUM_THREADS']='2'
from pathlib import Path
from argostranslate.package import Package
import ctranslate2

def load_package(path, source, target):
    package=Package(next(Path(path).rglob('metadata.json')).parent)
    if package.from_code!=source or package.to_code!=target:
        raise RuntimeError('UNSUPPORTED_LANGUAGE_PACKAGE')
    return package

def translator(package):
    return ctranslate2.Translator(str(package.package_path/'model'),device='cpu',compute_type='int8',inter_threads=1,intra_threads=2)

chinese_package=load_package(sys.argv[1],'en','zh')
chinese_engine=translator(chinese_package)
source_paths=json.loads(sys.argv[2]) if len(sys.argv)>2 else {}
source_package=None
source_engine=None
loaded_source=None
cache={}

def translate_group(package, engine, text):
    tokens=package.tokenizer.encode(text)
    if len(tokens)>150:raise ValueError('GROUP_TOO_LONG')
    prefix=[[package.target_prefix]] if package.target_prefix else None
    result=engine.translate_batch([tokens],target_prefix=prefix,beam_size=2,num_hypotheses=1,length_penalty=0.2,max_input_length=0,max_decoding_length=300)
    translated=package.tokenizer.decode(result[0].hypotheses[0])
    if package.target_prefix and translated.startswith(package.target_prefix):translated=translated[len(package.target_prefix):]
    if not translated.strip() or len(translated)>4000:raise ValueError('INVALID_OUTPUT')
    return translated.strip()

for message in sys.stdin:
    try:
        request=json.loads(message)
        if request.get('Ping'):
            blocked=False
            try:socket.create_connection(('127.0.0.1',9),timeout=0.1)
            except RuntimeError as error:blocked=str(error)=='NETWORK_DISABLED'
            response={'Ready':True,'NetworkDisabled':blocked,'Engine':'Argos Translate 1.11.0 / CTranslate2 4.6.0','LoadedModels':1+(source_engine is not None)}
        else:
            source=request.get('SourceLanguage','en')
            if source not in ('en','fr','de','es','ja','ko'):raise ValueError('UNSUPPORTED_SOURCE')
            if source!=loaded_source:
                source_engine=None;source_package=None;gc.collect()
                if source!='en':
                    source_package=load_package(source_paths[source],source,'en')
                    source_engine=translator(source_package)
                loaded_source=source
            lines=[]
            for text in request['Texts']:
                if not isinstance(text,str) or len(text)>4000:raise ValueError('INVALID_INPUT')
                key=(source,text)
                if key not in cache:
                    english=text if source=='en' else translate_group(source_package,source_engine,text)
                    translated=translate_group(chinese_package,chinese_engine,english)
                    if len(cache)>=512:cache.clear()
                    cache[key]=translated
                lines.append(cache[key])
            response={'Texts':lines}
        print(json.dumps(response,ensure_ascii=False),flush=True)
    except Exception:
        # Never echo originals, intermediate lyrics, paths or raw model errors to logs.
        print(json.dumps({'Error':'TRANSLATION_FAILED'}),flush=True)
