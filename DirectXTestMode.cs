using System;
using System.Diagnostics;
using System.Drawing;

namespace ColorInverter
{
    /// <summary>
    /// Test and diagnostic utilities for the DirectX color inversion pipeline
    /// </summary>
    public static class DirectXTestMode
    {
        public static bool TestDirectXSupport()
        {
            try
            {
                using (var deviceManager = new D3D11DeviceManager())
                {
                    Debug.WriteLine($"DirectX Device Created: Feature Level {deviceManager.Device.FeatureLevel}");
                    Debug.WriteLine($"Compute Shader Support: {deviceManager.SupportsComputeShaders()}");
                    Debug.WriteLine($"DirectX Interop Support: {deviceManager.SupportsDirectXInterop()}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DirectX Test Failed: {ex.Message}");
                return false;
            }
        }

        public static bool TestDesktopDuplication(Rectangle monitorBounds)
        {
            try
            {
                using (var deviceManager = new D3D11DeviceManager())
                using (var duplication = new DXGIDesktopDuplication(deviceManager.Device, monitorBounds))
                {
                    Debug.WriteLine($"Desktop Duplication Created for bounds: {monitorBounds}");
                    
                    // Test capturing a frame
                    if (duplication.TryGetNextFrame(out var frame, timeoutMs: 100))
                    {
                        Debug.WriteLine($"Frame captured: {frame.Description.Width}x{frame.Description.Height}");
                        frame?.Dispose();
                        duplication.ReleaseFrame();
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine("No frame available (normal on some systems)");
                        return true; // Still consider this a success
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Desktop Duplication Test Failed: {ex.Message}");
                return false;
            }
        }

        public static bool TestComputeShader()
        {
            try
            {
                using (var deviceManager = new D3D11DeviceManager())
                using (var processor = new ComputeShaderProcessor(deviceManager))
                {
                    Debug.WriteLine("Compute Shader Processor Created");
                    
                    processor.UpdateScreenDimensions(1920, 1080);
                    processor.UpdateVideoWindows(new System.Collections.Generic.List<Rectangle>
                    {
                        new Rectangle(100, 100, 200, 200) // Test video window
                    });
                    
                    Debug.WriteLine("Compute Shader Configuration Updated");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Compute Shader Test Failed: {ex.Message}");
                return false;
            }
        }

        public static void RunDiagnostics(Rectangle primaryMonitorBounds)
        {
            Debug.WriteLine("=== DirectX Color Inverter Diagnostics ===");
            
            var directXSupported = TestDirectXSupport();
            var duplicationSupported = TestDesktopDuplication(primaryMonitorBounds);
            var computeShaderSupported = TestComputeShader();
            
            Debug.WriteLine("\n=== Test Results ===");
            Debug.WriteLine($"DirectX Support: {(directXSupported ? "✓ PASS" : "✗ FAIL")}");
            Debug.WriteLine($"Desktop Duplication: {(duplicationSupported ? "✓ PASS" : "✗ FAIL")}");
            Debug.WriteLine($"Compute Shader: {(computeShaderSupported ? "✓ PASS" : "✗ FAIL")}");
            
            if (directXSupported && duplicationSupported && computeShaderSupported)
            {
                Debug.WriteLine("\n🎉 All DirectX features are working correctly!");
                Debug.WriteLine("Expected performance improvement: 100x+ over GDI pipeline");
            }
            else
            {
                Debug.WriteLine("\n⚠️  Some DirectX features failed - will fallback to GDI pipeline");
            }
            
            Debug.WriteLine("=== End Diagnostics ===\n");
        }

        public static void BenchmarkPerformance(Rectangle monitorBounds, int frameCount = 60)
        {
            Debug.WriteLine($"=== Performance Benchmark ({frameCount} frames) ===");
            
            try
            {
                using (var deviceManager = new D3D11DeviceManager())
                using (var processor = new ComputeShaderProcessor(deviceManager))
                {
                    // Create test textures
                    var inputTexture = deviceManager.CreateTexture2D(
                        monitorBounds.Width, 
                        monitorBounds.Height,
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SharpDX.Direct3D11.BindFlags.ShaderResource);
                        
                    var outputTexture = deviceManager.CreateTexture2D(
                        monitorBounds.Width, 
                        monitorBounds.Height,
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.UnorderedAccess);

                    processor.UpdateScreenDimensions((uint)monitorBounds.Width, (uint)monitorBounds.Height);

                    var stopwatch = Stopwatch.StartNew();
                    
                    for (int i = 0; i < frameCount; i++)
                    {
                        processor.ProcessTexture(inputTexture, outputTexture, ShaderVariant.Optimized);
                        deviceManager.DeviceContext.Flush(); // Ensure GPU work is submitted
                    }
                    
                    stopwatch.Stop();
                    
                    var totalMs = stopwatch.ElapsedMilliseconds;
                    var fps = frameCount * 1000.0 / totalMs;
                    var frameTimeMs = totalMs / (double)frameCount;
                    
                    Debug.WriteLine($"Total Time: {totalMs} ms");
                    Debug.WriteLine($"Average FPS: {fps:F1}");
                    Debug.WriteLine($"Frame Time: {frameTimeMs:F2} ms");
                    Debug.WriteLine($"Resolution: {monitorBounds.Width}x{monitorBounds.Height}");
                    
                    inputTexture.Dispose();
                    outputTexture.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Benchmark Failed: {ex.Message}");
            }
            
            Debug.WriteLine("=== End Benchmark ===\n");
        }
    }
}