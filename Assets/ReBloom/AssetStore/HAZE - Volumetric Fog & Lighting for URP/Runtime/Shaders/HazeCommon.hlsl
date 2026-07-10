#ifndef HAZECOMMON_INCLUDED
#define HAZECOMMON_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

SamplerState hazeSampler_LinearClamp;
SamplerState hazeSampler_TrilinearClamp;

float LinearToExpDepth(float z, float near, float far)
{
    float z_buffer_params_y = far / near;
    float z_buffer_params_x = 1.0f - z_buffer_params_y;

    return (1.0f / z - z_buffer_params_y) / z_buffer_params_x;
}

float ExpToLinearDepth(float z, float near, float far)
{
    float z_buffer_params_y = far / near;
    float z_buffer_params_x = 1.0f - z_buffer_params_y;

    return 1.0f / (z_buffer_params_x * z + z_buffer_params_y);
}

float3 NDCToWorld(float3 ndc, float4x4 inverseViewProj)
{
    float4 p = mul(inverseViewProj, float4(ndc, 1.0f));
        
    p.x /= p.w;
    p.y /= p.w;
    p.z /= p.w;
        
    return p.xyz;
}

float3 NDCToWorldOrtho(float3 ndc, float depth)
{
    float4x4 viewMatrix = UNITY_MATRIX_V;
    float3 rightOffset = normalize(viewMatrix[0].xyz) * (ndc.x * unity_OrthoParams.x);
    float3 upOffset = normalize(viewMatrix[1].xyz) * (ndc.y * unity_OrthoParams.y);
    float3 fwdOffset = normalize(-viewMatrix[2].xyz) * depth;

    return _WorldSpaceCameraPos + fwdOffset + rightOffset + upOffset;
}

float3 UVToNDC(float3 uv, float near, float far)
{
    float3 ndc;
        
    ndc.x = 2.0f * uv.x - 1.0f;
    ndc.y = 2.0f * uv.y - 1.0f;
    ndc.z = 2.0f * LinearToExpDepth(uv.z, near, far) - 1.0f;
        
    return ndc;
}

float3 IDToUVWithJitter(uint3 id, float near, float far, float jitter, uint3 gridSize)
{
    float view_z = near * pow(abs(far / near), (float(id.z) + 0.5f + jitter) / float(gridSize.z));

    return float3((float(id.x) + 0.5f) / float(gridSize.x),
                (float(id.y) + 0.5f) / float(gridSize.y),
                view_z / far);
}

float3 IDToWorldWithJitter(uint3 id, float jitter, float near, float far, float4x4 invViewProjMatrix, uint3 gridSize)
{
    float3 uv = IDToUVWithJitter(id, near, far, jitter, gridSize);
    float3 ndc = UVToNDC(uv, near, far);
    return unity_OrthoParams.w ? NDCToWorldOrtho(ndc, lerp(near, far, uv.z)) : NDCToWorld(ndc, invViewProjMatrix);
}

float3 IDToWorld(uint3 id, float near, float far, float4x4 invViewProjMatrix, uint3 gridSize)
{
    return IDToWorldWithJitter(id, 0, near, far, invViewProjMatrix, gridSize);
}

float3 WorldToNDC(float3 worldPos, float4x4 viewProjMatrix)
{
    float4 p = mul(viewProjMatrix, float4(worldPos, 1.0f));
        
    if (p.w > 0.0f)
    {
        p.x /= p.w;
        p.y /= p.w;
        p.z /= p.w;
    }
    
    return p.xyz;
}

float3 NDCToUV(float3 ndc, float near, float far, uint depth)
{
    float3 uv;
        
    uv.x = ndc.x * 0.5f + 0.5f;
    uv.y = ndc.y * 0.5f + 0.5f;
    uv.z = ExpToLinearDepth(ndc.z * 0.5f + 0.5f, near, far);

    // Exponential View-Z
    float2 params = float2(float(depth) / log2(far / near), -(float(depth) * log2(near) / log2(far / near)));

    float view_z = uv.z * far;
    uv.z = (max(log2(view_z) * params.x + params.y, 0.0f)) / depth;
    
    return uv;
}

float3 NDCToUVOrtho(float3 ndc, float near, float far, uint depth)
{
    float3 uv;
        
    uv.x = ndc.x * 0.5f + 0.5f;
    uv.y = ndc.y * 0.5f + 0.5f;
    uv.z = ndc.z * 0.5f + 0.5f;

    // Exponential View-Z
    float2 params = float2(float(depth) / log2(far / near), -(float(depth) * log2(near) / log2(far / near)));

    float view_z = uv.z * far;
    uv.z = (max(log2(view_z) * params.x + params.y, 0.0f)) / depth;

    return uv;
}

