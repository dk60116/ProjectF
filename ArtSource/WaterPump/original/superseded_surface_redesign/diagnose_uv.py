from pathlib import Path
import json
text=Path(__file__).with_name('repack_uv.py').read_text().split('# Verify the mapping')[0]
text=text.replace("for name,position in views:render('before_'+name,position)","pass")
text=text.replace("margin_method='FRACTION',margin=.006","margin_method='SCALED',margin=.003")
exec(compile(text,__file__,'exec'))
current=np.array([u.uv[:] for u in mesh.uv_layers['OriginalUV'].data])
print('ORIGINAL UV DIFFERENCE',float(np.abs(current-old_uv).max()))
changed=[i for i,p in enumerate(mesh.polygons) if list(p.vertices)!=faces[i]]
print('FACE ORDER CHANGES',len(changed),[(i,list(mesh.polygons[i].vertices),faces[i]) for i in changed[:4]])
print('PACKED RANGE',packed_uv.min(),packed_uv.max())
np.savez(work/'uv_diagnostic.npz',old=old_uv,current=current,packed=packed_uv,vi=np.array([l.vertex_index for l in mesh.loops]))
tail=Path(__file__).with_name('repack_uv.py').read_text().split('# Verify the mapping')[1].split('atlas =')[0]
tail=tail[tail.index('parent ='):].replace('assert max_error<2e-5, max_error',"print('ERROR',max_error,'ISLANDS',len(islands),'SCALES',min(scales),max(scales))")
exec(tail)
