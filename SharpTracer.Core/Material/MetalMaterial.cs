﻿using System.Drawing;
using System.Numerics;
using SharpTracer.Core.Geometry;
using SharpTracer.Core.Renderer;
using SharpTracer.Core.Utility;

namespace SharpTracer.Core.Material;

public class MetalMaterial : IMaterial
{
    public MetalMaterial(Color albedo, float fuzziness)
    {
        Albedo = albedo;
        Fuzziness = fuzziness < 1f ? fuzziness : 1f;
    }

    public Color Albedo { get; }
    public float Fuzziness { get; }

    public void Scatter(Ray ray, HitRecord hit, out Color attenuation, out Ray outRay)
    {
        Vector3 reflected = Vector3.Reflect(Vector3.Normalize(ray.Direction), hit.Normals);
        outRay = new Ray(hit.Position, reflected + Fuzziness * Sphere.RandomPointInSphere(new Random()), ray.Time);
        if (Vector3.Dot(outRay.Direction, hit.Normals) <= 0)
        {
            attenuation = Vector3.Zero.ToColor();
        }
        else
        {
            attenuation = Albedo;
        }
    }
}
