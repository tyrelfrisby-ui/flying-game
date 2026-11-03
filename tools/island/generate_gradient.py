#!/usr/bin/env python3
"""Specialize the exact indexed height expressions for forward analytic differentiation.

IslandHeightIndex.cs is the single source for both evaluators. Only query arithmetic is
lifted to (value,dE,dN); authoring data, index construction and candidate order stay scalar.
Run after changing the height evaluator; --check rejects stale generated output.
No finite differences, sampling, symbolic simplification or arithmetic reassociation.
"""
from pathlib import Path
import hashlib
import re
import sys

root = Path(__file__).resolve().parents[2]
source = root / 'src/FlyingGame.Core/IslandHeightIndex.cs'
target = source.with_name('IslandGradientIndex.Generated.cs')
original = source.read_text()
s = original.replace('IslandHeightIndex', 'IslandGradientIndex')

def lift(text):
    text = re.sub(r'\bdouble\b', 'TerrainJet', text)
    text = text.replace('Math.', 'TerrainJet.')
    text = text.replace('IslandTerrain.Segment(', 'TerrainJetPoint.Segment(')
    return text

def body(signature):
    global s
    start = s.index(signature)
    brace = s.index('{', start)
    depth = 1
    end = brace+1
    while depth:
        depth += (s[end] == '{') - (s[end] == '}')
        end += 1
    s = s[:brace]+lift(s[brace:end])+s[end:]
    s = s[:start]+s[start:brace].replace('IslandPoint p','TerrainJetPoint p').replace('double','TerrainJet')+s[brace:]

for signature in ['public (double d,double t) Distance(', 'public double Shore(',
                  'public double Apply(', 'public double Height(',
                  'private static double Layers(', 'internal static double Evaluate(',
                  'internal static double NaturalShape(', 'internal static double StripShape(']:
    body(signature)
# Every layer closure is another expression of the same scalar evaluator.
starts = list(re.finditer(r'\(p,h\)=>', s))
for match in reversed(starts):
    start=match.end()
    if s[start:].lstrip().startswith('{'):
        brace=s.index('{',start); end=brace+1; depth=1
        while depth:
            depth+=(s[end]=='{')-(s[end]=='}'); end+=1
    else:
        # expression ends at Add's final parenthesis; balance nested expressions
        end=start; depth=0
        while not (s[end]==')' and depth==0):
            depth+=(s[end]=='(')-(s[end]==')'); end+=1
    s=s[:start]+lift(s[start:end])+s[end:]
s=s.replace('private delegate double Layer(IslandPoint p, double h);', 'private delegate TerrainJet Layer(TerrainJetPoint p, TerrainJet h);')
s=s.replace('private static double Smooth(double t) => IslandTerrain.Smooth(t);', 'private static TerrainJet Smooth(TerrainJet t) => TerrainJet.Smooth(t);')
s=s.replace('private static double Mix(double h, double z, double a) => IslandTerrain.Mix(h, z, a);', 'private static TerrainJet Mix(TerrainJet h, TerrainJet z, TerrainJet a) => h+(z-h)*a;')
s=s.replace('public (double d,double t) At(IslandPoint p) => geometry!.Distance(p,Total);', 'public (TerrainJet d,TerrainJet t) At(TerrainJetPoint p) => geometry!.Distance(p,Total);')
# The field centre is adjusted along the runway before evaluating its plane.
s=s.replace('var c=f.Centre; TerrainJet l=', 'TerrainJetPoint c=f.Centre; TerrainJet l=')
# Geometry layout is shared with the scalar index, not independently rebuilt. Keep only
# its lifted query methods and forward bucket selection to the canonical compiled geometry.
start=s.index('    internal sealed class Geometry\n')
queries=s.index('        public (TerrainJet d,TerrainJet t) Distance(',start)
s=s[:start]+"""    private sealed class Geometry
    {
        private readonly IslandHeightIndex.Geometry shared;
        public Geometry(IslandPoint[] points,double radius,bool closed)
        { shared=IslandHeightIndex.SharedGeometry(points,radius,closed); }
        private IslandHeightIndex.GeometryCell? At(IslandPoint p) => shared.At(p);
"""+s[queries:]
# Remove the scalar initialization registry from the generated specialization.
start=s.index('    private static readonly List<Geometry> Geometries')
end=s.index('    private static IslandValue W =>',start)
s=s[:start]+s[end:]
start=s.index('    internal readonly struct Edge')
end=s.index('    private sealed class Geometry\n',start)
s=s[:start]+s[end:]
s=s.replace('    private static int maxNearestSegments, maxRayEdges;\n','')
s=re.sub(r'    internal static string WorkBounds =>[^\n]*\n','',s)
# The scalar-only unbucketed diagnostic is not a gradient API. Remove it entirely:
# every derivative call must use bounded candidate lists and differentiated geometry.
s=s.replace(',bool indexed=true)', ')').replace(',bool indexed)', ')')
s=re.sub(r'indexed\?([A-Za-z]+)\.At\(p\):\1\.All', r'\1.At(p)', s)
s=s.replace('indexed?(MainlandBounds.Contains(p)?MainlandCoast.Shore(p):-100):IslandTerrain.Shore(p,Land[0])', '(MainlandBounds.Contains(p)?MainlandCoast.Shore(p):-100)')
s=s.replace('(indexed?CatalinaCoast.Shore(p):IslandTerrain.Shore(p,Land[1]))', 'CatalinaCoast.Shore(p)')
s=s.replace('indexed?q.Coast.Shore(p):IslandTerrain.Shore(p,q.Polygon)', 'q.Coast.Shore(p)')
s=s.replace(',h,indexed)', ',h)')
assert 'indexed?' not in s and 'IslandTerrain.Shore(p' not in s, 'Unlifted gradient path'
header = '// <auto-generated> tools/island/generate_gradient.py; do not edit.\n// Source SHA256: '+hashlib.sha256(original.encode()).hexdigest()+'\n// </auto-generated>\n#nullable enable\n'
s=header+s
if '--check' in sys.argv:
    if not target.exists() or target.read_text()!=s:
        sys.exit('Stale analytic terrain evaluator: run python3 tools/island/generate_gradient.py')
else:
    target.write_text(s)
