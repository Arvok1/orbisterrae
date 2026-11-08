Shader "Orbis/CartographicInk"
{
    Properties { _BaseColor("Ink", Color) = (.1,.2,.23,1) _Ribbon("Ribbon", Float) = 1 _Width("Pixel width", Float) = 1.6 _GlobeRadius("Globe radius", Float) = 6371 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Ribbon;
                float _Width;
                float _GlobeRadius;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 endpoint : TEXCOORD0; float2 side : TEXCOORD1; };
            struct Varyings { float4 position : SV_POSITION; float3 center : TEXCOORD2; float3 world : TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.center = TransformObjectToWorld(float3(0, 0, 0));
                output.world = world;
                float4 position = TransformWorldToHClip(world);
                float4 endpoint = TransformWorldToHClip(TransformObjectToWorld(input.endpoint));
                float2 direction = endpoint.xy / max(abs(endpoint.w),.0001) - position.xy / max(abs(position.w),.0001);
                direction = normalize(direction * _ScreenParams.xy + float2(.00001,0));
                position.xy += float2(-direction.y,direction.x) * input.side.x * _Width / _ScreenParams.xy * position.w * _Ribbon;
                output.position = position;
                return output;
            }
            /// Ink fades where the globe itself blocks the eye ray: past its tangent horizon it is invisible.
            half Visibility(Varyings input)
            {
                float3 toCamera = _WorldSpaceCameraPos - input.center;
                float eye = max(length(toCamera), 1e-6);
                float horizon = _GlobeRadius / eye; // cosine of the horizon angle seen from the camera
                float facing = dot(normalize(input.world - input.center), toCamera / eye);
                float band = max((1 - horizon) * .3, 1e-7); // fade band grows with distance so close zoom still cuts sharply
                return saturate((facing - horizon) / band);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half visibility = Visibility(input);
                return half4(_BaseColor.rgb, _BaseColor.a * visibility);
            }
            ENDHLSL
        }
    }
}
