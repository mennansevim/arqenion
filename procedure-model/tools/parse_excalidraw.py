import json, re, sys
SRC = sys.argv[1]
d = json.load(open(SRC))
blocks = [e for e in d['elements'] if e['type']=='text' and 'Doğru scrub uygulaması' in e['text']]

def bullets(txt):
    out=[]
    for line in txt.split('\n'):
        s=line.strip()
        if not s: continue
        s=re.sub(r'^(\d+\.|•|○|-)\s*','',s).strip()
        if out and not re.match(r'^(\d+\.|•|○|-)', line.strip()) and not line.startswith(' ') and out[-1] and not out[-1].endswith(('.',':')):
            out[-1]+=' '+s
        else:
            out.append(s)
    return [o.rstrip() for o in out if o]

def para(txt):
    return re.sub(r'\s*\n\s*',' ',txt.strip())

SEC = ['Scrubın yaptığı yanlışlık','Olası komplikasyon','Komplikasyon belirtileri',
       'Scrubın sıralı çözüm yaklaşımı','Vakanın devamı','Yönetim ve organizasyona etkisi']
KEY = {'Scrubın yaptığı yanlışlık':'scrubError','Olası komplikasyon':'possibleComplications',
       'Komplikasyon belirtileri':'signs','Scrubın sıralı çözüm yaklaşımı':'resolutionSteps',
       'Vakanın devamı':'caseContinuation','Yönetim ve organizasyona etkisi':'organizationalImpact'}

def parse_scenario(title, body):
    sc={'title':title}
    pat='^('+'|'.join(re.escape(s) for s in SEC)+r')\s*$'
    parts=re.split(pat, body, flags=re.M)
    for i in range(1,len(parts),2):
        k=KEY[parts[i]]; v=parts[i+1]
        if k in ('scrubError','caseContinuation'): sc[k]=para(v)
        elif k=='signs':
            # may contain sub-headers "Ameliyat sırasında:" / "Ameliyat sonrasında ...:"
            sub=re.split(r'^(Ameliyat sırasında:|Ameliyat sonrasında[^\n]*:)\s*$', v, flags=re.M)
            if len(sub)>1:
                sc['signs']={}
                for j in range(1,len(sub),2):
                    key='intraoperative' if 'sırasında' in sub[j] else 'postoperative'
                    sc['signs'][key]=bullets(sub[j+1])
            else: sc['signs']=bullets(v)
        else: sc[k]=bullets(v)
    return sc

res={}
for e in blocks:
    t=e['text'].strip('\n')
    m=re.match(r'\s*Adım\s+(\d+)\s*[–-]\s*(.+)', t)
    if m: n=int(m.group(1)); title=m.group(2).strip()
    else: n=1; title='McBurney/Elliot cilt insizyonu'
    body=t.split('Doğru scrub uygulaması',1)[1]
    sp=re.split(r'^Hata senaryosu\s*(\d+)\s*[–-]\s*(.+)$', body, flags=re.M)
    correct=sp[0].strip()
    # correct practice may contain a "Bu içerik denetimli klinik simülasyon içindir" disclaimer paragraph
    scen=[]
    for i in range(1,len(sp),3):
        s=parse_scenario(sp[i+1].strip(), sp[i+2]); s['id']=f'S{n:02d}-E{int(sp[i])}'; scen.append(s)
    if n in res: print('DUP',n, file=sys.stderr)
    res[n]={'detailTitle':title,'correctScrubPractice':para(correct),'errorScenarios':scen}
print('steps with detail:',sorted(res), file=sys.stderr)
print('missing:',[i for i in range(1,61) if i not in res], file=sys.stderr)
for n,v in sorted(res.items()):
    miss=[s['id']+':'+','.join(k for k in KEY.values() if k not in s) for s in v['errorScenarios'] if any(k not in s for k in KEY.values())]
    print(n,len(v['errorScenarios']),miss, file=sys.stderr)
json.dump(res, open(sys.argv[2],'w'), ensure_ascii=False, indent=1)
