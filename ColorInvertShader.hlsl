// Color Inversion Compute Shader with Video Window Masking
// Processes screen capture textures in parallel on GPU

// Input texture (screen capture)
Texture2D<float4> InputTexture : register(t0);

// Output texture (color inverted result)  
RWTexture2D<float4> OutputTexture : register(u0);

// Video window mask buffer (rectangles defining video regions)
cbuffer VideoMaskData : register(b0)
{
    uint VideoWindowCount;      // Number of video windows
    uint ScreenWidth;           // Screen dimensions for bounds checking
    uint ScreenHeight;
    uint Padding;               // Align to 16 bytes
    
    // Video window rectangles (up to 32 windows)
    // Each rectangle is stored as [left, top, right, bottom]
    int4 VideoWindows[32];
};

// Check if a pixel coordinate is inside any video window
bool IsInVideoWindow(uint2 pixelPos)
{
    [unroll(32)]
    for (uint i = 0; i < VideoWindowCount && i < 32; ++i)
    {
        int4 rect = VideoWindows[i];
        
        // Check if pixel is within rectangle bounds
        if (pixelPos.x >= rect.x && pixelPos.x < rect.z &&
            pixelPos.y >= rect.y && pixelPos.y < rect.w)
        {
            return true;
        }
    }
    
    return false;
}

// Compute shader main function
// Processes pixels in 16x16 thread groups for optimal GPU utilization
[numthreads(16, 16, 1)]
void ColorInvertCS(uint3 id : SV_DispatchThreadID)
{
    // Bounds check to prevent out-of-range access
    if (id.x >= ScreenWidth || id.y >= ScreenHeight)
        return;
    
    // Sample input pixel
    float4 inputColor = InputTexture[id.xy];
    
    // Check if this pixel is in a video window
    bool skipInversion = IsInVideoWindow(id.xy);
    
    float4 outputColor;
    
    if (skipInversion)
    {
        // Keep original color for video regions
        outputColor = inputColor;
    }
    else
    {
        // Invert RGB channels, preserve alpha
        outputColor = float4(1.0 - inputColor.rgb, inputColor.a);
    }
    
    // Write result to output texture
    OutputTexture[id.xy] = outputColor;
}

// Alternative version with gamma correction for better visual results
[numthreads(16, 16, 1)]
void ColorInvertGammaCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ScreenWidth || id.y >= ScreenHeight)
        return;
    
    float4 inputColor = InputTexture[id.xy];
    
    if (!IsInVideoWindow(id.xy))
    {
        // Convert to linear space, invert, convert back to gamma space
        float3 linearColor = pow(inputColor.rgb, 2.2);
        float3 invertedLinear = 1.0 - linearColor;
        float3 invertedGamma = pow(invertedLinear, 1.0 / 2.2);
        
        OutputTexture[id.xy] = float4(invertedGamma, inputColor.a);
    }
    else
    {
        OutputTexture[id.xy] = inputColor;
    }
}

// High-performance version with optimized branching
[numthreads(16, 16, 1)]
void ColorInvertOptimizedCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ScreenWidth || id.y >= ScreenHeight)
        return;
    
    float4 inputColor = InputTexture[id.xy];
    
    // Use branchless selection for better GPU performance
    float maskValue = IsInVideoWindow(id.xy) ? 0.0 : 1.0;
    
    // Lerp between original and inverted based on mask
    float3 invertedColor = 1.0 - inputColor.rgb;
    float3 finalColor = lerp(inputColor.rgb, invertedColor, maskValue);
    
    OutputTexture[id.xy] = float4(finalColor, inputColor.a);
}