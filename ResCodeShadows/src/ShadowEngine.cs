using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Clipper2Lib;

namespace ResCodeShadows
{
  public class Caster
  {
    public Mesh Mesh;
    public BoundingBox Box;
  }

  public static class ShadowEngine
  {
    // ------------------------------------------------------------------ meshing

    /// <summary>One mesh per Rhino object (blocks exploded, nested too). Existing render meshes are reused.</summary>
    public static List<Caster> BuildCasters(IEnumerable<RhinoObject> objects, Action<int> progress = null)
    {
      var list = new List<Caster>();
      var mp = MeshingParameters.FastRenderMesh;
      int n = 0;
      foreach (var obj in objects)
      {
        progress?.Invoke(++n);
        if (obj == null) continue;
        var mesh = new Mesh();
        if (obj is InstanceObject io)
        {
          io.Explode(true, out RhinoObject[] pieces, out ObjectAttributes[] _, out Transform[] xforms);
          for (int i = 0; i < pieces.Length; i++)
            AddGeometry(pieces[i].Geometry, xforms[i], mesh, mp);
        }
        else
        {
          Mesh[] render = null;
          if (obj.ObjectType == ObjectType.Brep || obj.ObjectType == ObjectType.Extrusion)
            render = obj.GetMeshes(MeshType.Render);
          if (render != null && render.Length > 0 && render.All(m => m != null))
            foreach (var m in render) mesh.Append(m);
          else
            AddGeometry(obj.Geometry, Transform.Identity, mesh, mp);
        }
        if (mesh.Faces.Count == 0) continue;
        mesh.Normals.ComputeNormals();
        var box = mesh.GetBoundingBox(false);
        list.Add(new Caster { Mesh = mesh, Box = box });
      }
      return list;
    }

    /// <summary>All casters joined into one mesh (for silhouettes).</summary>
    public static Mesh Combine(params IEnumerable<Caster>[] sets)
    {
      var m = new Mesh();
      foreach (var set in sets)
        foreach (var c in set) m.Append(c.Mesh);
      if (m.Faces.Count > 0) m.Compact();
      return m;
    }

    static void AddGeometry(GeometryBase geo, Transform xf, Mesh target, MeshingParameters mp)
    {
      var meshes = new List<Mesh>();
      switch (geo)
      {
        case Brep b:
          meshes.AddRange(Mesh.CreateFromBrep(b, mp) ?? new Mesh[0]);
          break;
        case Extrusion e:
          var eb = e.ToBrep();
          if (eb != null) meshes.AddRange(Mesh.CreateFromBrep(eb, mp) ?? new Mesh[0]);
          break;
        case Mesh m:
          meshes.Add(m.DuplicateMesh());
          break;
        case SubD s:
          var sm = Mesh.CreateFromSubD(s, 3);
          if (sm != null) meshes.Add(sm);
          break;
      }
      foreach (var m in meshes)
      {
        if (m == null) continue;
        if (!xf.IsIdentity) m.Transform(xf);
        target.Append(m);
      }
    }

    // ------------------------------------------------------------------ shadow polygons

    /// <summary>Integer scale for polygon clipping: half the model tolerance per unit.</summary>
    public static double ClipScale(double tol) => 2.0 / Math.Max(tol, 1e-9);

    /// <summary>
    /// Shadow cast on the plane z = groundZ: every mesh triangle is projected along the sun
    /// direction and all triangles are unioned with Clipper2. This is exact (no silhouette
    /// tracing) and always produces simple, non-self-intersecting outer/hole loops.
    /// </summary>
    public static Shadow2D CastShadow(IEnumerable<Caster> casters, Vector3d toSun, double groundZ, double tol)
    {
      double scale = ClipScale(tol);
      var tris = new Paths64();
      if (toSun.Z > 1e-6)
      {
        double kx = toSun.X / toSun.Z, ky = toSun.Y / toSun.Z;
        foreach (var c in casters)
        {
          var m = c.Mesh;
          if (m == null || m.Faces.Count == 0) continue;
          bool closed = m.IsClosed;
          if (closed && m.FaceNormals.Count != m.Faces.Count) m.FaceNormals.ComputeFaceNormals();

          var pts = new Point64[m.Vertices.Count];
          for (int i = 0; i < pts.Length; i++)
          {
            var p = m.Vertices[i];
            double h = p.Z - groundZ;
            pts[i] = new Point64((p.X - kx * h) * scale, (p.Y - ky * h) * scale);
          }
          for (int f = 0; f < m.Faces.Count; f++)
          {
            // closed solids: the sun-facing half already covers the whole shadow
            if (closed && (Vector3d)m.FaceNormals[f] * toSun <= 0) continue;
            var face = m.Faces[f];
            AddTri(tris, pts[face.A], pts[face.B], pts[face.C]);
            if (face.IsQuad) AddTri(tris, pts[face.A], pts[face.C], pts[face.D]);
          }
        }
      }
      var union = Clipper.Union(tris, FillRule.NonZero);
      union = Clipper.SimplifyPaths(union, 1.0, true);            // drop sub-tolerance zig-zags
      union = Clipper.Union(union, FillRule.NonZero);             // re-clean after simplification
      return new Shadow2D(union, scale, groundZ);
    }

    static void AddTri(Paths64 paths, Point64 a, Point64 b, Point64 c)
    {
      double cross = (double)(b.X - a.X) * (c.Y - a.Y) - (double)(b.Y - a.Y) * (c.X - a.X);
      if (Math.Abs(cross) < 2.0) return;                           // degenerate (edge-on) triangle
      var p = new Path64(3) { a };
      if (cross > 0) { p.Add(b); p.Add(c); } else { p.Add(c); p.Add(b); }  // all counter-clockwise
      paths.Add(p);
    }

    // ------------------------------------------------------------------ sunlight test

    /// <summary>True if a ray from <paramref name="p"/> towards the sun is blocked by any mesh.</summary>
    public static bool IsShaded(Point3d p, Vector3d toSun, List<Caster> blockers, double lift)
    {
      var o = p + Vector3d.ZAxis * lift;
      var ray = new Ray3d(o, toSun);
      foreach (var c in blockers)
      {
        if (!RayHitsBox(o, toSun, c.Box)) continue;      // cheap rejection before the mesh test
        if (Intersection.MeshRay(c.Mesh, ray) >= 0) return true;
      }
      return false;
    }

    static bool RayHitsBox(Point3d o, Vector3d d, BoundingBox b)
    {
      double tmin = 0, tmax = double.MaxValue;
      double[] oo = { o.X, o.Y, o.Z }, dd = { d.X, d.Y, d.Z };
      double[] mn = { b.Min.X, b.Min.Y, b.Min.Z }, mx = { b.Max.X, b.Max.Y, b.Max.Z };
      for (int i = 0; i < 3; i++)
      {
        double pad = 1e-6 * (1 + Math.Abs(mx[i] - mn[i]));
        if (Math.Abs(dd[i]) < 1e-12)
        {
          if (oo[i] < mn[i] - pad || oo[i] > mx[i] + pad) return false;
          continue;
        }
        double t1 = (mn[i] - pad - oo[i]) / dd[i], t2 = (mx[i] + pad - oo[i]) / dd[i];
        if (t1 > t2) { var t = t1; t1 = t2; t2 = t; }
        tmin = Math.Max(tmin, t1);
        tmax = Math.Min(tmax, t2);
        if (tmin > tmax) return false;
      }
      return true;
    }
  }
}
