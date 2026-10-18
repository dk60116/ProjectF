const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const YAML = require('../PlayerAnimationHarness/node_modules/yaml');

const root = path.resolve(__dirname, '../..');
const assetPath = path.join(root, 'FactorioProject/Assets/MapObject/Wall/Brick Wall/Brick Wall_Corner.asset');
const dot = (a,b) => a.reduce((sum,value,i) => sum+value*b[i],0);
const sub = (a,b) => a.map((value,i) => value-b[i]);
const cross = (a,b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const unit = v => v.map(value => value/Math.hypot(...v));
const floats = (buffer,offset,count) => Array.from({length:count},(_,i) => buffer.readFloatLE(offset+i*4));

function readMesh(source) {
  const doc = YAML.parseDocument(source.replace(/^%.*\r?\n/gm,'').replace(/^--- !u!.*$/gm,'---'));
  assert.equal(doc.errors.length,0,'Mesh YAML must parse');
  const mesh = doc.toJS().Mesh;
  const data = mesh.m_VertexData;
  const channels = data.m_Channels;
  const buffer = Buffer.from(data._typelessdata,'hex');
  const stride = buffer.length/data.m_VertexCount;
  for(const index of [0,1,2,4]) {
    assert.equal(channels[index].stream,0,'Repair expects one interleaved stream');
    assert.equal(channels[index].format,0,'Repair expects Float32 mesh channels');
  }
  assert.equal(mesh.m_IndexFormat,0,'Corner indices must be UInt16');
  assert.equal(mesh.m_SubMeshes.length,1,'Corner must have one material submesh');
  const indices = Buffer.from(mesh.m_IndexBuffer,'hex');
  return {
    mesh, stride,
    position:channels[0].offset, normal:channels[1].offset,
    tangent:channels[2].offset, uv:channels[4].offset,
    vertices:Array.from({length:data.m_VertexCount},(_,i) => Buffer.from(buffer.subarray(i*stride,(i+1)*stride))),
    indices:Array.from({length:indices.length/2},(_,i) => indices.readUInt16LE(i*2))
  };
}

function repair(input) {
  const vertices = input.vertices.map(v => Buffer.from(v));
  const indices = [...input.indices];
  const invalid = new Set();
  for(let i=0;i<vertices.length;i++)
    if(Math.hypot(...floats(vertices[i],input.normal,3))<.01) invalid.add(i);
  const assignments = new Map();
  const repaired = new Map();
  for(let i=0;i<indices.length;i+=3) {
    const original = input.indices.slice(i,i+3);
    if(!original.some(index => invalid.has(index))) continue;
    const p = original.map(index => floats(input.vertices[index],input.position,3));
    const face = cross(sub(p[1],p[0]),sub(p[2],p[0]));
    assert.ok(Math.hypot(...face)>1e-10,'Invalid normals must belong to non-degenerate faces');
    const normal = unit(face);
    for(let corner=0;corner<3;corner++) {
      const originalIndex = original[corner];
      if(!invalid.has(originalIndex)) continue;
      let variants = assignments.get(originalIndex);
      if(!variants) { variants=[]; assignments.set(originalIndex,variants); }
      let assignment = variants.find(entry => dot(entry.normal,normal)>.9999);
      if(!assignment) {
        const index = variants.length ? vertices.length : originalIndex;
        if(variants.length) vertices.push(Buffer.from(input.vertices[originalIndex]));
        assignment = {index,normal,tangent:[0,0,0],bitangent:[0,0,0]};
        variants.push(assignment);
        repaired.set(index,assignment);
        normal.forEach((value,axis) => vertices[index].writeFloatLE(value,input.normal+axis*4));
      }
      indices[i+corner]=assignment.index;
    }

    const uv = original.map(index => floats(input.vertices[index],input.uv,2));
    const du = sub(uv[1],uv[0]), dv = sub(uv[2],uv[0]);
    const determinant = du[0]*dv[1]-du[1]*dv[0];
    assert.ok(Math.abs(determinant)>1e-10,'Cut-face UVs must remain usable');
    const edge1 = sub(p[1],p[0]), edge2 = sub(p[2],p[0]);
    const tangent = edge1.map((value,axis) => (value*dv[1]-edge2[axis]*du[1])/determinant);
    const bitangent = edge2.map((value,axis) => (value*du[0]-edge1[axis]*dv[0])/determinant);
    for(let corner=0;corner<3;corner++) {
      const entry = repaired.get(indices[i+corner]);
      if(!entry) continue;
      for(let axis=0;axis<3;axis++) {
        entry.tangent[axis]+=tangent[axis];
        entry.bitangent[axis]+=bitangent[axis];
      }
    }
  }
  for(const entry of repaired.values()) {
    const tangent = unit(entry.tangent.map((value,axis) => value-entry.normal[axis]*dot(entry.tangent,entry.normal)));
    const handedness = dot(cross(entry.normal,tangent),entry.bitangent)<0 ? -1 : 1;
    [...tangent,handedness].forEach((value,axis) => vertices[entry.index].writeFloatLE(value,input.tangent+axis*4));
  }
  return {...input,vertices,indices,invalidCount:invalid.size};
}

function verify(mesh) {
  for(const vertex of mesh.vertices) {
    const n = floats(vertex,mesh.normal,3);
    assert.ok(n.every(Number.isFinite) && Math.abs(Math.hypot(...n)-1)<.0001,'Every normal must be finite and unit length');
  }
  // Coplanar triangles with opposite winding must not share their shading vertices.
  const faces = new Map();
  for(let i=0;i<mesh.indices.length;i+=3) {
    const ids = mesh.indices.slice(i,i+3);
    const key = [...ids].sort((a,b) => a-b).join(',');
    const p = ids.map(index => floats(mesh.vertices[index],mesh.position,3));
    const n = cross(sub(p[1],p[0]),sub(p[2],p[0]));
    const previous = faces.get(key);
    assert.ok(!previous || dot(previous,n)>=0,'Opposite faces cannot share a normal buffer');
    faces.set(key,n);
  }
}

function verifyPreserved(before,after) {
  assert.equal(after.indices.length,before.indices.length,'Triangle count is unchanged');
  for(let i=0;i<before.indices.length;i++) {
    const a = before.vertices[before.indices[i]], b = after.vertices[after.indices[i]];
    assert.deepEqual(floats(b,after.position,3),floats(a,before.position,3),'Every triangle position is preserved');
    assert.deepEqual(floats(b,after.uv,2),floats(a,before.uv,2),'Every triangle UV is preserved');
    if(Math.hypot(...floats(a,before.normal,3))>.01)
      assert.deepEqual(b,a,'Unaffected source vertex channels remain byte-for-byte identical');
    else {
      const ids = after.indices.slice(Math.floor(i/3)*3,Math.floor(i/3)*3+3);
      const p = ids.map(index => floats(after.vertices[index],after.position,3));
      const normal = unit(cross(sub(p[1],p[0]),sub(p[2],p[0])));
      assert.ok(dot(normal,floats(b,after.normal,3))>.9999,'Repaired face normals follow actual winding');
      const tangent = floats(b,after.tangent,3);
      assert.ok(Math.abs(dot(normal,tangent))<.0001 && Math.abs(Math.hypot(...tangent)-1)<.0001,'Repaired tangent frames are orthonormal');
    }
  }
}

function syntheticChecks() {
  const vertices = [[0,0,0],[1,0,0],[1,1,0],[0,1,0]].map((position,i) => {
    const buffer=Buffer.alloc(48);
    position.forEach((value,axis) => buffer.writeFloatLE(value,axis*4));
    [[0,0],[1,0],[1,1],[0,1]][i].forEach((value,axis) => buffer.writeFloatLE(value,40+axis*4));
    return buffer;
  });
  const input={vertices,indices:[0,1,2,0,2,3,0,2,1,0,3,2],position:0,normal:12,tangent:24,uv:40,stride:48};
  const result=repair(input);
  assert.equal(result.vertices.length,8,'Double-sided cut plane needs distinct front/back vertices');
  verify(result); verifyPreserved(input,result);
  assert.equal(repair(result).invalidCount,0,'Repair is idempotent');
}

syntheticChecks();
const source = fs.readFileSync(assetPath,'utf8');
const before = readMesh(source);
if(process.argv.includes('--repair')) {
  const after = repair(before);
  verify(after); verifyPreserved(before,after);
  if(after.invalidCount) {
    const data = Buffer.concat(after.vertices);
    const indices = Buffer.alloc(after.indices.length*2);
    after.indices.forEach((value,i) => indices.writeUInt16LE(value,i*2));
    let updated = source.replace(/(m_VertexCount: )\d+/,`$1${after.vertices.length}`)
      .replace(/(    vertexCount: )\d+/,`$1${after.vertices.length}`)
      .replace(/(m_DataSize: )\d+/,`$1${data.length}`)
      .replace(/(m_IndexBuffer: )[0-9a-f]+/,`$1${indices.toString('hex')}`)
      .replace(/(_typelessdata: )[0-9a-f]+/,`$1${data.toString('hex')}`);
    const serialized = readMesh(updated);
    verify(serialized); verifyPreserved(before,serialized);
    fs.writeFileSync(assetPath,updated);
    console.log(`Repaired ${after.invalidCount} zero normals; vertices ${before.vertices.length} -> ${after.vertices.length}; triangles ${after.indices.length/3} unchanged.`);
  } else console.log('Corner already has valid normals; no asset changes.');
} else {
  verify(before);
  console.log(`PASS: brick corner ${before.vertices.length} vertices / ${before.indices.length/3} triangles; no zero normals or shared opposite-face vertices; synthetic split/UV/tangent/idempotence checks passed.`);
}
