using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;

namespace TMRJW.Benchmarks
{
    [CPUUsageDiagnoser]
    public class ImageIOBenchmark
    {
        private byte[] _sampleBytes;
        private string _tempDir = string.Empty;
        private string _samplePath = string.Empty;
        [GlobalSetup]
        public void Setup()
        {
            // 1x1 PNG (transparent) base64
            var b64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";
            _sampleBytes = Convert.FromBase64String(b64);
            _tempDir = Path.Combine(Path.GetTempPath(), "tmrjw_bench");
            Directory.CreateDirectory(_tempDir);
            _samplePath = Path.Combine(_tempDir, "sample.png");
            File.WriteAllBytes(_samplePath, _sampleBytes);
        }

        [Benchmark]
        public async Task SaveBytesToCacheAsync()
        {
            var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".png");
            await File.WriteAllBytesAsync(path, _sampleBytes).ConfigureAwait(false);
        }

        [Benchmark]
        public byte[] ReadBytesFromCache()
        {
            return File.ReadAllBytes(_samplePath);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            try
            {
                // Clean temporary files created by the benchmark
                foreach (var f in Directory.GetFiles(_tempDir))
                {
                    try
                    {
                        File.Delete(f);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    Directory.Delete(_tempDir);
                }
                catch
                {
                }
            }
            catch
            {
            }
        }
    }
}