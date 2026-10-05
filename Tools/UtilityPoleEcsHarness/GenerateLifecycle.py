from pathlib import Path
import sys
root=Path(__file__).resolve().parents[2]/'FactorioProject/Assets/Scripts'
out=Path(sys.argv[1])
def member(path,signature):
 text=(root/path).read_text(encoding='utf-8-sig');start=text.index(signature);end=text.index('{',start)+1;depth=1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
world=(root/'Map/UtilityPoleWorld.cs').read_text(encoding='utf-8-sig')
(out/'World.cs').write_text('using SphereCollider = PoleSphereBoundary;\n'+world,encoding='utf-8')
point=member('Map/UtilityPoleRuntime.Identity.cs','internal sealed class UtilityPoleLinePoint')
(out/'Point.cs').write_text('using UnityEngine; namespace ProjectF.Power { '+point+' }',encoding='utf-8')
wire=member('Rendering/UtilityPoleWireRenderer.cs','internal sealed class UtilityPoleWire')
(out/'Wire.cs').write_text('using UnityEngine; namespace ProjectF.Rendering { '+wire+' }',encoding='utf-8')
traversal=member('Rendering/CameraRenderCulling.cs','internal struct SpatialRayCellTraversal')
(out/'Traversal.cs').write_text('using UnityEngine; namespace ProjectF.Rendering { '+traversal+' }',encoding='utf-8')
