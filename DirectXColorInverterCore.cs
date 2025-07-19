using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpDX.Direct3D11;

namespace ColorInverter
{
    public class DirectXColorInverterCore : IDisposable
    {
        private readonly Rectangle monitorRect;
        private readonly VideoWindowDetector videoDetector;
        private D3D11DeviceManager deviceManager;
        private DXGIDesktopDuplication desktopDuplication;
        private ComputeShaderProcessor shaderProcessor;
        
        private Texture2D capturedTexture;
        private Texture2D processedTexture;
        private Texture2D sharedTexture;
        
        private bool running;
        private bool inversionActive;
        private CancellationTokenSource? cancellationTokenSource;
        private Task? processingTask;
        
        // WPF integration
        private D3DImage? d3dImage;
        private WriteableBitmap? fallbackBitmap;
        private bool useD3DImageFallback = false;

        public event Action<ImageSource>? FrameProcessed;
        public bool IsRunning => running;
        public bool IsInversionActive => inversionActive;

        public DirectXColorInverterCore(Rectangle monitorRect, VideoWindowDetector videoDetector)
        {
            this.monitorRect = monitorRect;
            this.videoDetector = videoDetector;

            InitializeDirectX();
        }

        private void InitializeDirectX()
        {
            try
            {
                // Initialize DirectX components
                deviceManager = new D3D11DeviceManager();
                desktopDuplication = new DXGIDesktopDuplication(deviceManager.Device, monitorRect);
                shaderProcessor = new ComputeShaderProcessor(deviceManager);

                // Update shader with screen dimensions
                shaderProcessor.UpdateScreenDimensions((uint)monitorRect.Width, (uint)monitorRect.Height);

                // Create textures for processing pipeline
                capturedTexture = deviceManager.CreateTexture2D(
                    monitorRect.Width, 
                    monitorRect.Height, 
                    SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                    BindFlags.ShaderResource);

                processedTexture = deviceManager.CreateTexture2D(
                    monitorRect.Width, 
                    monitorRect.Height, 
                    SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                    BindFlags.ShaderResource | BindFlags.UnorderedAccess);

                // Try to create shared texture for WPF D3DImage interop
                if (deviceManager.SupportsDirectXInterop())
                {
                    sharedTexture = deviceManager.CreateTexture2D(
                        monitorRect.Width, 
                        monitorRect.Height, 
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        BindFlags.ShaderResource | BindFlags.RenderTarget,
                        shared: true);

                    // Initialize D3DImage for WPF integration
                    InitializeD3DImage();
                }
                else
                {
                    // Fallback to WriteableBitmap
                    useD3DImageFallback = true;
                    fallbackBitmap = new WriteableBitmap(
                        monitorRect.Width, 
                        monitorRect.Height, 
                        96, 96, 
                        PixelFormats.Bgra32, 
                        null);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize DirectX pipeline: {ex.Message}", ex);
            }
        }

        private void InitializeD3DImage()
        {
            if (sharedTexture == null) return;

            try
            {
                d3dImage = new D3DImage();
                d3dImage.Lock();

                // Get shared handle for D3DImage
                using (var resource = sharedTexture.QueryInterface<SharpDX.DXGI.Resource>())
                {
                    var sharedHandle = resource.SharedHandle;
                    d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, sharedHandle);
                }

                d3dImage.Unlock();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize D3DImage: {ex.Message}");
                
                // Fallback to WriteableBitmap
                useD3DImageFallback = true;
                d3dImage?.Freeze();
                d3dImage = null;
                
                fallbackBitmap = new WriteableBitmap(
                    monitorRect.Width, 
                    monitorRect.Height, 
                    96, 96, 
                    PixelFormats.Bgra32, 
                    null);
            }
        }

        public void Start()
        {
            if (running) return;

            running = true;
            cancellationTokenSource = new CancellationTokenSource();
            
            // Start processing loop
            processingTask = Task.Run(() => ProcessingLoop(cancellationTokenSource.Token));
        }

        public void Stop()
        {
            if (!running) return;

            running = false;
            inversionActive = false;
            
            cancellationTokenSource?.Cancel();
            
            try
            {
                processingTask?.Wait(5000); // Wait up to 5 seconds
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions
            }
            
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            processingTask = null;
        }

        public void SetInversionActive(bool active)
        {
            inversionActive = active;
        }

