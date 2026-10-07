import bpy,math,json
from mathutils import Vector
from pathlib import Path
out=Path(__file__).parent/'Models';out.mkdir(exist_ok=True)
def material(n):return bpy.data.materials.get(n) or bpy.data.materials.new(n)
def finish(o,n,smooth=True):
 o.data.materials.append(material(n))
 if smooth:
  for p in o.data.polygons:p.use_smooth=True
 return o
def sphere(loc,scale,n):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=loc);o=bpy.context.object;o.scale=scale;return finish(o,n)
def cylinder(loc,r,d,n,verts=24):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=d,location=loc);return finish(bpy.context.object,n,False)
def torus(loc,r,d,n,rot=(0,0,0),scale=(1,1,1)):
 bpy.ops.mesh.primitive_torus_add(major_segments=32,minor_segments=8,location=loc,major_radius=r,minor_radius=d);o=bpy.context.object;o.rotation_euler=rot;o.scale=scale;return finish(o,n)
def cube(loc,scale,n):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);b=o.modifiers.new('Soft edges','BEVEL');b.width=.004;b.segments=2;return finish(o,n,False)
def lathe(profile,n,segments=12):
 vertices=[(r*math.cos(i*2*math.pi/segments),r*math.sin(i*2*math.pi/segments),z) for r,z in profile for i in range(segments)]
 faces=[]
 for row in range(len(profile)-1):
  for i in range(segments):faces.append((row*segments+i,row*segments+(i+1)%segments,(row+1)*segments+(i+1)%segments,(row+1)*segments+i))
 faces += [tuple(reversed(range(segments))),tuple((len(profile)-1)*segments+i for i in range(segments))]
 mesh=bpy.data.meshes.new(n);mesh.from_pydata(vertices,[],faces);mesh.update();o=bpy.data.objects.new(n,mesh);bpy.context.collection.objects.link(o);finish(o,n,False)
 bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project();bpy.ops.object.mode_set(mode='OBJECT');o.select_set(False);return o
def export(name):
 deps=bpy.context.evaluated_depsgraph_get();data=[]
 for o in bpy.data.objects:
  if o.type!='MESH':continue
  e=o.evaluated_get(deps);m=e.to_mesh();m.calc_loop_triangles()
  for t in m.loop_triangles:
   group=o.data.materials[t.material_index].name;face=[]
   for i in t.loops:
    co=o.matrix_world@m.vertices[m.loops[i].vertex_index].co;no=(o.matrix_world.to_3x3()@m.loops[i].normal).normalized();uv=m.uv_layers.active.data[i].uv if m.uv_layers.active else Vector((co.x*5,co.z*5));face.append((co,no,uv))
   data.append((group,face))
  e.to_mesh_clear()
 groups=sorted(set(g for g,f in data));lines=['# Icon-inspired League potion. Y up, metres, grip centred.'];idx=1
 for group in groups:
  lines+=['g '+group,'usemtl '+group]
  for g,face in data:
   if g!=group:continue
   for c,n,u in face:
    lines+=['v %.7f %.7f %.7f'%(c.x,c.z,-c.y),'vt %.7f %.7f'%tuple(u),'vn %.7f %.7f %.7f'%(n.x,n.z,-n.y)]
   lines.append('f '+' '.join(f'{v}/{v}/{v}' for v in range(idx,idx+3)));idx+=3
 (out/(str(name)+'.obj')).write_text('\n'.join(lines));return {'id':name,'groups':groups,'triangles':len(data)}
manifest=[]
for id in [2003,2031,2138,2139,2140]:
 for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
 if id==2003:
  sphere((0,0,-.012),(.060,.047,.068),'Glass');sphere((0,0,-.024),(.054,.042,.053),'Liquid')
  torus((0,0,-.004),.057,.010,'Leather',(0,math.radians(-23),0),(1,.80,1))
  cylinder((-.015,0,.064),.024,.044,'Glass');cylinder((-.015,0,.088),.020,.020,'Cork');torus((-.015,0,.079),.024,.003,'Silver')
  cube((-.034,-.041,.014),(.021,.008,.026),'Silver');cube((-.034,-.046,.014),(.011,.003,.015),'Leather')
  tip=(-.015,.102,0)
 elif id==2031:
  lathe([(.022,-.080),(.049,-.060),(.055,.013),(.038,.054),(.022,.065),(.022,.094)],'Glass',8)
  lathe([(.020,-.074),(.044,-.056),(.049,.016),(.033,.040)],'Liquid',8)
  cylinder((0,0,.097),.020,.018,'Cork',8);torus((0,0,.088),.024,.0035,'Gold');torus((0,0,.067),.024,.002,'Gold');tip=(0,.109,0)
 else:
  # The three elixirs share a square armoured vial, matching their original icons.
  cube((0,0,-.006),(.092,.070,.120),'Glass');cube((0,0,-.016),(.078,.058,.091),'Liquid')
  for x in [-.045,.045]:
   for y in [-.034,.034]:cube((x,y,-.006),(.008,.008,.125),'Iron' if id==2140 else 'Gold')
  cube((0,0,-.067),(.105,.081,.013),'Iron' if id==2140 else 'Gold');cube((0,0,.055),(.105,.081,.014),'Iron' if id==2140 else 'Gold')
  cylinder((0,0,.080),.027,.040,'Glass',8);cylinder((0,0,.104),.030,.012,'Iron' if id==2140 else 'Gold',8);cylinder((0,0,.112),.020,.013,'Cork',8)
  sphere((0,-.041,-.008),(.016,.007,.022),'Gem');torus((0,-.043,-.008),.022,.002,'Silver',(math.pi/2,0,0),(.75,1,1));tip=(0,.12,0)
 m=export(id);m['tip']=tip;manifest.append(m)
(out/'manifest.json').write_text(json.dumps({'models':manifest},indent=2))
print('Five distinct potion models exported',manifest)
