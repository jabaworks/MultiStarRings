Shader "MultiStarRings/RingsMultiStar"
{
    Properties
    {
        _MainTex ("Ring Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)

        [Header(Lighting)]
        _Brightness ("Brightness", Range(0, 8)) = 2.0
        _AlbedoStrength ("Albedo Strength", Range(0, 2)) = 1.0

        [Header(Scattering Rays)]
        _ScatteringStrength ("Scattering Strength", Range(0, 6)) = 1.8
        _Anisotropy ("Anisotropy (Ray Focus)", Range(-0.95, 0.95)) = 0.72
        _ScatteringPower ("Scattering Power", Range(1, 8)) = 2.5
        _AmbientScatter ("Ambient Scatter", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
                float3 ringRelPos : TEXCOORD4;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            float _Brightness;
            float _AlbedoStrength;

            float _ScatteringStrength;
            float _Anisotropy;
            float _ScatteringPower;
            float _AmbientScatter;

            float4 sunPositions[4];
            float sunRadii[4];
            float sunWeights[4];
            float sunLuminosities[4];
            float sunFluxRatios[4];
            float sunReferenceDistances[4];

            float referenceFlux;
            float4 sunColors[4];
            int numActiveLights;

            #define MAX_KEYFRAMES_PER_LIGHT 20

            float starKeyframeDistances[4 * MAX_KEYFRAMES_PER_LIGHT];
            float starKeyframeValues[4 * MAX_KEYFRAMES_PER_LIGHT];
            float starKeyframeInTangents[4 * MAX_KEYFRAMES_PER_LIGHT];
            float starKeyframeOutTangents[4 * MAX_KEYFRAMES_PER_LIGHT];
            float starKeyframeCounts[4];

            float brightnessCompressionExponent;
            float brightnessCeiling;
            float glowBoost;

            float4 _RefBodyPos;
            float _RingScaleFactor;

            float4 shadowPositions[8];
            float shadowRadii[8];
            float shadowIntensities[8];

            int numShadows;
            float shadowSoftness;
            int shadowQuality;

            float hgPhase(float cosTheta, float g)
            {
                float g2 = g * g;

                float denom =
                    1.0 +
                    g2 -
                    2.0 * g *
                    cosTheta;

                return
                    (1.0 - g2) /
                    (
                        pow(
                            max(denom, 0.001),
                            1.5
                        )
                        *
                        12.5663706144
                    );
            }

            float calculateShadow(
                float3 ringPos,
                float3 lightPos,
                float3 shadowPos,
                float shadowRadius,
                float lightRadius,
                float intensity,
                float softness)
            {
                float3 ray =
                    lightPos - ringPos;

                float tMax =
                    length(ray);

                if (tMax <= 1e-6)
                    return 1.0;

                float3 rayDir =
                    ray / tMax;

                float3 occluderToRayOrigin =
                    shadowPos - ringPos;

                float b =
                    dot(
                        occluderToRayOrigin,
                        rayDir
                    );

                if (b < 0.0)
                    return 1.0;

                float perpDistSqr =
                    dot(
                        occluderToRayOrigin,
                        occluderToRayOrigin
                    )
                    -
                    b * b;

                float perpDist =
                    sqrt(
                        max(
                            perpDistSqr,
                            0.0
                        )
                    );

                float distToOccluder =
                    length(
                        shadowPos - ringPos
                    );

                float distLightToOccluder =
                    length(
                        lightPos - shadowPos
                    );

                float penumbraWidth =
                    max(
                        shadowRadius +
                        lightRadius *
                        (
                            distToOccluder /
                            max(
                                distLightToOccluder,
                                1e-6
                            )
                        ),
                        0.0001
                    )
                    *
                    softness;

                float edgeDist =
                    shadowRadius - perpDist;

                if (edgeDist <= -penumbraWidth)
                    return 1.0;

                if (edgeDist >= penumbraWidth)
                    return 1.0 - intensity;

                float factor =
                    saturate(
                        (edgeDist + penumbraWidth) /
                        (2.0 * penumbraWidth)
                    );

                return lerp(
                    1.0,
                    1.0 - intensity,
                    factor
                );
            }

            float evaluateStarIntensityCurve(int lightIndex, float scaledDistance, out bool hasCurve)
            {
                int count = (int)starKeyframeCounts[lightIndex];
                hasCurve = count > 0;

                if (count <= 0)
                    return -1.0;

                int kfBase = lightIndex * MAX_KEYFRAMES_PER_LIGHT;
                float distanceMeters = scaledDistance * _RingScaleFactor;

                if (count == 1)
                    return max(starKeyframeValues[kfBase], 0.0);

                if (distanceMeters <= starKeyframeDistances[kfBase])
                    return max(starKeyframeValues[kfBase], 0.0);

                for (int k = 0; k < MAX_KEYFRAMES_PER_LIGHT - 1; k++)
                {
                    if (k + 1 >= count)
                        break;

                    int i0 = kfBase + k;
                    int i1 = kfBase + k + 1;

                    float x0 = starKeyframeDistances[i0];
                    float x1 = starKeyframeDistances[i1];

                    if (distanceMeters <= x1)
                    {
                        float y0 = starKeyframeValues[i0];
                        float y1 = starKeyframeValues[i1];
                        float m0 = starKeyframeOutTangents[i0];
                        float m1 = starKeyframeInTangents[i1];

                        float dx = x1 - x0;
                        float t = saturate((distanceMeters - x0) / max(dx, 1e-6));

                        float t2 = t * t;
                        float t3 = t2 * t;

                        float h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
                        float h10 = t3 - 2.0 * t2 + t;
                        float h01 = -2.0 * t3 + 3.0 * t2;
                        float h11 = t3 - t2;

                        float value =
                            h00 * y0 +
                            h10 * dx * m0 +
                            h01 * y1 +
                            h11 * dx * m1;

                        return max(value, 0.0);
                    }
                }

                return max(starKeyframeValues[kfBase + count - 1], 0.0);
            }

            v2f vert(appdata v)
            {
                v2f o;

                o.pos =
                    UnityObjectToClipPos(
                        v.vertex
                    );

                o.uv =
                    TRANSFORM_TEX(
                        v.uv,
                        _MainTex
                    );

                o.worldPos =
                    mul(
                        unity_ObjectToWorld,
                        v.vertex
                    ).xyz;

                o.ringRelPos =
                    mul(
                        (float3x3)unity_ObjectToWorld,
                        v.vertex.xyz
                    );

                o.worldNormal =
                    UnityObjectToWorldNormal(
                        v.normal
                    );

                o.viewDir =
                    _WorldSpaceCameraPos -
                    o.worldPos;

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 texColor =
                    tex2D(
                        _MainTex,
                        i.uv
                    );

                float3 normal =
                    normalize(
                        i.worldNormal
                    );

                float3 viewDir =
                    normalize(
                        i.viewDir
                    );

                float3 ringPos =
                i.ringRelPos;

                float opticalDepth =
                    saturate(
                        1.0 -
                        abs(i.uv.x - 0.5) *
                        1.9
                    );

                opticalDepth =
                    pow(
                        opticalDepth,
                        0.8
                    );

                float3 totalLight =
                    float3(
                        0.0,
                        0.0,
                        0.0
                    );

                int lightCount =
                    min(
                        numActiveLights,
                        4
                    );

                int shadowCount =
                    min(
                        numShadows,
                        8
                    );

                float3 ambientLight =
                    float3(
                        0.01,
                        0.01,
                        0.01
                    )
                    *
                    opticalDepth;

                if (lightCount > 0)
                {
                    for (int j = 0;
                         j < lightCount;
                         j++)
                    {
                        float3 starPos =
                            sunPositions[j].xyz;

                        float3 starColor =
                            sunColors[j].rgb;

                        float3 toStar =
                            starPos -
                            ringPos;

                        float dist =
                            length(
                                toStar
                            );

                        dist =
                            max(
                                dist,
                                1e-6
                            );

                        float3 lightDir =
                            toStar /
                            dist;

                        bool hasStarCurve;

                        float curveAtPixel =
                            evaluateStarIntensityCurve(
                                j,
                                dist,
                                hasStarCurve
                            );

                        float curveAtReference =
                            evaluateStarIntensityCurve(
                                j,
                                max(
                                    sunReferenceDistances[j],
                                    1e-6
                                ),
                                hasStarCurve
                            );

                        float curveRatio = 1.0;

                        if (hasStarCurve &&
                            curveAtReference > 1e-6)
                        {
                            curveRatio =
                                max(
                                    curveAtPixel /
                                    curveAtReference,
                                    0.0
                                );
                        }

                        float referenceDistance =
                            max(
                                sunReferenceDistances[j],
                                1e-6
                            );

                        float distanceRatio =
                            referenceDistance /
                            dist;

                        float inverseSquare =
                            distanceRatio *
                            distanceRatio;

                        float attenuation =
                            sunFluxRatios[j] *
                            inverseSquare *
                            curveRatio;

                        attenuation =
                            max(
                                attenuation,
                                0.001
                            );

                        float ndotl =
                            abs(
                                dot(
                                    normal,
                                    lightDir
                                )
                            );

                        float diffTerm =
                            ndotl *
                            _AlbedoStrength;

                        float cosTheta =
                            clamp(
                                dot(
                                    -lightDir,
                                    viewDir
                                ),
                                -1.0,
                                1.0
                            );

                        float hg =
                            hgPhase(
                                cosTheta,
                                _Anisotropy
                            );

                        float scatter =
                            pow(
                                hg,
                                _ScatteringPower
                            )
                            *
                            _ScatteringStrength
                            *
                            opticalDepth;

                        float ambient =
                            _AmbientScatter *
                            opticalDepth;

                        float shadowMultiplier =
                            1.0;

                        if (shadowCount > 0)
                        {
                            for (int k = 0;
                                 k < shadowCount;
                                 k++)
                            {
                                float3 shadowPos =
                                    shadowPositions[k].xyz;

                                float shadowRadius =
                                    shadowRadii[k];

                                float starToShadow =
                                    length(
                                        starPos -
                                        shadowPos
                                    );

                                if (starToShadow <=
                                    shadowRadius * 1.01)
                                {
                                    continue;
                                }

                                float s =
                                    calculateShadow(
                                        ringPos,
                                        starPos,
                                        shadowPos,
                                        shadowRadius,
                                        sunRadii[j],
                                        shadowIntensities[k],
                                        shadowSoftness
                                    );

                                shadowMultiplier *=
                                    s;

                                if (shadowMultiplier <
                                    0.01)
                                {
                                    break;
                                }
                            }
                        }

                        float lightIntensity =
                            (
                                diffTerm +
                                scatter +
                                ambient
                            )
                            *
                            attenuation
                            *
                            _Brightness
                            *
                            glowBoost
                            *
                            shadowMultiplier;

                        lightIntensity =
                            max(
                                lightIntensity,
                                0.0
                            );

                        totalLight +=
                            starColor *
                            lightIntensity;
                    }

                    totalLight +=
                        ambientLight;
                }
                else
                {
                    float3 lightDir =
                        normalize(
                            _WorldSpaceLightPos0.xyz
                        );

                    float ndotl =
                        abs(
                            dot(
                                normal,
                                lightDir
                            )
                        );

                    totalLight =
                        _LightColor0.rgb *
                        (
                            ndotl *
                            _AlbedoStrength
                            +
                            0.1
                        )
                        *
                        _Brightness;
                }

                float3 finalRGB =
                    totalLight *
                    _Color.rgb *
                    texColor.rgb;

                float finalAlpha =
                    texColor.a *
                    _Color.a;

                return float4(
                    finalRGB,
                    finalAlpha
                );
            }

            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
