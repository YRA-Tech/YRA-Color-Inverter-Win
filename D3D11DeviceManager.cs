using System;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace ColorInverter
{
    public class D3D11DeviceManager : IDisposable
    {
        private Device device;
        private DeviceContext deviceContext;
        private bool disposed = false;

        public Device Device => device;
        public DeviceContext DeviceContext => deviceContext;

        public D3D11DeviceManager()
        {
            InitializeDevice();
        }

        private void InitializeDevice()
        {
            // Create D3D11 device with hardware acceleration
            var creationFlags = DeviceCreationFlags.None;

#if DEBUG
            // Enable debug layer in debug builds
            creationFlags |= DeviceCreationFlags.Debug;
#endif

            // Feature levels to try (prefer higher levels for better performance)
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            };

            try
            {
                // Try to create hardware device first
                device = new Device(DriverType.Hardware, creationFlags, featureLevels);
            }
            catch (SharpDXException)
            {
                try
                {
                    // Fallback to WARP (software) renderer
                    device = new Device(DriverType.Warp, creationFlags, featureLevels);
                }
                catch (SharpDXException)
                {
                    // Last resort: reference rasterizer
                    device = new Device(DriverType.Reference, creationFlags, featureLevels);
                }
            }

            deviceContext = device.ImmediateContext;

            // Verify compute shader support
            if (device.FeatureLevel < FeatureLevel.Level_10_0)
            {
                throw new NotSupportedException("DirectX 10 or higher is required for compute shader support");
            }
        }

        public bool SupportsComputeShaders()
        {
            return device.FeatureLevel >= FeatureLevel.Level_10_0;
        }

        public bool SupportsDirectXInterop()
        {
            try
            {
                // Test if we can create a shared texture
                var textureDesc = new Texture2DDescription
                {
                    Width = 1,
                    Height = 1,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.Shared
                };

                using (var testTexture = new Texture2D(device, textureDesc))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public Texture2D CreateTexture2D(int width, int height, Format format = Format.B8G8R8A8_UNorm, 
            BindFlags bindFlags = BindFlags.ShaderResource, bool shared = false)
        {
            var textureDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = bindFlags,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = shared ? ResourceOptionFlags.Shared : ResourceOptionFlags.None
            };

            return new Texture2D(device, textureDesc);
        }

        public Texture2D CreateStagingTexture(int width, int height, Format format = Format.B8G8R8A8_UNorm)
        {
            var textureDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None
            };

            return new Texture2D(device, textureDesc);
        }

        public ShaderResourceView CreateShaderResourceView(Texture2D texture)
        {
            return new ShaderResourceView(device, texture);
        }

        public UnorderedAccessView CreateUnorderedAccessView(Texture2D texture)
        {
            return new UnorderedAccessView(device, texture);
        }

        public SharpDX.Direct3D11.Buffer CreateConstantBuffer<T>(T data) where T : struct
        {
            var bufferDesc = new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<T>(),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            };

            var buffer = new SharpDX.Direct3D11.Buffer(device, bufferDesc);
            
            // Upload initial data
            var dataStream = deviceContext.MapSubresource(buffer, MapMode.WriteDiscard, MapFlags.None);
            dataStream.Write(data);
            deviceContext.UnmapSubresource(buffer, 0);

            return buffer;
        }

        public void UpdateConstantBuffer<T>(SharpDX.Direct3D11.Buffer buffer, T data) where T : struct
        {
            var dataStream = deviceContext.MapSubresource(buffer, MapMode.WriteDiscard, MapFlags.None);
            dataStream.Write(data);
            deviceContext.UnmapSubresource(buffer, 0);
        }

        public void CopyTexture(Texture2D source, Texture2D destination)
        {
            deviceContext.CopyResource(source, destination);
        }

        public void CopySubresourceRegion(Texture2D source, Texture2D destination, 
            int sourceX, int sourceY, int width, int height, int destX = 0, int destY = 0)
        {
            var sourceRegion = new ResourceRegion
            {
                Left = sourceX,
                Top = sourceY,
                Right = sourceX + width,
                Bottom = sourceY + height,
                Front = 0,
                Back = 1
            };

            deviceContext.CopySubresourceRegion(source, 0, sourceRegion, destination, 0, destX, destY, 0);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                deviceContext?.Dispose();
                device?.Dispose();
                disposed = true;
            }
        }
    }
}