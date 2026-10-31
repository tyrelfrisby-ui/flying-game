// Built-in, bounded fixture glow. Four vertices per light, no texture, bloom or realtime lights.
Shader "FlyingGame/AirportGlow"
{
    Properties { _HaloPixels ("Distant glow radius in pixels", Float) = 2 }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _AirportDusk, _ReflectionRendering, _HaloPixels, _SunsetOn, _CityNight, _AirportBeaconPreview, _AirportBeaconTime;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; float3 rowStep:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float visible:TEXCOORD1; float2 opticalRadii:TEXCOORD2; float rowClarity:TEXCOORD3; };
            v2f vert(appdata v)
            {
                v2f o;
                float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                float3 towardEye=normalize(_WorldSpaceCameraPos-world);
                // Zero normal is omnidirectional. Threshold faces and PAPI point into their approach.
                float directional=dot(v.normal,v.normal);
                float visible=directional>.1?smoothstep(.02,.25,dot(v.normal,towardEye)):1;
                // Beacon alpha encodes opposed green/white rotating heads (120 degrees/s).
                float beacon=1-step(.75,v.color.a);
                float papi=step(.75,v.color.a)*(1-step(.9,v.color.a));
                if(beacon>.5)
                {
                    float phase=v.color.a<.375?0:UNITY_PI;
                    float seconds=lerp(_Time.y,_AirportBeaconTime,_AirportBeaconPreview);
                    float beam=cos(atan2(towardEye.x,towardEye.z)-seconds*2.094395+phase);
                    visible*=.015+5*smoothstep(.80,.985,beam);
                }
                float thresholdGreen=step(.925,v.color.a)*(1-step(.975,v.color.a));
                float4 view=mul(UNITY_MATRIX_V,float4(world,1));
                view.z+=.65; // Put the optical glow just in front of its opaque lens.
                // Render-target projections can flip Y (including Metal). A signed
                // scale here collapses the denominator to 1 and makes 24 m blobs.
                float metresPerPixel=2*max(.01,-view.z)/max(1,_ScreenParams.y*abs(UNITY_MATRIX_P._m11));
                // Size the visible core, not just its mostly transparent card. All
                // pixel targets are relative to a 1024-wide plate, including Retina.
                float resolutionScale=max(.25,_ScreenParams.x/1024.0);
                float quality=saturate((_HaloPixels-1.5)/1.5);
                float corePixels=lerp(1.5,2.1,quality)*resolutionScale;
                corePixels=lerp(corePixels,2.25*resolutionScale,papi);
                float haloPixels=lerp(5.0,7.0,quality)*resolutionScale;
                haloPixels*=1+beacon*.45;
                haloPixels=lerp(haloPixels,6.0*resolutionScale,papi);
                // Only edge rows carry a 60 m world-space neighbour vector.
                // Estimate the smaller projected interval on either side so the
                // far, foreshortened row loses its skirt before becoming a rail.
                o.rowClarity=1;
                if(dot(v.rowStep,v.rowStep)>1)
                {
                    float3 delta=mul((float3x3)UNITY_MATRIX_V,mul((float3x3)unity_ObjectToWorld,v.rowStep));
                    float4 centreClip=mul(UNITY_MATRIX_P,view);
                    float4 nextClip=mul(UNITY_MATRIX_P,view+float4(delta,0));
                    float4 prevClip=mul(UNITY_MATRIX_P,view-float4(delta,0));
                    float2 centreNdc=centreClip.xy/max(.001,centreClip.w);
                    float nextSpacing=length((nextClip.xy/max(.001,nextClip.w)-centreNdc)*_ScreenParams.xy*.5);
                    float prevSpacing=length((prevClip.xy/max(.001,prevClip.w)-centreNdc)*_ScreenParams.xy*.5);
                    float spacing=min(nextSpacing,prevSpacing);
                    // Suppress only when neighbouring bright cores nearly touch.
                    // A9's four-halo-radius upper bound dimmed even normally spaced
                    // aerial rows. Above three core radii this is exactly A8 again.
                    o.rowClarity=smoothstep(2*corePixels,3*corePixels,spacing);
                    haloPixels=lerp(corePixels*1.1,haloPixels,o.rowClarity);
                }
                // The physical floor may enlarge a nearby fixture, but cannot
                // bypass the close-up screen cap. The pixel floor survives at range.
                float physicalRadius=lerp(1.0,.35,papi);
                float maxScreenFraction=lerp(.01,.0075,thresholdGreen);
                maxScreenFraction=lerp(maxScreenFraction,.008,papi);
                float screenLimit=metresPerPixel*_ScreenParams.y*maxScreenFraction;
                float radius=min(max(physicalRadius,metresPerPixel*max(haloPixels,corePixels/.55)),screenLimit);
                // Independent radii keep the bright disc 3-5 px across rather
                // than shrinking it to ~1/10 of an already tiny halo card.
                o.opticalRadii=float2(min(.55,metresPerPixel*corePixels/max(.001,radius)),
                                     min(1.0,metresPerPixel*haloPixels/max(.001,radius)));
                view.xy+=v.uv*radius;
                o.pos=mul(UNITY_MATRIX_P,view); o.uv=v.uv; o.color=v.color;
                o.visible=visible*(1+papi*1.5+thresholdGreen*1.8); return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float strength=max(_AirportDusk,max(_SunsetOn*.78,_CityNight));
                clip(strength-.001);
                clip(.5-_ReflectionRendering); // No un-clipped halos in mirrored water views.
                float radius2=dot(i.uv,i.uv); clip(1-radius2);
                float coreRadius=max(.001,i.opticalRadii.x);
                float haloRadius=max(.001,i.opticalRadii.y);
                float papi=step(.75,i.color.a)*(1-step(.9,i.color.a));
                float core=exp(-.693147*radius2/(coreRadius*coreRadius))*2.7;
                // Keep the A8 half-maximum core size, but trim its faint
                // outer tail in dense edge rows; PAPI/end/beacon are unchanged.
                core*=lerp(1-smoothstep(1.0,2.25,radius2/(coreRadius*coreRadius)),1,i.rowClarity);
                float skirt=lerp(.04,1,i.rowClarity)*exp(-3.0*radius2/(haloRadius*haloRadius))*lerp(.32,.20,papi);
                float halo=(core+skirt)*(1-smoothstep(.75,1.0,radius2));
                return float4(i.color.rgb*halo*strength*i.visible,0);
            }
            ENDCG
        }
    }
}
