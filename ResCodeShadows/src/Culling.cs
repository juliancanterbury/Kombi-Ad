using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ResCodeShadows
{
  /// <summary>
  /// Decides, from bounding boxes alone (no meshing), which objects can contribute to the shadow
  /// that matters. Rules are conservative: an object is only dropped when it provably cannot
  /// change the result (or is below the size threshold the user chose).
  /// </summary>
  internal static class Culling
  {
    public class Report
    {
      public int Input, Ground, Tiny, Enclosed, OutOfReach, Kept;
      public override string ToString() =>
        $"{Kept} of {Input} objects cast relevant shadow (ignored: {OutOfReach} out of reach, {Enclosed} enclosed in solids, " +
        $"{Tiny} smaller than the size limit, {Ground} lying on the ground).";
    }

    class Item
    {
      public RhinoObject Obj;
      public BoundingBox Box;
      public BoundingBox Reach;   // plan extents of everything this object can shade, over all sun positions
      public bool Keep = true;
    }

    /// <summary>Sun vectors at every analysis step over the time range (endpoints included), above horizon only.</summary>
    public static List<Vector3d> SunVectors(ShadowSettings s)
    {
      var list = new List<Vector3d>();
      double step = Math.Max(1, s.AnalysisStep) / 60.0;
      for (double t = s.StartHour; t <= s.EndHour + 1e-6; t += step)
      {
        var p = s.SunAt(t);
        if (p.IsUp) list.Add(s.SunVector(p));
      }
      var pe = s.SunAt(s.EndHour);
      if (pe.IsUp) list.Add(s.SunVector(pe));
      return list;
    }

    /// <summary>Plan box covering the object's footprint and all its shadows on plane z = groundZ.</summary>
    public static BoundingBox ShadowReach(BoundingBox bb, IList<Vector3d> suns, double groundZ)
    {
      var r = new BoundingBox(new Point3d(bb.Min.X, bb.Min.Y, groundZ), new Point3d(bb.Max.X, bb.Max.Y, groundZ));
      var corners = bb.GetCorners();
      foreach (var v in suns)
      {
        if (v.Z <= 1e-6) continue;
        foreach (var c in corners)
        {
          double h = c.Z - groundZ;
          if (h <= 0) continue;
          r.Union(new Point3d(c.X - v.X / v.Z * h, c.Y - v.Y / v.Z * h, groundZ));
        }
      }
      return r;
    }

    static bool Overlap2D(BoundingBox a, BoundingBox b) =>
      a.Min.X <= b.Max.X && a.Max.X >= b.Min.X && a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;

    /// <summary>
    /// Filters the three object sets in place.
    /// </summary>
    /// <param name="studyArea">Plan region that matters (e.g. the SPOS). Null = wherever the proposal casts shadow.</param>
    public static Report Run(RhinoDoc doc, ShadowSettings s,
                             ref List<RhinoObject> proposed, ref List<RhinoObject> removed, ref List<RhinoObject> context,
                             BoundingBox? studyArea)
    {
      double m2model = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
      double tol = doc.ModelAbsoluteTolerance;
      double groundTol = 0.05 * m2model;
      double minSize = Math.Max(0, s.MinCasterSize) * m2model;
      double margin = 1.0 * m2model;

      var P = proposed.Select(o => new Item { Obj = o, Box = Box(o) }).ToList();
      var R = removed.Select(o => new Item { Obj = o, Box = Box(o) }).ToList();
      var C = context.Select(o => new Item { Obj = o, Box = Box(o) }).ToList();
      var rep = new Report { Input = P.Count + R.Count + C.Count };

      // reference ground = underside of the proposal (or of the study area if lower)
      var pBox = BoundingBox.Empty;
      foreach (var i in P) if (i.Box.IsValid) pBox.Union(i.Box);
      double groundZ = pBox.IsValid ? pBox.Min.Z : 0;
      if (studyArea.HasValue && studyArea.Value.IsValid) groundZ = Math.Min(groundZ, studyArea.Value.Min.Z);

      var suns = SunVectors(s);

      // 1. ground-hugging and tiny objects
      foreach (var i in P.Concat(R).Concat(C))
      {
        if (!i.Box.IsValid) continue;   // unknown extents: keep
        if (i.Box.Max.Z <= groundZ + groundTol) { i.Keep = false; rep.Ground++; continue; }
        var d = i.Box.Diagonal;
        if (Math.Max(d.X, Math.Max(d.Y, d.Z)) < minSize) { i.Keep = false; rep.Tiny++; continue; }
        i.Reach = ShadowReach(i.Box, suns, groundZ);
      }

      // 2. reach: can this object's shadow land anywhere that matters?
      List<BoundingBox> targets;
      if (studyArea.HasValue && studyArea.Value.IsValid)
      {
        var sa = studyArea.Value;
        sa.Inflate(margin, margin, 0);
        targets = new List<BoundingBox> { sa };
      }
      else
      {
        targets = P.Where(i => i.Keep && i.Box.IsValid).Select(i => { var b = i.Reach; b.Inflate(margin, margin, 0); return b; }).ToList();
      }
      var reachSets = studyArea.HasValue ? P.Concat(R).Concat(C) : R.Concat(C);
      foreach (var i in reachSets)
      {
        if (!i.Keep || !i.Box.IsValid) continue;
        if (!targets.Any(t => Overlap2D(t, i.Reach))) { i.Keep = false; rep.OutOfReach++; }
      }

      // 3. objects sealed inside a closed solid of the same set (furniture, internal walls, services...)
      if (s.CullEnclosed)
        foreach (var set in new[] { P, R, C })
          rep.Enclosed += CullEnclosed(set, tol);

      proposed = P.Where(i => i.Keep).Select(i => i.Obj).ToList();
      removed = R.Where(i => i.Keep).Select(i => i.Obj).ToList();
      context = C.Where(i => i.Keep).Select(i => i.Obj).ToList();
      rep.Kept = proposed.Count + removed.Count + context.Count;
      return rep;
    }

    static BoundingBox Box(RhinoObject o)
    {
      try { return o.Geometry?.GetBoundingBox(false) ?? BoundingBox.Unset; }
      catch { return BoundingBox.Unset; }
    }

    static int CullEnclosed(List<Item> set, double tol)
    {
      var kept = set.Where(i => i.Keep && i.Box.IsValid).ToList();
      if (kept.Count < 2) return 0;

      // closed containers, biggest first
      var containers = new List<(Item item, Func<Point3d, bool> inside)>();
      foreach (var i in kept.OrderByDescending(i => i.Box.Volume))
      {
        var f = InsideTest(i.Obj.Geometry, tol);
        if (f != null) containers.Add((i, f));
      }
      if (containers.Count == 0) return 0;

      int n = 0;
      foreach (var cand in kept)
      {
        foreach (var (item, inside) in containers)
        {
          if (ReferenceEquals(item, cand) || !item.Keep) continue;
          if (!item.Box.Contains(cand.Box, false)) continue;
          if (SamplePoints(cand.Box).All(inside)) { cand.Keep = false; n++; break; }
        }
      }
      return n;
    }

    static Func<Point3d, bool> InsideTest(GeometryBase g, double tol)
    {
      switch (g)
      {
        case Brep b when b.IsSolid:
          return p => b.IsPointInside(p, tol, true);
        case Extrusion e when e.IsSolid:
          var eb = e.ToBrep();
          return eb == null ? null : (Func<Point3d, bool>)(p => eb.IsPointInside(p, tol, true));
        case Mesh m when m.IsClosed:
          return p => m.IsPointInside(p, tol, true);
        default:
          return null;
      }
    }

    /// <summary>27 points of a 3x3x3 lattice on the box (corners, edge mid-points, face centres, centre).</summary>
    static IEnumerable<Point3d> SamplePoints(BoundingBox b)
    {
      for (int i = 0; i <= 2; i++)
        for (int j = 0; j <= 2; j++)
          for (int k = 0; k <= 2; k++)
            yield return new Point3d(b.Min.X + (b.Max.X - b.Min.X) * i / 2.0,
                                     b.Min.Y + (b.Max.Y - b.Min.Y) * j / 2.0,
                                     b.Min.Z + (b.Max.Z - b.Min.Z) * k / 2.0);
    }
  }
}
