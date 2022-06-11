﻿using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json;
using SharpTracer.Core.Logging;
using SharpTracer.Core.Renderer;
using SharpTracer.Core.Scene;
using SharpTracer.Core.Settings;
using SharpTracer.Core.Utility;
using SimpleImageIO;

namespace SharpTracer.Core;

internal class Program
{
    private static async Task Main(string[] args)
    {
        const string pathSettings = "settings.json";

        RenderSettings settings;
        try
        {
            string jsonText = await File.ReadAllTextAsync(pathSettings);
            settings = JsonSerializer.Deserialize<RenderSettings>(jsonText);
            if (settings is null)
            {
                throw new Exception();
            }
        }
        catch
        {
            ConsoleLogger.Get().LogError("Failed to load settings");
            throw;
        }

        string fullPath = Path.Combine(settings.FolderPath, settings.FileName);
        const int maxDepth = 20;

        IScene scene = new EarthScene();
        BvhNode node = new(scene.Render(), 0f, 1f);

        IEyeView eye = new LevelCamera();
        Camera camera = eye.GetCamera();

        // Render
        var img = new RgbImage(camera.Width, camera.Height);
        const float gamma = 1.2f;

        // Spawn tasks with N scanlines
        int scanlinesLeft = camera.Height;
        int scanlinesPerTask = camera.Height / settings.NumTasks;
        int leftoverScanlines = camera.Height % settings.NumTasks;
        List<Task> scanlineTasks = new();
        for (int y = 0; y < camera.Height - leftoverScanlines; y += scanlinesPerTask)
        {
            int threadLocalY = y;
            scanlineTasks.Add(Task.Run(() =>
            {
                Random localRng = new();
                for (int scanline = threadLocalY; scanline < threadLocalY + scanlinesPerTask; scanline++)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    CalculateScanline(localRng, scanline, camera, node, maxDepth, img, gamma, ref scanlinesLeft);
                }
            }));
        }

        for (int y = camera.Height - leftoverScanlines; y < camera.Height; y++)
        {
            int threadLocalY = y;
            scanlineTasks.Add(Task.Run(() =>
            {
                Random localRng = new();
                // ReSharper disable once AccessToDisposedClosure
                CalculateScanline(localRng, threadLocalY, camera, node, maxDepth, img, gamma, ref scanlinesLeft);
            }));
        }

        // Execute
        Stopwatch sw = Stopwatch.StartNew();
        ConsoleLogger.Get().LogInfo("Start pathtracing");
        await Task.WhenAll(scanlineTasks);
        ConsoleLogger.Get().LogInfo("Done pathtracing");

        RgbImage image = img;
        if (settings.Denoise)
        {
            ConsoleLogger.Get().LogInfo("Start denoising");
            using (Denoiser denoiser = new())
            {
                image = denoiser.Denoise(img);
            }

            img.Dispose();

            ConsoleLogger.Get().LogInfo("Done denoising");
        }

        ConsoleLogger.Get().LogInfo("Start writing to disk");
        image.WriteToFile(fullPath);
        ConsoleLogger.Get().LogInfo("Done writing to disk");
        sw.Stop();
        ConsoleLogger.Get().LogInfo($"Rendering done, time elapsed: {sw.Elapsed}");
        sw.Reset();
        image.Dispose();

        ConsoleLogger.Get().LogInfo("Opening");
        ProcessStartInfo info = new(fullPath) { UseShellExecute = true };
        Process.Start(info);
        ConsoleLogger.Get().LogInfo("Done");
    }

    private static void CalculateScanline(
        Random rng,
        int y,
        Camera camera,
        IHittable world,
        int maxDepth,
        RgbImage renderer,
        float gamma,
        ref int scanlineCount)
    {
        for (int x = 0; x < camera.Width; x++)
        {
            Vector3 colorVec = Vector3.Zero;
            for (int s = 0; s < ColorHelper.SamplesPerPixel; s++)
            {
                float u = (x + rng.NextSingle()) / camera.Width;
                float v = (y + rng.NextSingle()) / camera.Height;
                Ray ray = camera.GetRay(rng, u, v);
                colorVec += RayColor(ray, world, maxDepth);
            }

            renderer.SetPixel(x, camera.Height - 1 - y, ColorHelper.WriteColor(colorVec, gamma));
        }

        ConsoleLogger.Get().LogInfo($"Scanlines remaining: {scanlineCount--}");
    }

    private static Vector3 RayColor(Ray ray, IHittable world, int stackDepth)
    {
        if (stackDepth <= 0)
        {
            return Vector3.Zero;
        }

        HitRecord hit = new();
        if (world.Hit(ray, 0.001f, float.PositiveInfinity, ref hit))
        {
            hit.Material.Scatter(ray, hit, out Color attenuation, out Ray outRay);
            return attenuation.ToVector3() * RayColor(outRay, world, stackDepth - 1);
        }

        Color topColor = Color.LightSkyBlue;
        Color bottomColor = Color.White;
        Vector3 dir = Vector3.Normalize(ray.Direction);
        float t = 0.5f * (dir.Y + 1f);
        return (1f - t) * bottomColor.ToVector3() + t * topColor.ToVector3();
    }
}
