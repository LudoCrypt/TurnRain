Shader "Futile/TRDeathRain"
{
	Properties
	{
		_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
	}
	SubShader
	{
		Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
		Blend SrcAlpha OneMinusSrcAlpha
		Fog { Color(0, 0, 0, 0) }
		Cull Off
		Lighting Off
		ZWrite Off

		GrabPass { }
		Pass
		{
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

sampler2D _MainTex;
sampler2D _NoiseTex;
sampler2D _GrabTexture;

uniform float _rainDirection;
uniform float _rainEverywhere;
uniform float _rainIntensity;

uniform float _waterLevel;

uniform float _RAIN;

uniform float4 _RainSpriteRect;

struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
	float2 scrPos : TEXCOORD1;
	float4 clr : COLOR;
};

float4 _MainTex_ST;

v2f vert(appdata_full v) {
	v2f o;
	o.pos = UnityObjectToClipPos (v.vertex);
	o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
	o.scrPos = ComputeScreenPos(o.pos);
	o.clr = v.color;
	return o;
}

// https://www.shadertoy.com/view/lcdSzS

half4 frag(v2f i) : SV_Target {
	float2 uv = i.uv;
	float2 scrPos = i.scrPos;
	float3x3 noiseMotion = float3x3(
		2.0, 0.0, 0.0,
		0.0, 0.25, 0.0,
		0.2 * _RAIN, 3.0 * _RAIN, 1.0
	);
	
	float theta = radians(_rainDirection);
	float cosTheta = cos(theta);
	float sinTheta = sin(theta);

	float3x3 rainAngle = float3x3(
		cosTheta, -sinTheta, 0.0,
		sinTheta, cosTheta, 0.0,
		0.0, 0.0, 1.0
	);

	half2 getPos = mul(noiseMotion * rainAngle, half3(uv, 1.0)).xy;

	half topPos = floor((dot(uv, half2(cosTheta, sinTheta))) * 1683.0) / 683.0;

	float rand = 
	frac(sin(dot(topPos, 12.98232) + _RAIN - tex2D(_NoiseTex, half2(topPos, _RAIN)).x) * 43758.5453) +
	frac(sin(dot(topPos + 1.0/683.0, 12.98232) + _RAIN - tex2D(_NoiseTex, half2(topPos + 1.0 / 683.0, _RAIN)).x) * 43758.5453) +
	frac(sin(dot(topPos - 1.0/683.0, 12.98232) + _RAIN - tex2D(_NoiseTex, half2(topPos - 1.0 / 683.0, _RAIN)).x) * 43758.5453);
	
	rand /= 3.0;

	float3x3 noiseMotion2 = float3x3(
		1.3, 0.0, 0.0,
		0.0, 0.25, 0.0,
		0.0, 2.0 * _RAIN, 1.0
	);
	
	half2 displace2Pos = mul(noiseMotion2 * rainAngle, half3(uv, 1.0)).xy;
	
	half displace2 = 0.5 + 0.5f * sin((tex2D(_NoiseTex, displace2Pos).x + _RAIN * 5.0) * 6.28);
	half displace = 0.5 + 0.5f * sin((tex2D(_NoiseTex, getPos).x + _RAIN * 3.0 + displace2) * 6.28 + uv.y * 7.0 + _RAIN * 46.0 + getPos.x * 4.2);
	
	displace = lerp(displace, displace2, 0.5);
	half geoFac = tex2D(_MainTex, float2(_RainSpriteRect.x + uv.x * _RainSpriteRect.z, _RainSpriteRect.y + uv.y * _RainSpriteRect.w)).x;
	
	half scal = min(_RainSpriteRect.z, _RainSpriteRect.w);
	
	half2 p = uv;
	half beforeStepped = geoFac;
	half fac = 1.0;
	
	[loop]
	for (float i = 0.0; i < (200.0 / scal); i++) {
		p += half2(-(0.01 * scal) * sinTheta, (0.01 * scal) * cosTheta);
		float2 bounds = float2(_RainSpriteRect.x + p.x * _RainSpriteRect.z, _RainSpriteRect.y + p.y * _RainSpriteRect.w);
		half2 mask = tex2D(_MainTex, bounds).xy;

		if (bounds.x > 1.0) {
			break;
		}
		if (bounds.y > 1.0) {
			break;
		}
		if (bounds.x < 0.0) {
			break;
		}
		if (bounds.y < 0.0) {
			break;
		}
		
		if (mask.y > 0.5) {
			break;
		}
		
		if (beforeStepped < mask.x) {
			fac *= mask.x;

			if (fac == 0.0) {
				break;
			}
		} else if (beforeStepped > mask.x) { 
			fac *= mask.x;
		}
		
		beforeStepped = mask.x;
	}
	fac *= geoFac;

	half lightness = (-1.0 + lerp(displace, rand, 0.5) * 2.0) * fac * pow(_rainIntensity, 0.25);

	if(1.0 - rand > _rainIntensity) {
		fac = 0.0;
	} else {
		fac = max(0.0, fac - (1.0 -_rainIntensity));
	}

	if(rand > 1.0 -_rainEverywhere * 1.5) {
		fac = lerp(fac, 1.0, lerp(displace / 5.0, 1.0, max(0.0, _rainEverywhere - 0.85) * 10.0));
		lightness = (-1.0 + lerp(displace, rand, 0.5) * 2.0) * pow(_rainIntensity, 0.1);
	}

	if(scrPos.y < (1.0 - _waterLevel) - 0.14) {
		fac = lerp(fac, 0.0, clamp((((1.0 - _waterLevel) - 0.14) - scrPos.y) * 5.0, 0.0, 1.0));
		lightness *= fac;
	}

	displace = lerp(displace, rand, 0.8);

	fac *= _rainIntensity;

	half4 returnCol = tex2D(_GrabTexture, half2(scrPos.x - displace * 0.05 * rand * displace2 * fac, scrPos.y + (-0.5 + displace) * 0.25 * fac));

	returnCol = lerp(returnCol, half4(0.15 * rand, 0.15 * rand, 0.15 * rand, 1), max(0.0, _rainEverywhere - 0.8) * 3.0);

	if(lightness < -0.3)
		returnCol.xyz *= 0.9;
	else if (lightness > 0.5)
		returnCol.xyz = pow(returnCol.xyz, 0.8);

	return returnCol;
}

			ENDCG
		}
	}
}
