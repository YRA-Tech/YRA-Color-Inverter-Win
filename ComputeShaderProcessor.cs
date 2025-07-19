using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace ColorInverter
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VideoMaskData
    {
        public uint VideoWindowCount;
        public uint ScreenWidth;
        public uint ScreenHeight;
        public uint Padding;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public Int4[] VideoWindows;

        public VideoMaskData(uint width, uint height)
        {
            VideoWindowCount = 0;
            ScreenWidth = width;
            ScreenHeight = height;
            Padding = 0;
            VideoWindows = new Int4[32];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Int4
    {
        public int X, Y, Z, W;

        public Int4(int x, int y, int z, int w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public static Int4 FromRectangle(Rectangle rect)
        {
            return new Int4(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }

    public class ComputeShaderProcessor : IDisposable
    {
        private readonly D3D11DeviceManager deviceManager;
        private ComputeShader colorInvertShader;
        private Buffer constantBuffer;
        private VideoMaskData maskData;
        private bool disposed = false;

        // Shader variants
        private ComputeShader basicInvertShader;
        private ComputeShader gammaInvertShader;
        private ComputeShader optimizedInvertShader;

        public ComputeShaderProcessor(D3D11DeviceManager deviceManager)
        {
            this.deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            
            if (!deviceManager.SupportsComputeShaders())
            {
                throw new NotSupportedException("Compute shaders are not supported on this device");
            }

            InitializeShaders();
        }

        private void InitializeShaders()
        {
            try
            {
                // Compile the compute shader from HLSL file
                var shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColorInvertShader.hlsl");
                
                if (!File.Exists(shaderPath))
                {
                    // Create embedded shader if file doesn't exist
                    CreateEmbeddedShaderFile(shaderPath);
                }

                // Compile different shader variants
                using (var shaderBytecode = ShaderBytecode.CompileFromFile(shaderPath, "ColorInvertCS", "cs_5_0"))
                {
                    basicInvertShader = new ComputeShader(deviceManager.Device, shaderBytecode);
                }

                using (var shaderBytecode = ShaderBytecode.CompileFromFile(shaderPath, "ColorInvertGammaCS", "cs_5_0"))
                {
                    gammaInvertShader = new ComputeShader(deviceManager.Device, shaderBytecode);
                }

                using (var shaderBytecode = ShaderBytecode.CompileFromFile(shaderPath, "ColorInvertOptimizedCS", "cs_5_0"))
                {
                    optimizedInvertShader = new ComputeShader(deviceManager.Device, shaderBytecode);
                }

                // Use optimized version by default
                colorInvertShader = optimizedInvertShader;

                // Create constant buffer for video mask data
                maskData = new VideoMaskData(1920, 1080); // Will be updated with actual dimensions
                constantBuffer = deviceManager.CreateConstantBuffer(maskData);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize compute shaders: {ex.Message}", ex);
            }
        }

        private void CreateEmbeddedShaderFile(string path)
        {
            // If HLSL file doesn't exist, create it with embedded shader code
            // This is a fallback for deployment scenarios
            var shaderCode = @"
Texture2D<float4> InputTexture : register(t0);
RWTexture2D<float4> OutputTexture : register(u0);

cbuffer VideoMaskData : register(b0)
{
    uint VideoWindowCount;
    uint ScreenWidth;
    uint ScreenHeight;
    uint Padding;
    int4 VideoWindows[32];
};

bool IsInVideoWindow(uint2 pixelPos)
{
    [unroll(32)]
    for (uint i = 0; i < VideoWindowCount && i < 32; ++i)
    {
        int4 rect = VideoWindows[i];
        if (pixelPos.x >= rect.x && pixelPos.x < rect.z &&
            pixelPos.y >= rect.y && pixelPos.y < rect.w)
        {
            return true;
        }
    }
    return false;
}

[numthreads(16, 16, 1)]
void ColorInvertCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ScreenWidth || id.y >= ScreenHeight)
        return;
    
    float4 inputColor = InputTexture[id.xy];
    bool skipInversion = IsInVideoWindow(id.xy);
    
    float4 outputColor = skipInversion ? inputColor : float4(1.0 - inputColor.rgb, inputColor.a);
    OutputTexture[id.xy] = outputColor;
}

[numthreads(16, 16, 1)]
void ColorInvertGammaCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ScreenWidth || id.y >= ScreenHeight)
        return;
    
    float4 inputColor = InputTexture[id.xy];
    
    if (!IsInVideoWindow(id.xy))
    {
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

[numthreads(16, 16, 1)]
void ColorInvertOptimizedCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ScreenWidth || id.y >= ScreenHeight)
        return;
    
    float4 inputColor = InputTexture[id.xy];
    float maskValue = IsInVideoWindow(id.xy) ? 0.0 : 1.0;
    float3 invertedColor = 1.0 - inputColor.rgb;
    float3 finalColor = lerp(inputColor.rgb, invertedColor, maskValue);
    
    OutputTexture[id.xy] = float4(finalColor, inputColor.a);
}
";
            File.WriteAllText(path, shaderCode);
        }

        public void UpdateVideoWindows(List<Rectangle> videoWindows)
        {
            maskData.VideoWindowCount = (uint)Math.Min(videoWindows.Count, 32);
            
            // Clear existing windows
            for (int i = 0; i < 32; i++)
            {
                maskData.VideoWindows[i] = new Int4(0, 0, 0, 0);
            }

            // Copy video window rectangles
            for (int i = 0; i < maskData.VideoWindowCount; i++)
            {
                maskData.VideoWindows[i] = Int4.FromRectangle(videoWindows[i]);
            }

            // Update constant buffer
            deviceManager.UpdateConstantBuffer(constantBuffer, maskData);
        }

        public void UpdateScreenDimensions(uint width, uint height)
        {
            maskData.ScreenWidth = width;
            maskData.ScreenHeight = height;
            deviceManager.UpdateConstantBuffer(constantBuffer, maskData);
        }

        public void ProcessTexture(Texture2D inputTexture, Texture2D outputTexture, ShaderVariant variant = ShaderVariant.Optimized)
        {
            var context = deviceManager.DeviceContext;

            // Select shader variant
            ComputeShader shaderToUse = variant switch
            {
                ShaderVariant.Basic => basicInvertShader,
                ShaderVariant.Gamma => gammaInvertShader,
                ShaderVariant.Optimized => optimizedInvertShader,
                _ => optimizedInvertShader
            };

            // Set compute shader
            context.ComputeShader.Set(shaderToUse);

            // Create shader resource view for input
            using (var inputSRV = deviceManager.CreateShaderResourceView(inputTexture))
            {
                context.ComputeShader.SetShaderResource(0, inputSRV);
            }

            // Create unordered access view for output
            using (var outputUAV = deviceManager.CreateUnorderedAccessView(outputTexture))
            {
                context.ComputeShader.SetUnorderedAccessView(0, outputUAV);
            }

            // Set constant buffer
            context.ComputeShader.SetConstantBuffer(0, constantBuffer);

            // Calculate dispatch dimensions (16x16 thread groups)
            var dispatchX = (maskData.ScreenWidth + 15) / 16;
            var dispatchY = (maskData.ScreenHeight + 15) / 16;

            // Dispatch compute shader
            context.Dispatch((int)dispatchX, (int)dispatchY, 1);

            // Unbind resources
            context.ComputeShader.SetShaderResource(0, null);
            context.ComputeShader.SetUnorderedAccessView(0, null);
            context.ComputeShader.Set(null);
        }

        public void SetShaderVariant(ShaderVariant variant)
        {
            colorInvertShader = variant switch
            {
                ShaderVariant.Basic => basicInvertShader,
                ShaderVariant.Gamma => gammaInvertShader,
                ShaderVariant.Optimized => optimizedInvertShader,
                _ => optimizedInvertShader
            };
        }

        public void Dispose()
        {
            if (!disposed)
            {
                basicInvertShader?.Dispose();
                gammaInvertShader?.Dispose();
                optimizedInvertShader?.Dispose();
                constantBuffer?.Dispose();
                disposed = true;
            }
        }
    }

    public enum ShaderVariant
    {
        Basic,      // Simple color inversion
        Gamma,      // Gamma-corrected inversion
        Optimized   // Branch-optimized version
    }
}