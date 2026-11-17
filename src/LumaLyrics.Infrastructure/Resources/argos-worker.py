"""Argos package/tokenizer + its CTranslate2 backend; timed lyric groups need no automatic sentence splitter."""
import sys, os, json, socket
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
package = Package(next(Path(sys.argv[1]).rglob('metadata.json')).parent)
if package.from_code != 'en' or package.to_code != 'zh':
    raise RuntimeError('UNSUPPORTED_LANGUAGE_PACKAGE')
engine=ctranslate2.Translator(str(package.package_path/'model'),device='cpu',compute_type='int8',inter_threads=1,intra_threads=2)
cache={}
for message in sys.stdin:
    try:
        request=json.loads(message)
        if request.get('Ping'):
            blocked=False
            try:socket.create_connection(('127.0.0.1',9),timeout=0.1)
            except RuntimeError as error:blocked=str(error)=='NETWORK_DISABLED'
            response={'Ready':True,'NetworkDisabled':blocked,'Engine':'Argos Translate 1.11.0 / CTranslate2 4.6.0'}
        else:
            lines=[]
            for text in request['Texts']:
                if not isinstance(text,str) or len(text)>4000:raise ValueError('INVALID_INPUT')
                if text not in cache:
                    tokens=package.tokenizer.encode(text)
                    if len(tokens)>150:raise ValueError('GROUP_TOO_LONG')
                    prefix=[[package.target_prefix]] if package.target_prefix else None
                    result=engine.translate_batch([tokens],target_prefix=prefix,beam_size=2,num_hypotheses=1,length_penalty=0.2,max_input_length=0,max_decoding_length=300)
                    translated=package.tokenizer.decode(result[0].hypotheses[0])
                    if package.target_prefix and translated.startswith(package.target_prefix):translated=translated[len(package.target_prefix):]
                    if not translated.strip():raise ValueError('EMPTY_OUTPUT')
                    if len(cache)>=512:cache.clear()
                    cache[text]=translated.strip()
                lines.append(cache[text])
            response={'Texts':lines}
        print(json.dumps(response,ensure_ascii=False),flush=True)
    except Exception:
        # Do not echo lyric text, paths or raw model errors to shared logs.
        print(json.dumps({'Error':'TRANSLATION_FAILED'}),flush=True)
