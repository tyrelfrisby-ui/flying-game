#!/usr/bin/env python3
"""Limited source compile using installed Unity DLLs. Never launches Unity or builds a player.
External session/weather/event adapters are stubbed; this cannot establish BUILD SUCCEEDED.
Build Core first. All temporary files/caches live in TMPDIR.
"""
import os,subprocess,tempfile,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
version=(ROOT/'unity/ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ',1)[1]
engine=Path('/Applications/Unity/Hub/Editor')/version/'Unity.app/Contents/Resources/Scripting/Managed/UnityEngine'
assert engine.is_dir(),f'Installed Unity{version} engine references unavailable'
dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
with tempfile.TemporaryDirectory(prefix='island-world-source-') as work:
 p=Path(work);bridge=ROOT/'unity/Assets/_Project/Scripts/Bridge'
 play=(bridge/'PlaygroundBuilder.cs').read_text();play=play[:play.index('    /// <summary>Runtime for the playground:')]+'}\n';(p/'PlaygroundGeometry.cs').write_text(play)
 (p/'Adapters.cs').write_text('''using UnityEngine;using System.Collections.Generic;
namespace FlyingGame.Bridge {
public static class Cumulus {public static void Puffy(Vector3 p,float r,float h,int seed){}}
public static class SessionSettings {public static bool ReplayActive;}
public static class WaterReflection {public const int PropsLayer=9;}
public static class CoordinateMap {public static FlyingGame.Core.MathTypes.Vec3 ToSim(Vector3 v)=>new(v.z,v.x,-v.y);public static Vector3 ToUnity(FlyingGame.Core.MathTypes.Vec3 v)=>new((float)v.Y,(float)-v.Z,(float)v.X);}
public sealed class PlaygroundRuntime:MonoBehaviour {public static readonly List<Transform> SpinRings=new();}
}''')
 refs=''.join(f'<Reference Include="{q.stem}"><HintPath>{q}</HintPath></Reference>' for q in engine.glob('*.dll'))
 files=[q for q in bridge.glob('*Builder.cs') if q.name!='PlaygroundBuilder.cs']+[p/'PlaygroundGeometry.cs',p/'Adapters.cs']+[bridge/f for f in ['PylonTopBurst.cs','Spin.cs','Windsock.cs','IslandTerrainMesh.cs']]+[ROOT/'unity/Assets/_Project/Scripts/Editor/WorldRender.cs']
 includes=''.join(f'<Compile Include="{q}"/>' for q in files)
 (p/'Check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>disable</Nullable></PropertyGroup><ItemGroup>'+refs+f'<Reference Include="FlyingGame.Core"><HintPath>{ROOT}/src/FlyingGame.Core/bin~/Debug/net8.0/FlyingGame.Core.dll</HintPath></Reference>'+includes+'</ItemGroup></Project>')
 env=dict(os.environ,DOTNET_CLI_HOME=str(Path(os.environ.get('TMPDIR','/tmp'))/'island-dotnet'))
 result=subprocess.run([dotnet,'build',str(p/'Check.csproj'),'--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false'],env=env)
 raise SystemExit(result.returncode)
