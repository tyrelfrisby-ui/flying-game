namespace FlyingGame.Core;

/// <summary>
/// Spatial rejection for the continuous authoring evaluator. Cells contain ordered feature references,
/// never sampled elevations. Every arithmetic operation still evaluates at the caller's exact point.
/// Construction may scan the world; a query only visits its cell's local candidates.
/// </summary>
internal static class IslandHeightIndex
{
    private const double CellSize = .5, Margin = 1e-9;
    // Authoring-time limits make the work bound explicit. A denser world must be reviewed/reindexed
    // at initialization, rather than silently increasing the cost of every physics sample.
    private const int CandidateLimit = 128, SegmentLimit = 64;
    private static int maxNearestSegments, maxRayEdges;
    private static IslandValue W => IslandTerrain.World;
    private static double Smooth(double t) => IslandTerrain.Smooth(t);
    private static double Mix(double h, double z, double a) => IslandTerrain.Mix(h, z, a);
    private const double Feet = IslandTerrain.Feet;
    private delegate double Layer(IslandPoint p, double h);
    private readonly struct Rect
    {
        public readonly double MinE, MaxE, MinN, MaxN;
        public Rect(double minE, double maxE, double minN, double maxN, double padding=Margin)
        { MinE = minE - padding; MaxE = maxE + padding; MinN = minN - padding; MaxN = maxN + padding; }
        public static Rect Around(IslandPoint p, double r) => new(p.E-r, p.E+r, p.N-r, p.N+r);
        public static Rect Points(IslandPoint[] points, double r, double padding=Margin) => new(points.Min(p=>p.E)-r, points.Max(p=>p.E)+r, points.Min(p=>p.N)-r, points.Max(p=>p.N)+r,padding);
        public bool Contains(IslandPoint p) => p.E>=MinE && p.E<=MaxE && p.N>=MinN && p.N<=MaxN;
    }
    private sealed class Grid<T>
    {
        private readonly T[][] cells;
        private readonly int minE, minN, sizeE, sizeN;
        public readonly T[] All;
        public int MaxCandidates { get; }
        public Grid(List<(Rect bounds,T value)> entries)
        {
            All=entries.Select(e=>e.value).ToArray();
            if(entries.Count==0) { cells=Array.Empty<T[]>(); return; }
            minE=Cell(entries.Min(e=>e.bounds.MinE)); minN=Cell(entries.Min(e=>e.bounds.MinN));
            sizeE=Cell(entries.Max(e=>e.bounds.MaxE))-minE+1; sizeN=Cell(entries.Max(e=>e.bounds.MaxN))-minN+1;
            var lists=new List<T>?[sizeE*sizeN];
            foreach(var entry in entries)
                for(int n=Cell(entry.bounds.MinN);n<=Cell(entry.bounds.MaxN);n++)
                    for(int e=Cell(entry.bounds.MinE);e<=Cell(entry.bounds.MaxE);e++)
                        (lists[(n-minN)*sizeE+e-minE]??=new List<T>()).Add(entry.value);
            cells=lists.Select(l=>l?.ToArray()??Array.Empty<T>()).ToArray();
            MaxCandidates=cells.Max(c=>c.Length);
            if(MaxCandidates>CandidateLimit) throw new InvalidOperationException($"Island {typeof(T).Name} cell exceeds {CandidateLimit} candidates; refine the spatial index before growing this area.");
        }
        public T[] At(IslandPoint p)
        {
            // Compare in double before casting: huge finite coordinates must remain ocean.
            double e=Math.Floor(p.E/CellSize)-minE, n=Math.Floor(p.N/CellSize)-minN;
            return e>=0 && e<sizeE && n>=0 && n<sizeN ? cells[(int)n*sizeE+(int)e] : Array.Empty<T>();
        }
    }
    private static int Cell(double x) => (int)Math.Floor(x/CellSize);
    private readonly struct Edge
    {
        public readonly IslandPoint A,B;
        public readonly double Length,Run;
        public Edge(IslandPoint a,IslandPoint b,double run) { A=a; B=b; Length=(b-a).Length; Run=run; }
    }
    private sealed class GeometryCell
    {
        public readonly Edge[] Nearest, Rays;
        public GeometryCell(Edge[] nearest,Edge[] rays) { Nearest=nearest; Rays=rays; }
    }
    private sealed class Geometry
    {
        private const double GeometryCellSize=.125;
        private static int GeometryCoordinate(double x) => (int)Math.Floor(x/GeometryCellSize);
        private readonly GeometryCell[] cells;
        private readonly int minE,minN,sizeE,sizeN;
        private readonly Edge[] Edges;
        public Geometry(IslandPoint[] points,double radius,bool closed)
        {
            Edges=new Edge[closed?points.Length:points.Length-1]; double run=0;
            for(int i=0;i<Edges.Length;i++) { Edges[i]=new Edge(points[i],points[(i+1)%points.Length],run); run+=Edges[i].Length; }
            var bounds=Rect.Points(points,radius);
            minE=GeometryCoordinate(bounds.MinE); minN=GeometryCoordinate(bounds.MinN);
            sizeE=GeometryCoordinate(bounds.MaxE)-minE+1; sizeN=GeometryCoordinate(bounds.MaxN)-minN+1;
            cells=new GeometryCell[sizeE*sizeN];
            for(int n=0;n<sizeN;n++) for(int e=0;e<sizeE;e++)
            {
                double loE=(minE+e)*GeometryCellSize,loN=(minN+n)*GeometryCellSize;
                var centre=new IslandPoint(loE+GeometryCellSize/2,loN+GeometryCellSize/2);
                var distances=Edges.Select(edge=>IslandTerrain.Segment(centre,edge.A,edge.B).d).ToArray();
                // Distance to a segment is 1-Lipschitz. At any point in this cell, a segment
                // farther than best(center)+2*halfDiagonal cannot be the closest segment.
                double limit=distances.Min()+Math.Sqrt(2)*GeometryCellSize+Margin;
                var nearest=Edges.Where((edge,i)=>distances[i]<=limit).ToArray();
                // Only edges crossing this north interval and reaching east of the cell can
                // toggle the exact horizontal-ray parity test. Preserve original edge order.
                var rays=closed?Edges.Where(edge=>Math.Min(edge.A.N,edge.B.N)<=loN+GeometryCellSize+Margin && Math.Max(edge.A.N,edge.B.N)>=loN-Margin && Math.Max(edge.A.E,edge.B.E)>=loE-Margin).ToArray():Array.Empty<Edge>();
                if(nearest.Length>SegmentLimit||rays.Length>SegmentLimit) throw new InvalidOperationException($"Island geometry cell exceeds {SegmentLimit} segments; refine its spatial index.");
                maxNearestSegments=Math.Max(maxNearestSegments,nearest.Length); maxRayEdges=Math.Max(maxRayEdges,rays.Length);
                cells[n*sizeE+e]=new GeometryCell(nearest,rays);
            }
        }
        private GeometryCell? At(IslandPoint p)
        {
            double e=Math.Floor(p.E/GeometryCellSize)-minE,n=Math.Floor(p.N/GeometryCellSize)-minN;
            return e>=0&&e<sizeE&&n>=0&&n<sizeN?cells[(int)n*sizeE+(int)e]:null;
        }
        public (double d,double t) Distance(IslandPoint p,double total)
        {
            var cell=At(p);
            // Outside the feature's influence domain every blend is exactly zero. This is
            // also used by the diagnostic unbucketed path; never fall back to a world scan.
            if(cell==null) return (double.PositiveInfinity,0);
            double best=double.PositiveInfinity,bt=0;
            foreach(var edge in cell.Nearest)
            {
                var (d,t)=IslandTerrain.Segment(p,edge.A,edge.B);
                if(d<best) { best=d; bt=(edge.Run+t*edge.Length)/Math.Max(.00001,total); }
            }
            return (best,bt);
        }
        public double Shore(IslandPoint p)
        {
            var cell=At(p);
            // The indexed evaluator only asks inside this geometry's precomputed domain.
            if(cell==null) throw new InvalidOperationException("Island shore query outside indexed geometry domain.");
            double distance=double.PositiveInfinity;
            foreach(var edge in cell.Nearest) distance=Math.Min(distance,IslandTerrain.Segment(p,edge.A,edge.B).d);
            bool inside=false;
            foreach(var edge in cell.Rays)
            {
                var a=edge.A; var b=edge.B;
                if((a.N>p.N)!=(b.N>p.N)&&p.E<(b.E-a.E)*(p.N-a.N)/(b.N-a.N)+a.E) inside=!inside;
            }
            return inside?distance:-distance;
        }
    }
    private sealed class Line
    {
        public readonly IslandPoint[] Points;
        public readonly double[] Lengths,Runs;
        public readonly double Total;
        private Geometry? geometry;
        public Line(IslandValue value)
        {
            Points=value.Points; Lengths=new double[Points.Length-1]; Runs=new double[Lengths.Length];
            double run=0;
            for(int i=0;i<Lengths.Length;i++) { Runs[i]=run; Lengths[i]=(Points[i+1]-Points[i]).Length; run+=Lengths[i]; }
            Total=run;
        }
        public (double d,double t) At(IslandPoint p) => geometry!.Distance(p,Total);
        public Rect Bounds(double r) { geometry=new Geometry(Points,r+CellSize,false); return Rect.Points(Points,r); }
    }
    private sealed class Budget
    {
        public readonly bool Airway;
        public readonly IslandPoint A,B,U;
        public readonly double Width, Blend, Cap, Length, Elevation, Ratio;
        public readonly Rect Bounds;
        public Budget(IslandValue q)
        {
            Airway=q.S("kind")=="airway"; A=q["a"].Point; B=q["b"].Point; U=q["u"].Point;
            Width=q.N("width"); Blend=q.N("blend"); Cap=q.N("cap_m"); Length=q.N("length"); Elevation=q.N("elevation_m"); Ratio=q.N("ratio");
            if(Airway) Bounds=Rect.Points(new[]{A,B},Math.Max(Width,3.704)+Blend);
            else
            {
                // ad in [-.25-blend,length+.25+blend]; cross <= .15+.05*max(0,ad)+.25+blend.
                double lo=-.25-Blend, hi=Length+.25+Blend, cross=.4+.05*Math.Max(0,hi)+Blend;
                var v=new IslandPoint(U.N,-U.E);
                Bounds=Rect.Points(new[]{A-U*lo+v*cross,A-U*lo-v*cross,A-U*hi+v*cross,A-U*hi-v*cross},0);
            }
        }
        public double Apply(IslandPoint p,double h,double factor,bool broad)
        {
            double alpha,cap;
            if(Airway) { alpha=1-Smooth((IslandTerrain.Segment(p,A,B).d-Width)/Blend); cap=Cap; }
            else
            {
                var dp=p-A; double ad=-dp.Dot(U), cross=Math.Abs(dp.E*U.N-dp.N*U.E), width=.15+.05*Math.Max(0,ad);
                double dd=Math.Max(0,Math.Max(Math.Max(-ad-.25,ad-Length-.25),cross-width-.25));
                alpha=1-Smooth(dd/Blend); cap=Elevation+(broad?Math.Max(0,ad):ad)*1000/Ratio-(broad?30:0);
            }
            return Mix(h,Math.Min(h,cap),alpha*factor);
        }
    }
    private sealed class OuterIsland
    {
        public readonly IslandPoint[] Polygon;
        public readonly Geometry Coast;
        public readonly Rect Bounds;
        public readonly IslandPoint Centre, VolcanoCentre;
        public readonly bool Relief, Volcano;
        public readonly double Base, ReliefTop, ReliefRadius, VolcanoRadius, RimRadius, Rim, Floor, VolcanoBase;
        public OuterIsland(IslandValue q,IslandPoint[] polygon)
        {
            Polygon=polygon; Coast=new Geometry(polygon,.35,true); Bounds=Rect.Points(polygon,.35,0); Centre=q["centre"].Point;
            Base=q.N("base_ft",q.N("elevation_ft",35))*Feet;
            Relief=q.Has("relief"); ReliefTop=q["relief"].N("top_ft")*Feet; ReliefRadius=q["relief"].N("radius_km");
            Volcano=q.Has("volcano"); var v=q["volcano"]; VolcanoCentre=v["centre"].Point; VolcanoRadius=v.N("radius_km");
            RimRadius=v.N("rim_radius_km"); Rim=v.N("rim_ft")*Feet; Floor=v.N("crater_floor_ft")*Feet; VolcanoBase=q.N("base_ft")*Feet;
        }
        public double Height(IslandPoint p,double sd)
        {
            double zh=Base;
            if(Relief) zh+=(ReliefTop-zh)*(1-Smooth((p-Centre).Length/ReliefRadius));
            if(Volcano)
            {
                double r=(p-VolcanoCentre).Length;
                if(r<=RimRadius) zh=Mix(Floor,Rim,Smooth(r/RimRadius));
                else if(r<VolcanoRadius) zh=VolcanoBase+(Rim-VolcanoBase)*(VolcanoRadius-r)/(VolcanoRadius-RimRadius);
            }
            return -30+(zh+30)*Smooth(sd/ShoreFade);
        }
    }
    private static readonly IslandPoint[][] Land=W["land_polygons"].Items.Select(v=>v.Points).ToArray();
    private static readonly Rect MainlandBounds=Rect.Points(Land[0],.35,0), CatalinaBounds=Rect.Points(Land[1],.35,0);
    private static readonly Geometry MainlandCoast=new(Land[0],.35,true), CatalinaCoast=new(Land[1],.35,true);
    private static readonly double ShoreFade=W["edge"].N("land_shore_fade_km"), WaterOffset=W["height_field"].N("water_lane_bed_offset_m");
    private static readonly Grid<OuterIsland> Outer=BuildOuter();
    private static readonly Grid<Layer> BeforeOuter=BuildBeforeOuter();
    private static readonly Grid<Layer> AfterOuter=BuildAfterOuter(false), AfterBudget=BuildAfterOuter(true);
    private static readonly Grid<Budget> Budgets=BuildBudgets();
    private static readonly Grid<IslandField> Fields=BuildFields();
    private static readonly Grid<IslandPoint> PeakCores=new(W["height_field"]["peaks"].Items.Select(q=>(Rect.Around(q["centre"].Point,.4),q["centre"].Point)).ToList());
    internal static string WorkBounds => $"0.5 km feature cells: before outer {BeforeOuter.MaxCandidates}, outer {Outer.MaxCandidates}, after outer {AfterOuter.MaxCandidates}, budgets {Budgets.MaxCandidates}, after budget {AfterBudget.MaxCandidates}, fields {Fields.MaxCandidates}, peak cores {PeakCores.MaxCandidates}; 0.125 km geometry cells: nearest segments {maxNearestSegments}, ray edges {maxRayEdges}; enforced limits {CandidateLimit} features / {SegmentLimit} segments per list";
    private static readonly IslandValue Drake=IslandTerrain.Feature("F1");
    private static readonly double DrakeMouth=Drake["shell_geometry"]["mouth"].Point.N, DrakeExterior=Drake.N("exterior_strip_m")/1000;
    private static Grid<OuterIsland> BuildOuter()
    {
        var all=W["outer_islands"].Items.Concat(W["islets"].Items).Select((q,i)=>new OuterIsland(q,Land[i+2])).ToArray();
        return new Grid<OuterIsland>(all.Select(q=>(q.Bounds,q)).ToList());
    }
    // Smooth saturates at 1, so these expanded footprints contain every nonzero blend.
    // Grid construction appends in authoring order: overlapping cuts/raises never reorder.
    private static void Add(List<(Rect,Layer)> layers,Rect bounds,Layer op) => layers.Add((bounds,op));
    private static void Radial(List<(Rect,Layer)> layers,IslandPoint centre,double core,double blend,double height,bool maximum=false)
        => Add(layers,Rect.Around(centre,core+blend),(p,h)=> {
            double z=Mix(h,height,1-Smooth(((p-centre).Length-core)/blend)); return maximum?Math.Max(h,z):z;
        });
    private static Grid<Layer> BuildBeforeOuter()
    {
        var layers=new List<(Rect,Layer)>(); var hf=W["height_field"];
        foreach(var q in hf["regional_overrides"].Items)
        {
            var c=q["centre"].Point; double rx=q["radii_km"][0].Number(),ry=q["radii_km"][1].Number(),z=q.N("elevation_ft")*Feet;
            Add(layers,new Rect(c.E-rx,c.E+rx,c.N-ry,c.N+ry),(p,h)=> {
                var d=p-c; return Mix(h,z,1-Smooth(Math.Sqrt(d.E*d.E/(rx*rx)+d.N*d.N/(ry*ry))));
            });
        }
        foreach(var q in hf["crests"].Items)
        {
            var line=new Line(q["path"]); double top=q.N("top_ft")*Feet,width=q.N("width_km");
            Add(layers,line.Bounds(width),(p,h)=>Math.Max(h,Mix(h,top,1-Smooth(line.At(p).d/width))));
        }
        foreach(var q in hf["peaks"].Items) Radial(layers,q["centre"].Point,0,q.N("radius_km"),q.N("top_ft")*Feet,true);
        foreach(var q in W["water"].Items)
        {
            var c=q["centre"].Point; var u=IslandPoint.Heading(q.N("long_axis_true_deg",90));
            double rx=q["radii_km"][0].Number(),ry=q["radii_km"][1].Number(),z=q.N("elevation_ft")*Feet-q.N("depth_m");
            Add(layers,Rect.Around(c,1.3*Math.Max(rx,ry)),(p,h)=> {
                var d=p-c; double a=d.Dot(u)/rx,b=(d.E*u.N-d.N*u.E)/ry;
                return Mix(h,Math.Min(h,z),1-Smooth((Math.Sqrt(a*a+b*b)-.85)/.45));
            });
        }
        foreach(var q in W["glaciers"].Items)
        {
            var line=new Line(q["path"]); double width=q.N("half_width_m")/1000,head=q.N("head_ft"),ski=q["ski_zone"].N("elevation_ft");
            Add(layers,line.Bounds(width+.3),(p,h)=> {
                var (d,t)=line.At(p); double a=(1-Smooth((d-width)/.3))*Smooth(t/.12);
                return Mix(h,(head*(1-t)+ski*t)*Feet,a);
            });
            Radial(layers,q["ski_zone"]["centre"].Point,.15,.25,ski*Feet);
        }
        foreach(var q in W["canyons"].Items)
        {
            var line=new Line(q["path"]); var rw=q["rim_width_m"].Items; var fw=q["floor_width_m"].Items; var zf=q["floor_ft"].Items;
            double r0=rw[0].Number(),r1=rw[rw.Length-1].Number(),f0=fw[0].Number(),f1=fw[fw.Length-1].Number(),z0=zf[0].Number(),z1=zf[zf.Length-1].Number();
            double radius=(Math.Max(f0,f1)/2+Math.Max(1,Math.Max(r0-f0,r1-f1)/2))/1000;
            for(int i=0;i<line.Lengths.Length;i++)
            {
                var a=line.Points[i]; var b=line.Points[i+1]; double run=line.Runs[i],length=line.Lengths[i],total=line.Total;
                Add(layers,Rect.Points(new[]{a,b},radius),(p,h)=> {
                    var (dd,t0)=IslandTerrain.Segment(p,a,b); double t=(run+t0*length)/total;
                    double rim=Mix(r0,r1,t),floor=Mix(f0,f1,t);
                    return Mix(h,Math.Min(h,Mix(z0,z1,t)*Feet),1-Smooth((dd*1000-floor/2)/Math.Max(1,(rim-floor)/2)));
                });
            }
        }
        foreach(var q in W["lesson_terrain"].Items)
        {
            var safe=new Line(q["safe_path"]); var box=new Line(q["box_path"]); double saddle=q.N("saddle_ft")*Feet,rise=q.N("box_floor_rise_m");
            Add(layers,safe.Bounds(.37),(p,h)=>Mix(h,Math.Min(h,saddle),1-Smooth((safe.At(p).d-.12)/.25)));
            Add(layers,box.Bounds(.2),(p,h)=> { var (d,t)=box.At(p); return Mix(h,saddle+rise*t,1-Smooth((d-.08)/.12)); });
        }
        var centre=W["aerobatic_boxes"][2]["centre"].Point;
        Add(layers,Rect.Around(centre,.7),(p,h)=> { var d=p-centre; return Mix(h,5000*Feet,1-Smooth((Math.Max(Math.Abs(d.E),Math.Abs(d.N))-.5)/.2)); });
        return new Grid<Layer>(layers);
    }
    private static Grid<Layer> BuildAfterOuter(bool afterBudget)
    {
        var layers=new List<(Rect,Layer)>(); var hf=W["height_field"];
        if(!afterBudget)
        {
            var terrace=hf["land_moa_terrace"]; var boundary=terrace["boundary"].Points; double z=terrace.N("elevation_ft")*Feet,blend=terrace.N("blend_m")/1000;
            var coast=new Geometry(boundary,blend,true);
            var bounds=Rect.Points(boundary,blend);
            Add(layers,bounds,(p,h)=>bounds.Contains(p)?Mix(h,z,1-Smooth(Math.Max(0,-coast.Shore(p))/blend)):h);
            foreach(var zone in W["combat_zones"].Items) foreach(var f in zone["combat_features"].Items)
            {
                if(f.S("type")!="paired ridges and dry canyon") continue;
                var c=f["centre"].Point; double length=f.N("length_m")/2000,floorWidth=f.N("canyon_floor_width_m")/2000,floor=f.N("canyon_floor_ft")*Feet;
                double canyon=f.N("canyon_width_m")/2000,half=f.N("ridge_half_width_m")/1000,rim=f.N("rim_ft")*Feet;
                Add(layers,new Rect(c.E-Math.Max(floorWidth+.02,canyon+2*half),c.E+Math.Max(floorWidth+.02,canyon+2*half),c.N-Math.Max(.15,length+.025),c.N+Math.Max(.15,length+.025)),(p,h)=> {
                    double a=1-Smooth(Math.Max(0,Math.Abs(p.N-c.N)-length)/.025);
                    double cut=(1-Smooth((Math.Abs(p.E-c.E)-floorWidth)/.02))*(1-Smooth((Math.Abs(p.N-c.N)-.12)/.03));
                    h=Mix(h,Math.Min(h,floor),cut);
                    double rr=Math.Abs(p.E-c.E+(canyon+half))/half;
                    h=Mix(h,rim,(1-Smooth(rr))*a);
                    rr=Math.Abs(p.E-c.E-(canyon+half))/half;
                    return Mix(h,rim,(1-Smooth(rr))*a);
                });
            }
            foreach(var q in hf["hazard_benches"].Items) Radial(layers,q["centre"].Point,q.N("core_radius_m")/1000,q.N("blend_m")/1000,q.N("elevation_ft")*Feet);
            foreach(var zone in W["combat_zones"].Items) foreach(var f in zone["combat_features"].Items)
                if(f.S("type")=="rock spire pair") foreach(var c in f["stack_centres"].Items) Radial(layers,c.Point,0,f.N("radius_m")/1000,f.N("top_msl_ft")*Feet,true);
        }
        else
        {
            foreach(var q in W["lesson_terrain"].Items)
            {
                var line=new Line(q["box_path"]); double saddle=q.N("saddle_ft")*Feet,rise=q.N("box_floor_rise_m"),width=q.N("floor_half_width_m")/1000,blend=q.N("outer_blend_m")/1000;
                Add(layers,line.Bounds(width+blend),(p,h)=> { var (d,t)=line.At(p); return Mix(h,saddle+rise*t,1-Smooth((d-width)/blend)); });
                var wall=q["headwall_geometry"]; Radial(layers,wall["centre"].Point,wall.N("core_radius_m")/1000,wall.N("blend_m")/1000,wall.N("top_ft")*Feet,true);
            }
            foreach(var q in hf["peaks"].Items) Radial(layers,q["centre"].Point,.12,.28,q.N("top_ft")*Feet);
            var box=W["aerobatic_boxes"][2]; var c=box["centre"].Point; double z=box.N("centre_ground_ft")*Feet;
            Add(layers,Rect.Around(c,.65),(p,h)=> { var d=p-c; return Mix(h,z,1-Smooth((Math.Max(Math.Abs(d.E),Math.Abs(d.N))-.5)/.15)); });
            var shell=IslandTerrain.Feature("F1")["shell_geometry"]; var chamber=shell["chamber_centre"].Point;
            double roof=shell.N("minimum_roof_msl_m"),blendRoof=shell.N("roof_blend_m")/1000;
            Add(layers,new Rect(chamber.E-.210-blendRoof,chamber.E+.210+blendRoof,chamber.N-.410-blendRoof,chamber.N+.410+blendRoof),(p,h)=> {
                var dp=p-chamber; double dd=Math.Sqrt(Math.Pow(Math.Max(0,Math.Abs(dp.N)-.410),2)+Math.Pow(Math.Max(0,Math.Abs(dp.E)-.210),2));
                return Math.Max(h,Mix(h,roof,1-Smooth(dd/blendRoof)));
            });
            Radial(layers,shell["crown_centre"].Point,.25,.4,shell.N("crown_msl_ft")*Feet,true);
            var cave=IslandTerrain.Feature("F2"); var ci=cave["interior"];
            Radial(layers,ci["chamber_centre"].Point,cave.N("chamber_radius_m")/1000,.08,ci.N("roof_top_msl_m"),true);
            foreach(var q in W["water"].Items)
            {
                if(!q.Has("basin_detail")) continue;
                var centre=q["centre"].Point; var u=IslandPoint.Heading(q.N("long_axis_true_deg",90));
                double rx=q["radii_km"][0].Number(),ry=q["radii_km"][1].Number(),water=q.N("elevation_ft")*Feet,depth=q.N("depth_m");
                var cfg=q["basin_detail"]; double core=cfg.N("ridge_offset_m"),width=cfg.N("bank_width_m"),ridge=cfg.N("ridge_height_m");
                Add(layers,Rect.Around(centre,Math.Max(rx,ry)*(1+width/(Math.Min(rx,ry)*1000))),(p,h)=> {
                    var dp=p-centre; double a=dp.Dot(u)/rx,b=(dp.E*u.N-dp.N*u.E)/ry,rr=Math.Sqrt(a*a+b*b);
                    if(rr<=1) return water-depth*(1-Smooth(rr));
                    double dd=(rr-1)*Math.Min(rx,ry)*1000;
                    if(dd<core) return water+ridge*Smooth(dd/core);
                    if(dd<width) return Mix(h,water+ridge,1-Smooth((dd-core)/(width-core)));
                    return h;
                });
            }
            foreach(var q in W["mesa_pads"].Items)
            {
                var u=IslandPoint.Heading(q.N("heading_true_deg")); var centre=q["centre"].Point;
                double length=q.N("length_m")/2000,width=q.N("width_m")/2000,blend=q.N("blend_m")/1000,elevation=q.N("elevation_ft")*Feet;
                Add(layers,Rect.Around(centre,Math.Sqrt(length*length+width*width)+blend),(p,h)=> {
                    var d=p-centre; double da=Math.Max(0,Math.Abs(d.Dot(u))-length),db=Math.Max(0,Math.Abs(d.E*u.N-d.N*u.E)-width);
                    return Mix(h,elevation,1-Smooth(Math.Sqrt(da*da+db*db)/blend));
                });
            }
        }
        return new Grid<Layer>(layers);
    }
    private static Grid<Budget> BuildBudgets() => new(W["clearance_budgets"].Items.Select(q=>new Budget(q)).Select(b=>(b.Bounds,b)).ToList());
    private static Grid<IslandField> BuildFields() => new(IslandTerrain.Fields.Select(f=> {
        double a=f.Length/2000+f.EndShoulder/1000,b=f.Width/2000+f.Shoulder/1000;
        double e=Math.Abs(f.U.E)*a+Math.Abs(f.V.E)*b+f.Blend/1000,n=Math.Abs(f.U.N)*a+Math.Abs(f.V.N)*b+f.Blend/1000;
        return (new Rect(f.Centre.E-e,f.Centre.E+e,f.Centre.N-n,f.Centre.N+n),f);
    }).ToList());
    private static double Layers(Grid<Layer> grid,IslandPoint p,double h,bool indexed)
    { foreach(var layer in indexed?grid.At(p):grid.All) h=layer(p,h); return h; }
    internal static double Evaluate(IslandPoint p,bool indexed=true)
    {
        // Original coast ownership and composition are preserved, including outer-island overwrite.
        // Outside the mainland's .35 km envelope its negative shore distance can only
        // lead to ocean (-30) or an outer-island overwrite; its exact magnitude is unused.
        double dm=indexed?(MainlandBounds.Contains(p)?MainlandCoast.Shore(p):-100):IslandTerrain.Shore(p,Land[0]);
        double dc=CatalinaBounds.Contains(p)?(indexed?CatalinaCoast.Shore(p):IslandTerrain.Shore(p,Land[1])):-100,d=Math.Max(dm,dc);
        bool owner=false,volcanoProtected=false;
        var outer=indexed?Outer.At(p):Outer.All;
        foreach(var q in outer)
        {
            if(!q.Bounds.Contains(p)) continue;
            double dd=indexed?q.Coast.Shore(p):IslandTerrain.Shore(p,q.Polygon);
            if(dd>d) { d=dd; owner=true; }
            if(q.Volcano&&(p-q.VolcanoCentre).Length<=q.VolcanoRadius) volcanoProtected=true;
        }
        if(d<-.35) return -30;
        double h=(dc>dm?30:30+4570*Smooth(dm/2))*Feet;
        h=Layers(BeforeOuter,p,h,indexed);
        if(owner)
        {
            h=-30;
            foreach(var q in outer)
            {
                if(!q.Bounds.Contains(p)) continue; double sd=indexed?q.Coast.Shore(p):IslandTerrain.Shore(p,q.Polygon); if(sd<=0) continue;
                h=Math.Max(h,q.Height(p,sd));
            }
        }
        h=Layers(AfterOuter,p,h,indexed);
        var budgets=indexed?Budgets.At(p):Budgets.All;
        if(!volcanoProtected) foreach(var b in budgets) h=b.Apply(p,h,1,true);
        h=Layers(AfterBudget,p,h,indexed);
        double nearest=double.PositiveInfinity,bestA=0,bestZ=0,sumW=0,sumZ=0,maxA=0;
        foreach(var f in indexed?Fields.At(p):Fields.All)
        {
            var c=f.Centre; double l=f.Length/1000;
            if(f.Field=="DRAKE")
            {
                if(p.N<DrakeMouth) continue;
                c+=f.U*(-l/2+DrakeExterior/2); l=DrakeExterior;
            }
            var delta=p-c; double ad=delta.Dot(f.U),cr=delta.Dot(f.V);
            double da=Math.Max(0,Math.Abs(ad)-l/2-f.EndShoulder/1000),db=Math.Max(0,Math.Abs(cr)-f.Width/2000-f.Shoulder/1000),dd=Math.Sqrt(da*da+db*db);
            if(dd>=f.Blend/1000) continue;
            double a=1-Smooth(dd/(f.Blend/1000)),zz=f.Elevation+Math.Clamp(ad,-l/2,l/2)*1000*f.Slope/100-(f.Surface=="water"?WaterOffset:0);
            if(dd<nearest||(dd==nearest&&a<bestA)) { nearest=dd; bestA=a; bestZ=zz; }
            maxA=Math.Max(maxA,a); if(dd>0) { double wt=a/(dd*dd); sumW+=wt; sumZ+=wt*zz; }
        }
        double release=1;
        foreach(var c in indexed?PeakCores.At(p):PeakCores.All) release=Math.Min(release,Smooth(((p-c).Length-.12)/.28));
        if(nearest<double.PositiveInfinity)
        {
            h=Mix(h,nearest>1e-10?sumZ/sumW:bestZ,nearest>1e-10?maxA:bestA);
            if(nearest>1e-10&&!volcanoProtected) foreach(var b in budgets) h=b.Apply(p,h,maxA*release*Smooth(nearest/.03),false);
        }
        double padRelease=nearest<double.PositiveInfinity?Smooth(nearest/.03):1;
        foreach(var b in budgets)
        {
            if(!b.Airway) continue;
            double alpha=(1-Smooth((IslandTerrain.Segment(p,b.A,b.B).d-3.704)/b.Blend))*padRelease*release;
            h=Mix(h,Math.Min(h,b.Cap),alpha);
        }
        return -30+(h+30)*Smooth(d/ShoreFade);
    }
}
