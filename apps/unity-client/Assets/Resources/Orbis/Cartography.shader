Shader "Orbis/Cartography"
{
    Properties
    {
        _BaseMap("Relief", 2D) = "black" {}
        _BiomeMap("Biomes", 2D) = "black" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Manuscript("Manuscript", Float) = 0
        _Fade("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BiomeMap); SAMPLER(sampler_BiomeMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Manuscript;
                float _Fade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 relief = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 biome = SAMPLE_TEXTURE2D(_BiomeMap, sampler_BiomeMap, input.uv);
                half land = relief.a;
                half3 terrain = lerp(biome.rgb, relief.rgb, .44);
                // Biomas diferenciados: verdes (floresta) escurecem em manchas procedurais;
                // áridos ganham matiz quente; tundra fria e clara via azul do mapa de biomas.
                half forest = saturate((biome.g - biome.r) * 2.0);
                half arid = saturate((biome.r - biome.g) * 2.0);
                float2 dappleGrid = floor(input.uv * 2400.0);
                half dapple = frac(sin(dot(dappleGrid, float2(12.9898, 78.233))) * 43758.5453);
                terrain *= 1.0 - dapple * .10 * forest;
                terrain *= lerp(half3(1.0, 1.0, 1.0), half3(1.05, .99, .88), arid * .6);
                terrain *= lerp(half3(1.0, 1.0, 1.0), half3(.98, 1.02, 1.08), saturate(biome.b) * .35);
                half shade = .8 + .2 * saturate(dot(normalize(input.normalWS), normalize(float3(-.4,.7,.5))));
                half hatch = .96 + .04 * step(.45, frac((input.uv.x + input.uv.y) * 1600));
                float2 glyph = frac(input.uv * float2(160,100)) - .5;
                half tree = step(abs(glyph.x),(.3-glyph.y)*.5)*step(-.2,glyph.y)*step(glyph.y,.3);
                terrain *= 1 - tree*.12*step(biome.r*1.05,biome.g);
                half3 ink = dot(terrain, half3(.3,.59,.11)) * half3(.72,.59,.37) * hatch;
                terrain = lerp(terrain * half3(.72,.77,.64), ink, _Manuscript);
                half3 inkSea = half3(.043,.059,.071);
                half3 surface = lerp(inkSea, terrain * shade, land);
                // Patches do mosaico nascem na tinta do mar e revelam o relevo conforme o carregamento (_Fade 0..1).
                return half4(lerp(inkSea, surface, _Fade) * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