float InverseLerp(float a, float b, float x)
{
    return (x- a) / (b - a);
}

float3 InverseLerp(float3 a, float3 b, float3 x)
{
    return (x - a) / (b - a);
}

real3 WorldToNDCOrtho(float3 worldPos, float4x4 viewMatrix, float near, float far)
{
    float4 p = mul(viewMatrix, float4(worldPos, 1.0f));
    p.z *= -1.0;
    float3 ndc = InverseLerp(float3(-unity_OrthoParams.x, -unity_OrthoParams.y, near), float3(unity_OrthoParams.x, unity_OrthoParams.y, far), p.xyz) * 2.0 - 1.0;
    return ndc;
}

real3 WorldToUVOrtho(float3 worldPos, float near, float far, float4x4 viewMatrix, uint depth)
{
    float3 ndc = WorldToNDCOrtho(worldPos, viewMatrix, near, far);
    return NDCToUVOrtho(ndc, near, far, depth);
}

float3 WorldToUV(float3 worldPos, float near, float far, float4x4 viewProjMatrix, uint depth)
{
    float3 ndc = WorldToNDC(worldPos, viewProjMatrix);
    return NDCToUV(ndc, near, far, depth);
}

float3 WorldToUV(float3 worldPos, float near, float far, float4x4 inputMatrix, uint depth, int ortho)
{
    return ortho ? WorldToUVOrtho(worldPos, near, far, inputMatrix, depth) : WorldToUV(worldPos, near, far, inputMatrix, depth);
}

float IGN(int pixelX, int pixelY, uint frame)
{
    frame = frame % 64; // need to periodically reset frame to avoid numerical issues
    float x = float(pixelX) + 5.588238f * float(frame);
    float y = float(pixelY) + 5.588238f * float(frame);
    return fmod(52.9829189 * fmod(0.06711056 * x + 0.00583715 * y, 1.0f), 1.0f);
}


float4 Cubic(float v)
{
    float4 n = float4(1.0, 2.0, 3.0, 4.0) - v;
    float4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    float w = 6.0 - x - y - z;
    return float4(x, y, z, w) * (1.0 / 6.0);
}

float4 SampleTexture3DBicubic(Texture3D tex, float3 texCoords, float3 textureSize)
{
    texCoords = texCoords * textureSize - 0.5;

    float3 f = frac(texCoords);
    texCoords -= f;

    float4 xcubic = Cubic(f.x);
    float4 ycubic = Cubic(f.y);
    float4 zcubic = Cubic(f.z);

    float2 cx = texCoords.xx + float2(-0.5, 1.5);
    float2 cy = texCoords.yy + float2(-0.5, 1.5);
    float2 cz = texCoords.zz + float2(-0.5, 1.5);
    float2 sx = xcubic.xz + xcubic.yw;
    float2 sy = ycubic.xz + ycubic.yw;
    float2 sz = zcubic.xz + zcubic.yw;
    float2 offsetx = cx + xcubic.yw / sx;
    float2 offsety = cy + ycubic.yw / sy;
    float2 offsetz = cz + zcubic.yw / sz;
    offsetx /= textureSize.xx;
    offsety /= textureSize.yy;
    offsetz /= textureSize.zz;

    float4 sample0 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.x, offsety.x, offsetz.x), 0);
    float4 sample1 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.y, offsety.x, offsetz.x), 0);
    float4 sample2 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.x, offsety.y, offsetz.x), 0);
    float4 sample3 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.y, offsety.y, offsetz.x), 0);
    float4 sample4 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.x, offsety.x, offsetz.y), 0);
    float4 sample5 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.y, offsety.x, offsetz.y), 0);
    float4 sample6 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.x, offsety.y, offsetz.y), 0);
    float4 sample7 = SAMPLE_TEXTURE3D_LOD(tex, hazeSampler_LinearClamp, float3(offsetx.y, offsety.y, offsetz.y), 0);

    float gx = sx.x / (sx.x + sx.y);
    float gy = sy.x / (sy.x + sy.y);
    float gz = sz.x / (sz.x + sz.y);

    float4 x0 = lerp(sample1, sample0, gx.xxxx);
    float4 x1 = lerp(sample3, sample2, gx.xxxx);
    float4 x2 = lerp(sample5, sample4, gx.xxxx);
    float4 x3 = lerp(sample7, sample6, gx.xxxx);
    float4 y0 = lerp(x1, x0, gy.xxxx);
    float4 y1 = lerp(x3, x2, gy.xxxx);

    return lerp(y1, y0, gz.xxxx);
}

#endif