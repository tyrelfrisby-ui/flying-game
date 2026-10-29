// Original procedural airport finishes. No photographic textures; one opaque built-in pass.
Shader "FlyingGame/AirportSurface"
{
    Properties
    {
        _Color ("Base", Color) = (.3,.3,.3,1)
        _Kind ("0 asphalt, 1 concrete, 2 gravel, 3 grass, 4 timber, 5 deck", Float) = 0
        _Size ("Local metres", Vector) = (1,1,1,0)
        _Origin ("Local origin", Vector) = (0,0,0,0)
        _Apron ("Apron flood pools", Float) = 0
        _Length ("Runway length (zero disables rubber)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "SunsetSky.cginc"
            float4 _Color, _Size, _Origin, _BubbleSunDir, _ReflectionClip;
            float _Kind, _Length, _ReflectionRendering, _Apron, _AirportDusk;
            struct v2f { float4 pos:SV_POSITION; float3 wp:TEXCOORD0; float3 n:TEXCOORD1; float2 p:TEXCOORD2; UNITY_FOG_COORDS(3) };
            v2f vert(appdata_base v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                o.wp=mul(unity_ObjectToWorld,v.vertex).xyz; o.n=UnityObjectToWorldNormal(v.normal);
                o.p=((v.vertex.xyz-_Origin.xyz)*_Size.xyz).xz;
                UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 a=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            // Analytic lines broaden with the pixel footprint, avoiding distant sparkling.
            float surfaceLine(float d,float width,float aa) { return (1-smoothstep(width,width+aa,abs(d)))*saturate(2*width/aa); }
            fixed4 frag(v2f i, float face:VFACE):SV_Target
            {
                if (_ReflectionRendering>.5) clip(dot(float4(i.wp,1),_ReflectionClip));
                float2 p=i.p;
                float aa=max(.015,max(length(ddx(p)),length(ddy(p))));
                float broad=noise(p*.053), medium=lerp(.5,noise(p*.73),saturate(1-aa*.5));
                float grain=(noise(p*19)-.5)*saturate(1-aa*3);
                float3 c=_Color.rgb*(.91+.13*broad+.06*medium+.09*grain);
                float top=saturate(normalize(i.n).y*8);
                if (_Kind<.5 || _Kind>4.5)
                {
                    float2 cell=floor((p+float2(13,7))/float2(8,29));
                    float patch=step(.83,hash(cell));
                    c*=1-.22*patch*top;
                    c*=.91+.18*noise(float2(p.x*.13,p.y*.008));
                    float seam=surfaceLine(p.x-1.4-noise(float2(p.y*.09,6))*.35,.018,aa);
                    float joint=surfaceLine((frac((p.y+noise(p*.13)*.8)/29+.5)-.5)*29,.025,aa);
                    c*=1-top*(seam*.22+joint*.13);
                    if (_Length>0)
                    {
                        float dist=_Length*.5-abs(p.y);
                        float touch=smoothstep(65,145,dist)*(1-smoothstep(430,650,dist));
                        float rubber=exp(-pow((abs(p.x)-2.1)/1.0,2))*touch;
                        rubber*=.3+.7*noise(float2(p.x*7,p.y*.17));
                        c*=1-rubber*.65*top;
                    }
                }
                else if (_Kind<1.5)
                {
                    float2 q=abs(frac((p+3)/6)-.5)*6;
                    float joint=(1-smoothstep(.025,.025+aa,min(q.x,q.y)))*saturate(.1/aa);
                    c*=1-joint*.22*top;
                    c*=.94+.12*hash(floor((p+3)/6));
                    float oil=pow(saturate(noise(p*.31)-.38),3)*1.8;
                    c*=1-oil*top;
                    c*=1-.14*pow(saturate(noise(p*.18)-.35),2)*top;
                }
                else if (_Kind<2.5)
                {
                    float rut=exp(-pow((abs(p.x)-2.0)/.65,2));
                    c*=1-.22*rut*top;
                    c+=grain*.10;
                }
                else if (_Kind<3.5)
                {
                    float mow=.5+.5*sin(p.x*1.57)*saturate(1-aa);
                    // Broad dry/wet variation breaks up the otherwise uniform mown carpet.
                    c=lerp(c,c*float3(1.08,1.01,.85),smoothstep(.48,.78,broad)*.45);
                    float tracks=exp(-pow((abs(p.x)-1.8)/.55,2));
                    c*=.84+.16*mow;
                    c=lerp(c,float3(.40,.35,.22),tracks*(.22+.22*medium));
                }
                else
                {
                    float joint=surfaceLine((frac(p.y/.24+.5)-.5)*.24,.008,aa);
                    float fibre=lerp(.5,noise(float2(p.x*.8,p.y*60)),saturate(1-aa*10));
                    c*=.86+.19*fibre-joint*.2*saturate(1-aa);
                }
                float3 n=normalize(i.n)*(face>=0?1:-1);
                float3 l=dot(_BubbleSunDir.xyz,_BubbleSunDir.xyz)>.001?normalize(_BubbleSunDir.xyz):normalize(float3(.4,.8,-.4));
                float d=saturate(dot(n,l));
                c*=lerp(float3(.58,.58,.58)+.42*d,lerp(float3(.53,.51,.54),float3(.64,.65,.73),saturate(n.y*.5+.5))+float3(.65,.43,.24)*d,_SunsetOn);
                // Two broad flood pools on the existing apron, no extra transparent overdraw.
                float2 flood=float2(p.x-94,abs(p.y)-128);
                c+=float3(.30,.23,.12)*exp(-dot(flood,flood)/500)*_Apron*_AirportDusk*top;
                fixed4 result=fixed4(c,1);
                UNITY_APPLY_FOG(i.fogCoord,result);
                return result;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
