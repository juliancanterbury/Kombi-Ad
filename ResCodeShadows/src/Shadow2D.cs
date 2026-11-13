using System;
using System.Collections.Generic;
using Clipper2Lib;
using Rhino.Geometry;

namespace ResCodeShadows
{
  /// <summary>A plan region on a horizontal plane, stored as Clipper2 integer polygons.</summary>
  public sealed class Shadow2D
  {
    public readonly Paths64 Paths;
    public readonly double Scale;
    public readonly double Z;

    public Shadow2D(Paths64 paths, double scale, double z)
    {
      Paths = paths ?? new Paths64();
      Scale = scale;
      Z = z;
    }

    public bool IsEmpty => Paths.Count == 0;

    /// <summary>Net area in model units² (holes subtracted).</summary>
    public double Area => Math.Abs(Clipper.Area(Paths)) / (Scale * Scale);

    public Shadow2D Minus(Shadow2D other)
    {
      if (other == null || other.IsEmpty) return this;
      return new Shadow2D(Clipper.Difference(Paths, other.Paths, FillRule.NonZero), Scale, Z);
    }

    public BoundingBox Bounds
    {
      get
      {
        var bb = BoundingBox.Empty;
        foreach (var path in Paths)
          foreach (var p in path) bb.Union(new Point3d(p.X / Scale, p.Y / Scale, Z));
        return bb;
      }
    }

    /// <summary>Every loop (outer boundaries and holes) as closed curves.</summary>
    public List<Curve> Loops()
    {
      var list = new List<Curve>();
      foreach (var path in Paths)
      {
        var c = ToCurve(path);
        if (c != null) list.Add(c);
      }
      return list;
    }

    /// <summary>One entry per filled island: its outer boundary followed by its holes - ready for Hatch.Create.</summary>
    public List<Curve[]> HatchGroups()
    {
      var groups = new List<Curve[]>();
      if (IsEmpty) return groups;
      var tree = new PolyTree64();
      var clipper = new Clipper64();
      clipper.AddSubject(Paths);
      clipper.Execute(ClipType.Union, FillRule.NonZero, tree);
      Walk(tree, groups);
      return groups;
    }

    void Walk(PolyPath64 node, List<Curve[]> groups)
    {
      for (int i = 0; i < node.Count; i++)
      {
        var outer = node[i];
        if (outer.IsHole) continue;
        var curves = new List<Curve>();
        var oc = ToCurve(outer.Polygon);
        if (oc == null) continue;
        curves.Add(oc);
        for (int j = 0; j < outer.Count; j++)
        {
          var hole = outer[j];
          var hc = ToCurve(hole.Polygon);
          if (hc != null) curves.Add(hc);
          Walk(hole, groups);   // islands inside the hole
        }
        groups.Add(curves.ToArray());
      }
    }

    Curve ToCurve(Path64 path)
    {
      if (path == null || path.Count < 3) return null;
      var pl = new Polyline(path.Count + 1);
      foreach (var p in path) pl.Add(p.X / Scale, p.Y / Scale, Z);
      pl.Add(pl[0]);
      return new PolylineCurve(pl);
    }
  }
}
