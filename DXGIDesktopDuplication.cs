using System;
using System.Drawing;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace ColorInverter
{
    public class DXGIDesktopDuplication : IDisposable
    {
        private Device device;
        private DeviceContext deviceContext;
        private OutputDuplication outputDuplication;
        private Texture2D desktopTexture;
        private bool disposed = false;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public Rectangle MonitorBounds { get; private set; }

        public DXGIDesktopDuplication(Device d3dDevice, Rectangle monitorBounds)
        {
            device = d3dDevice;
            deviceContext = device.ImmediateContext;
            MonitorBounds = monitorBounds;
            Width = monitorBounds.Width;
            Height = monitorBounds.Height;

            InitializeDesktopDuplication();
        }

        private void InitializeDesktopDuplication()
        {
            // Get DXGI device from D3D11 device
            using (var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>())
            using (var dxgiAdapter = dxgiDevice.Parent.QueryInterface<Adapter>())
            {
                // Find the correct output for our monitor
                Output targetOutput = null;
                for (int i = 0; i < dxgiAdapter.GetOutputCount(); i++)
                {
                    using (var output = dxgiAdapter.GetOutput(i))
                    {
                        var outputDesc = output.Description;
                        var outputBounds = new Rectangle(
                            outputDesc.DesktopBounds.Left,
                            outputDesc.DesktopBounds.Top,
                            outputDesc.DesktopBounds.Right - outputDesc.DesktopBounds.Left,
                            outputDesc.DesktopBounds.Bottom - outputDesc.DesktopBounds.Top
                        );

                        if (outputBounds.X == MonitorBounds.X && outputBounds.Y == MonitorBounds.Y &&
                            outputBounds.Width == MonitorBounds.Width && outputBounds.Height == MonitorBounds.Height)
                        {
                            targetOutput = output;
                            break;
                        }
                    }
                }

                if (targetOutput == null)
                {
                    // Fallback to primary output
                    targetOutput = dxgiAdapter.GetOutput(0);
                }

                // Create desktop duplication
                using (var output1 = targetOutput.QueryInterface<Output1>())
                {
                    outputDuplication = output1.DuplicateOutput(device);
                }
            }
        }

        public bool TryGetNextFrame(out Texture2D frameTexture, int timeoutMs = 1000)
        {
            frameTexture = null;

            try
            {
                SharpDX.DXGI.Resource desktopResource;
                OutputDuplicateFrameInformation frameInfo;

                // Try to get the next frame
                var result = outputDuplication.TryAcquireNextFrame(timeoutMs, out frameInfo, out desktopResource);
                
                if (result.Failure || desktopResource == null)
                {
                    return false;
                }

                // Convert to D3D11 texture
                using (desktopResource)
                {
                    frameTexture = desktopResource.QueryInterface<Texture2D>();
                }

                return true;
            }
            catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.WaitTimeout)
            {
                // No new frame available
                return false;
            }
            catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
            {
                // Desktop duplication lost, needs reinitialization
                ReleaseFrame();
                try
                {
                    outputDuplication?.Dispose();
                    InitializeDesktopDuplication();
                }
                catch
                {
                    // Ignore reinit errors
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void ReleaseFrame()
        {
            try
            {
                outputDuplication?.ReleaseFrame();
            }
            catch
            {
                // Ignore release errors
            }
        }

        public Texture2D CreateCompatibleTexture(BindFlags bindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess)
        {
            var textureDesc = new Texture2DDescription
            {
                Width = Width,
                Height = Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = bindFlags,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };

            return new Texture2D(device, textureDesc);
        }

        public void CopyTexture(Texture2D source, Texture2D destination)
        {
            deviceContext.CopyResource(source, destination);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                ReleaseFrame();
                
                desktopTexture?.Dispose();
                outputDuplication?.Dispose();
                
                disposed = true;
            }
        }
    }
}