        private async Task ProcessingLoop(CancellationToken cancellationToken)
        {
            const int targetFps = 60;
            const int frameTimeMs = 1000 / targetFps;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var frameStart = Environment.TickCount;
                
                try
                {
                    if (inversionActive)
                    {
                        await ProcessFrame();
                    }
                    else
                    {
                        // When not active, just sleep to reduce CPU usage
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in processing loop: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                // Frame rate limiting
                var frameTime = Environment.TickCount - frameStart;
                var sleepTime = frameTimeMs - frameTime;
                
                if (sleepTime > 0)
                {
                    await Task.Delay(sleepTime, cancellationToken);
                }
            }
        }

        private async Task ProcessFrame()
        {
            // Capture screen using DXGI Desktop Duplication
            if (!desktopDuplication.TryGetNextFrame(out var frameTexture, timeoutMs: 16))
            {
                // No new frame available
                return;
            }

            try
            {
                // Copy captured frame to our processing texture
                deviceManager.CopyTexture(frameTexture, capturedTexture);

                // Update video window masks
                await UpdateVideoWindowMasks();

                // Process with compute shader
                shaderProcessor.ProcessTexture(capturedTexture, processedTexture, ShaderVariant.Optimized);

                // Present to WPF
                await PresentToWPF();
            }
            finally
            {
                // Always release the frame
                frameTexture?.Dispose();
                desktopDuplication.ReleaseFrame();
            }
        }

        private async Task UpdateVideoWindowMasks()
        {
            // Get video windows from detector (run on background thread to avoid blocking GPU)
            var videoWindows = await Task.Run(() =>
            {
                var windows = videoDetector.GetVideoWindows();
                return windows.Select(w => new Rectangle(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top)).ToList();
            });

            // Update shader with new video window data
            shaderProcessor.UpdateVideoWindows(videoWindows);
        }

        private async Task PresentToWPF()
        {
            if (useD3DImageFallback || d3dImage == null)
            {
                await PresentToWriteableBitmap();
            }
            else
            {
                await PresentToD3DImage();
            }
        }

        private async Task PresentToD3DImage()
        {
            if (d3dImage == null || sharedTexture == null) return;

            // Copy processed texture to shared texture for D3DImage
            deviceManager.CopyTexture(processedTexture, sharedTexture);

            // Update D3DImage on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    d3dImage.Lock();
                    d3dImage.AddDirtyRect(new Int32Rect(0, 0, monitorRect.Width, monitorRect.Height));
                    d3dImage.Unlock();

                    FrameProcessed?.Invoke(d3dImage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating D3DImage: {ex.Message}");
                }
            });
        }

        private async Task PresentToWriteableBitmap()
        {
            if (fallbackBitmap == null) return;

            // Copy texture to CPU memory for WriteableBitmap (fallback path)
            using (var stagingTexture = deviceManager.CreateStagingTexture(monitorRect.Width, monitorRect.Height))
            {
                deviceManager.CopyTexture(processedTexture, stagingTexture);

                // Map staging texture to get pixel data
                var dataBox = deviceManager.DeviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);
                
                try
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            fallbackBitmap.Lock();
                            
                            // Copy pixel data to WriteableBitmap
                            unsafe
                            {
                                var source = (byte*)dataBox.DataPointer;
                                var dest = (byte*)fallbackBitmap.BackBuffer;
                                var sourceStride = dataBox.RowPitch;
                                var destStride = fallbackBitmap.BackBufferStride;

                                for (int y = 0; y < monitorRect.Height; y++)
                                {
                                    var sourceLine = source + (y * sourceStride);
                                    var destLine = dest + (y * destStride);
                                    
                                    for (int x = 0; x < monitorRect.Width * 4; x++)
                                    {
                                        destLine[x] = sourceLine[x];
                                    }
                                }
                            }

                            fallbackBitmap.AddDirtyRect(new Int32Rect(0, 0, monitorRect.Width, monitorRect.Height));
                            fallbackBitmap.Unlock();

                            FrameProcessed?.Invoke(fallbackBitmap);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error updating WriteableBitmap: {ex.Message}");
                        }
                    });
                }
                finally
                {
                    deviceManager.DeviceContext.UnmapSubresource(stagingTexture, 0);
                }
            }
        }

        public void Dispose()
        {
            Stop();

            sharedTexture?.Dispose();
            processedTexture?.Dispose();
            capturedTexture?.Dispose();
            shaderProcessor?.Dispose();
            desktopDuplication?.Dispose();
            deviceManager?.Dispose();

            d3dImage?.Freeze();
            d3dImage = null;
            fallbackBitmap = null;
        }
    }
}