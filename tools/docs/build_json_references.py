"""Build reviewable JSON references from GH_IO XML exports of the shipped .gh files.

Usage: python tools/docs/build_json_references.py <directory containing XML exports>
No definition is executed or modified. Schemas and a reading guide live in reference/.
"""
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'docs/user-guide'
XML = Path(sys.argv[1])

def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf8')

def item(node, name, default=None):
    if node is None: return default
    found = node.find(f'items/item[@name="{name}"]')
    return (found.text if found.text is not None else default) if found is not None else default

def child(node, name):
    return node.find(f'chunks/chunk[@name="{name}"]')

def state(node, omissions, path):
    """Keep typed archive values; omit binary payloads, never silently discard them."""
    result = {'name': node.get('name', node.tag), 'items': [], 'chunks': []}
    if 'index' in node.attrib: result['index'] = int(node.get('index'))
    for entry in node.findall('items/item'):
        value = {'name': entry.get('name'), 'type': entry.get('type_name')}
        if 'index' in entry.attrib: value['index'] = int(entry.get('index'))
        if entry.get('type_name') in ('gh_bytearray', 'gh_drawing_bitmap'):
            payload = ET.tostring(entry, encoding='utf8')
            value['omitted'] = {'reason': 'Binary payload retained in the original .gh file',
                                'archiveXmlSha256': hashlib.sha256(payload).hexdigest()}
            omissions.append(path+'/'+entry.get('name'))
        elif len(entry):
            # Complex primitives (points, planes, intervals, versions) retain their named XML fields.
            value['xmlValue'] = ET.tostring(entry, encoding='unicode').strip()
        else:
            value['value'] = entry.text or ''
        result['items'].append(value)
    for part in node.findall('chunks/chunk'):
        if part.get('name') == 'Attributes': continue  # canvas layout is captured separately
        result['chunks'].append(state(part, omissions, path+'/'+part.get('name')))
    return result

def ports(container, direction):
    names = ('param_input', 'InputParam') if direction == 'inputs' else ('param_output', 'OutputParam')
    return [c for c in container.findall('.//chunk') if c.get('name') in names]

