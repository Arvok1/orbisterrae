Shader "Orbis/CartographicInk"
{
    Properties { _BaseColor("Ink", Color) = (.1,.2,.23,1) _Ribbon("Ribbon", Float) = 1 _Width("Pixel width", Float) = 1.6 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Ribbon;
                float _Width;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 endpoint : TEXCOORD0; float2 side : TEXCOORD1; };
            float4 Vert(Attributes input) : SV_POSITION
            {
                float4 position = TransformObjectToHClip(input.positionOS.xyz);
                float4 endpoint = TransformObjectToHClip(input.endpoint);
                float2 direction = endpoint.xy / max(abs(endpoint.w),.0001) - position.xy / max(abs(position.w),.0001);
                direction = normalize(direction * _ScreenParams.xy + float2(.00001,0));
                position.xy += float2(-direction.y,direction.x) * input.side.x * _Width / _ScreenParams.xy * position.w * _Ribbon;
                return position;
            }
            half4 Frag() : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
}
