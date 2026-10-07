import json, struct, pathlib
names=['zoe','aatrox','akshan','brand','pantheon','yunara']
result={}
for name in names:
    p=pathlib.Path('C:/Users/Kakad/Desktop')/(name+'.glb')
    b=p.read_bytes(); n=struct.unpack_from('<I',b,12)[0]; g=json.loads(b[20:20+n])
    meshes=[]
    for m in g.get('meshes',[]):
        meshes.append({'name':m.get('name'),'primitives':[{'material':x.get('material'),'vertices':g['accessors'][x['attributes']['POSITION']]['count'],'min':g['accessors'][x['attributes']['POSITION']].get('min'),'max':g['accessors'][x['attributes']['POSITION']].get('max'),'skin': 'JOINTS_0' in x['attributes']} for x in m['primitives']]})
    result[name]={'nodes':[(i,x.get('name'),x.get('mesh'),x.get('skin')) for i,x in enumerate(g.get('nodes',[]))], 'skins':[{'joints':[g['nodes'][i].get('name',str(i)) for i in s['joints']]} for s in g.get('skins',[])], 'animations':[(x.get('name'),len(x['channels'])) for x in g.get('animations',[])], 'meshes':meshes,'materials':g.get('materials',[]),'images':g.get('images',[])}
pathlib.Path('C:/Users/Kakad/AppData/Local/Temp/LeagueChampions/asset-inspection.json').write_text(json.dumps(result,indent=2))
for name,r in result.items():
    print(name, 'nodes',len(r['nodes']),'skins',len(r['skins']),'animations',r['animations'],'images',len(r['images']))
    print('meshes',r['meshes'])
    print('materials',[(x.get('name'),x.get('pbrMetallicRoughness',{}).get('baseColorTexture')) for x in r['materials']])
    print('bones', [s['joints'] for s in r['skins']])