stats=[]
for page in sorted((ROOT/'examples').glob('*.md')):
    if page.name == 'README.md': continue
    text=page.read_text(encoding='utf8')
    gh_rel=re.search(r'\]\((files/[^)]+\.gh)\)',text)[1]
    gh=page.parent/gh_rel
    definition=child(ET.parse(XML/(gh.stem+'.xml')).getroot(),'Definition')
    objects=child(definition,'DefinitionObjects').findall('chunks/chunk')
    nodes=[]; edges=[]; endpoints={}; omissions=[]
    for obj in objects:
        container=child(obj,'Container'); guid=item(container,'InstanceGuid')
        assert guid, (page.name, 'Missing instance ID')
        node={'id':guid,'componentGuid':item(obj,'GUID'),'name':item(container,'Name',item(obj,'Name')),
              'nickname':item(container,'NickName',''),'libraryId':item(obj,'Lib'),
              'inputs':[],'outputs':[]}
        endpoints[guid]={'nodeId':guid,'parameterId':guid,'portIndex':None}
        for direction in ('inputs','outputs'):
            for port in ports(container,direction):
                pid=item(port,'InstanceGuid'); index=int(port.get('index'))
                assert pid
                node[direction].append({'id':pid,'index':index,'name':item(port,'Name',''),
                    'nickname':item(port,'NickName',''),'savedState':state(port,omissions,guid+'/'+pid)})
                endpoints[pid]={'nodeId':guid,'parameterId':pid,'portIndex':index}
        # Keep control settings, persistent trees, expressions, group membership and custom state.
        copied=ET.fromstring(ET.tostring(container))
        for chunk_parent in copied.findall('.//chunks'):
            for part in list(chunk_parent):
                if part.get('name') in ('param_input','param_output','InputParam','OutputParam','ParameterData'):
                    chunk_parent.remove(part)
        node['savedState']=state(copied,omissions,guid)
        attributes=child(container,'Attributes')
        if attributes is not None:
            node['canvas']=state(attributes,omissions,guid+'/Attributes')
        node['groupMembers']=[i.text for i in container.findall('items/item[@name="ID"]')] if node['name']=='Group' else []
        nodes.append(node)
    # Resolve every Source entry, including wires into standalone parameters, in saved source order.
    for obj in objects:
        container=child(obj,'Container')
        for dest in [container]+ports(container,'inputs'):
            sources=dest.findall('items/item[@name="Source"]')
            assert len(sources)==int(item(dest,'SourceCount','0'))
            for source in sources:
                assert source.text in endpoints, (page.name,'Unresolved source',source.text)
                edges.append({'from':endpoints[source.text], 'to':endpoints[item(dest,'InstanceGuid')],
                              'sourceOrder':int(source.get('index','0'))})
    assert len({n['id'] for n in nodes})==len(objects)
    assert len(edges)==len(definition.findall('.//item[@name="Source"]'))
    unresolved_groups=[{'groupId':n['id'],'memberId':member} for n in nodes for member in n['groupMembers'] if member not in endpoints]
    libraries=child(definition,'GHALibraries')
    data={'$schema':'../../reference/definition.schema.json','schemaVersion':1,
          'title':text.splitlines()[0].removeprefix('# '),
          'source':{'file':'../'+gh_rel,'sha256':hashlib.sha256(gh.read_bytes()).hexdigest(),
                    'documentId':item(child(definition,'DocumentHeader'),'DocumentID')},
          'extraction':{'method':'GH_IO archive read without loading components or solving',
                        'coverage':'All saved top-level objects and Source wires; not an executable replacement for the .gh file',
                        'limitations':['Binary geometry and image payloads are omitted and identified by archive XML hashes.',
                                       'External files and Rhino document geometry are not bundled. Inspect saved paths and references.',
                                       'Runtime outputs, nested cluster internals and plugin-specific behavior are not reconstructed.',
                                       'Saved library versions describe the archive, not a guarantee of compatibility with newer components.'],
                        'omittedBinaryPaths':omissions,'unresolvedGroupMembers':unresolved_groups},
          'libraries':[state(c,omissions,'Library') for c in libraries.findall('chunks/chunk')] if libraries is not None else [],
          'nodes':nodes,'connections':edges}
    dest=ROOT/'examples/data'/f'{page.stem}.json';write_json(dest,data)
    block='\n\n<details>\n<summary>Grasshopper definition (JSON)</summary>\n\n'
    block+=f'[Download JSON](data/{page.stem}.json) · [JSON Schema](../reference/definition.schema.json) · [How to read this JSON](../reference/reading-json.md)\n\n'
    block+='This records the saved definition. Geometry and other binary payloads remain in the original `.gh` file.\n\n```json\n'
    # Compact individual records for a manageable code block while downloads remain pretty-printed.
    display=[]
    for key,value in data.items():
        if key in ('nodes','connections'):
            encoded='[\n'+',\n'.join('    '+json.dumps(v,ensure_ascii=False,separators=(',',':')) for v in value)+'\n  ]'
        else: encoded=json.dumps(value,ensure_ascii=False,separators=(',',':'))
        display.append('  '+json.dumps(key)+': '+encoded)
    block+='{\n'+',\n'.join(display)+'\n}\n```\n\n</details>\n'
    text=re.sub(r'\n*<details>\n<summary>Grasshopper definition \(JSON\)</summary>[\s\S]*?</details>\s*$', '',text)
    page.write_text(text.rstrip()+block,encoding='utf8')
    stats.append({'example':page.stem,'nodes':len(nodes),'connections':len(edges),'omittedBinaryPayloads':len(omissions)})

for page in sorted((ROOT/'components').glob('*.md')):
    text=page.read_text(encoding='utf8')
    match=re.search(r'```json\n([\s\S]*?)\n```',text)
    if not match: continue
    data=json.loads(match[1]);data={'$schema':'../component.schema.json',**data}
    write_json(ROOT/'reference/components'/f'{page.stem}.json',data)
    text=text[:match.start(1)]+json.dumps(data,ensure_ascii=False,indent=2)+text[match.end(1):]
    marker='<summary>Machine-readable reference (JSON)</summary>'
    links=f'\n\n[Download JSON](../reference/components/{page.stem}.json) · [JSON Schema](../reference/component.schema.json) · [How to read this JSON](../reference/reading-json.md)'
    if '[How to read this JSON]' not in text: text=text.replace(marker,marker+links)
    page.write_text(text,encoding='utf8')
print(json.dumps(stats,indent=2))
