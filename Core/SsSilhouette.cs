using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry.Intersect;



namespace SharpShadow
{
       public class SsSilhouette
    {
        // Fields
        // Crv itself
        public Curve SilCrv;
        public SilhouetteType SilType;
        public double Depth;
        // internal code
        public int CrvCode;
        private static int _nextNumber = 1;

        // Parent geometry
        public Guid ParentID;
        public ObjectType ParentType;
        public bool CreateShadow;
        public GeometryBase GeometryReference { get; private set; }

        // Spliting the geometry
        public bool IsProfile;
        public bool IsFailedCutter; // Internal flag to indicate if splitting failed
        public List<GeometryBase> JeongParts; // Light parts (陽)
        public List<GeometryBase> JamParts;   // Shadow parts (陰)
        public List<GeometryBase> JingParts; // Shade parts (影) for advanced classification

        // Planed curve
        public Curve PlanedCurve;
        public List<int> IntersectedCrvCode;
        private Plane supportPlane; // Store the support plane for Z.Min calculation

        // Methods
        // Constructor
        // Base constructor (without log)
        public SsSilhouette(GeometryBase geometry, SilhouetteType silhouetteType, double tolerance, double angleToleranceRadians, string viewName)
        {
            //basic settings
            SilType = silhouetteType;
            IsProfile = true;

            if (geometry != null)
            {
                GeometryReference = geometry;
                ParentID = Guid.NewGuid();
                ParentType = geometry.ObjectType;
            }
            else
            {
                ParentID = Guid.Empty;
                ParentType = ObjectType.None;
            }

            //get the view
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (string.IsNullOrEmpty(viewName))
            {
                RhinoView activeView = doc?.Views.ActiveView;
                viewName = activeView?.ActiveViewport.Name ?? "";
            }

            RhinoView targetView = doc?.Views.Find(viewName, true);
            if (targetView == null)
            {
                SilCrv = null;
                Depth = 0.0;
                PlanedCurve = null;
                goto InitializeRest;
            }

            // from view to plane and direction
            RhinoViewport viewport = targetView.ActiveViewport;
            Vector3d parallelCameraDirection = viewport.CameraDirection;
            supportPlane = new Plane();
            if (!viewport.GetFrustumNearPlane(out Plane nPlane))
            {
                SilCrv = null;
                Depth = 0.0;
                PlanedCurve = null;
                goto InitializeRest;
            }
            supportPlane = nPlane;

            // compute silhouettes
            Silhouette[] silhouettes = Silhouette.Compute(geometry, silhouetteType, parallelCameraDirection, tolerance, angleToleranceRadians);
            if (silhouettes != null && silhouettes.Length > 0)
            {
                SilCrv = Curve.JoinCurves(silhouettes.Select(s => s.Curve), tolerance).FirstOrDefault();
                Depth = CalculateDepth(geometry, supportPlane);
                if (SilCrv != null && supportPlane.IsValid)
                {
                    PlanedCurve = Curve.ProjectToPlane(SilCrv, supportPlane);
                }
                else
                {
                    PlanedCurve = null;
                }
            }
            else
            {
                SilCrv = null;
                Depth = 0.0;
                PlanedCurve = null;
            }

        // Initialize rest
        InitializeRest:
            CreateShadow = true;
            CrvCode = _nextNumber++;
            IntersectedCrvCode = new List<int>();
            IsFailedCutter = false;
            JeongParts = new List<GeometryBase>();
            JamParts = new List<GeometryBase>();
        }

        // Constructor with log support
        public SsSilhouette(GeometryBase geometry, SilhouetteType silhouetteType, double tolerance, double angleToleranceRadians, string viewName, out List<string> logMessages)
            : this(geometry, silhouetteType, tolerance, angleToleranceRadians, viewName) // Call base constructor
        {
            logMessages = new List<string>();
            IsProfile = true;
            try
            {
                if (geometry != null)
                {
                    logMessages.Add($"Geometry stored internally, Temporary ParentID: {ParentID}, Type: {ParentType}");
                }
                else
                {
                    logMessages.Add("No geometry provided.");
                }

                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (string.IsNullOrEmpty(viewName))
                {
                    logMessages.Add($"View name not provided, using active view: {viewName}");
                }

                RhinoView targetView = doc?.Views.Find(viewName, true);
                if (targetView == null)
                {
                    logMessages.Add($"Error: View '{viewName}' not found.");
                }
                else if (!targetView.ActiveViewport.GetFrustumNearPlane(out _))
                {
                    logMessages.Add("Error: Failed to get near plane.");
                }
                else
                {
                    logMessages.Add($"Support plane set, Normal: {supportPlane.Normal}");
                }

                if (SilCrv != null)
                {
                    logMessages.Add($"Silhouettes computed, Count: {SilCrv.DuplicateSegments().Length}, SilCrv Length: {SilCrv.GetLength()}");
                    if (PlanedCurve != null)
                    {
                        logMessages.Add($"PlanedCurve generated, Length: {PlanedCurve.GetLength()}");
                    }
                    else
                    {
                        logMessages.Add("Error: SilCrv or support plane invalid, PlanedCurve set to null.");
                    }
                }
                else
                {
                    logMessages.Add("Error: No silhouettes computed.");
                }
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception in constructor: {ex.Message}");
            }
        }


        public SsSilhouette(GeometryBase geometry, SilhouetteType silhouetteType, double tolerance, double angleToleranceRadians, string viewName, bool createShadow, out List<string> logMessages)
            : this(geometry, silhouetteType, tolerance, angleToleranceRadians, viewName) // Call base constructor
        {
            logMessages = new List<string>();
            IsProfile = true;
            CreateShadow = createShadow;

            try
            {
                if (geometry != null)
                {
                    logMessages.Add($"Geometry stored internally, Temporary ParentID: {ParentID}, Type: {ParentType}");
                }
                else
                {
                    logMessages.Add("No geometry provided.");
                }

                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (string.IsNullOrEmpty(viewName))
                {
                    logMessages.Add($"View name not provided, using active view: {viewName}");
                }

                RhinoView targetView = doc?.Views.Find(viewName, true);
                if (targetView == null)
                {
                    logMessages.Add($"Error: View '{viewName}' not found.");
                }
                else if (!targetView.ActiveViewport.GetFrustumNearPlane(out _))
                {
                    logMessages.Add("Error: Failed to get near plane.");
                }
                else
                {
                    logMessages.Add($"Support plane set, Normal: {supportPlane.Normal}");
                }

                if (SilCrv != null)
                {
                    logMessages.Add($"Silhouettes computed, Count: {SilCrv.DuplicateSegments().Length}, SilCrv Length: {SilCrv.GetLength()}");
                    if (PlanedCurve != null)
                    {
                        logMessages.Add($"PlanedCurve generated, Length: {PlanedCurve.GetLength()}");
                    }
                    else
                    {
                        logMessages.Add("Error: SilCrv or support plane invalid, PlanedCurve set to null.");
                    }
                }
                else
                {
                    logMessages.Add("Error: No silhouettes computed.");
                }
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception in constructor: {ex.Message}");
            }
        }







        // Reset CrvCode
        public static void ResetCrvCode()
        {
            _nextNumber = 1;
        }

        // Calculate depth
        private double CalculateDepth(GeometryBase geometry, Plane nearPlane)
        {
            if (geometry == null) return 0.0;
            BoundingBox bbox = geometry.GetBoundingBox(true);
            return bbox.IsValid ? bbox.Min.Z : 0.0;
        }

        // DivideChiaroSicuro (without log)
        // DivideChiaroSicuro (without log)
        public GeometryBase[] DivideChiaroSicuro(double tolerance)
        {
            IsFailedCutter = false;

            if (GeometryReference == null)
            {
                IsFailedCutter = true;
                return null;
            }

            if (SilCrv == null || !SilCrv.IsValid)
            {
                IsFailedCutter = true;
                return null;
            }

            Curve[] joinedCurves = Curve.JoinCurves(new[] { SilCrv }, tolerance);
            if (joinedCurves == null || joinedCurves.Length == 0)
            {
                IsFailedCutter = true;
                return null;
            }

            GeometryBase[] splitResults = null;
            switch (ParentType)
            {
                case ObjectType.Brep:
                    Brep brep = GeometryReference as Brep;
                    if (brep != null)
                    {
                        splitResults = brep.Split(joinedCurves, tolerance);
                    }
                    break;

                case ObjectType.Mesh:
                    Mesh mesh = GeometryReference as Mesh;
                    if (mesh != null)
                    {
                        List<PolylineCurve> polylineCurves = joinedCurves.Select(c => c.ToPolyline(0.01, 0.01, 0, 0)).ToList();
                        splitResults = mesh.SplitWithProjectedPolylines(polylineCurves, tolerance);
                    }
                    break;

                default:
                    IsFailedCutter = true;
                    return null;
            }

            if (splitResults == null || splitResults.Length < 2)
            {
                IsFailedCutter = true;
                return null;
            }

            return splitResults;
        }


        // DivideChiaroSicuro with log support
        public GeometryBase[] DivideChiaroSicuro(double tolerance, List<string> logMessages)
        {
            IsFailedCutter = false;

            try
            {
                if (GeometryReference == null)
                {
                    logMessages.Add($"Error: Geometry reference not found for CrvCode {CrvCode}");
                    IsFailedCutter = true;
                    return null;
                }

                ObjectType geoType = ParentType;

                if (SilCrv == null || !SilCrv.IsValid)
                {
                    logMessages.Add($"Error: SilCrv is invalid or null for CrvCode {CrvCode}");
                    IsFailedCutter = true;
                    return null;
                }

                Curve[] joinedCurves = Curve.JoinCurves(new[] { SilCrv }, tolerance);
                if (joinedCurves == null || joinedCurves.Length == 0)
                {
                    logMessages.Add($"Error: Failed to join SilCrv for CrvCode {CrvCode}");
                    IsFailedCutter = true;
                    return null;
                }

                GeometryBase[] splitResults = null;
                switch (geoType)
                {
                    case ObjectType.Brep:
                        Brep brep = GeometryReference as Brep;
                        if (brep == null)
                        {
                            logMessages.Add($"Error: Failed to cast geometry to Brep for CrvCode {CrvCode}");
                            IsFailedCutter = true;
                            return null;
                        }
                        splitResults = brep.Split(joinedCurves, tolerance);
                        break;

                    case ObjectType.Mesh:
                        Mesh mesh = GeometryReference as Mesh;
                        if (mesh == null)
                        {
                            logMessages.Add($"Error: Failed to cast geometry to Mesh for CrvCode {CrvCode}");
                            IsFailedCutter = true;
                            return null;
                        }
                        List<PolylineCurve> polylineCurves = joinedCurves.Select(c => c.ToPolyline(0.01, 0.01, 0, 0)).ToList();
                        splitResults = mesh.SplitWithProjectedPolylines(polylineCurves, tolerance);
                        break;

                    default:
                        logMessages.Add($"Error: Geometry type {geoType} is not supported for splitting in CrvCode {CrvCode}");
                        IsFailedCutter = true;
                        return null;
                }

                if (splitResults == null || splitResults.Length < 2)
                {
                    logMessages.Add($"Error: Failed to split geometry for CrvCode {CrvCode}. Split parts: {(splitResults?.Length ?? 0)}");
                    IsFailedCutter = true;
                    return null;
                }

                logMessages.Add($"A {geoType} object was successfully split into {splitResults.Length} parts for CrvCode {CrvCode}");
                return splitResults;
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception in DivideChiaroSicuro for CrvCode {CrvCode}: {ex.Message}");
                logMessages.Add($"Stack Trace: {ex.StackTrace}");
                IsFailedCutter = true;
                return null;
            }
        }


        // DivideChiaroSicuroAdvanced
        public GeometryBase[] DivideChiaroSicuroAdvanced(double tolerance, List<string> logMessages)
        {
            try
            {
                if (!IsFailedCutter)
                {
                    logMessages.Add($"Advanced split skipped: Initial split succeeded for CrvCode {CrvCode}");
                    return null;
                }

                logMessages.Add($"Initiating advanced split for CrvCode {CrvCode} with ParentID {ParentID}");

                // 输入验证
                if (GeometryReference == null)
                {
                    logMessages.Add($"Failed: Geometry reference is null for CrvCode {CrvCode}");
                    return null;
                }
                if (SilCrv == null || !SilCrv.IsValid)
                {
                    logMessages.Add($"Failed: SilCrv is null or invalid for CrvCode {CrvCode}");
                    return null;
                }
                //logMessages.Add($"Validated SilCrv, length: {SilCrv.GetLength()}");

                // 分解 SilCrv
                Curve[] curveSegments = SilCrv.DuplicateSegments();
                if (curveSegments == null || curveSegments.Length == 0)
                {
                    logMessages.Add($"Failed: No segments extracted from SilCrv for CrvCode {CrvCode}");
                    return null;
                }
                //logMessages.Add($"Decomposed SilCrv into {curveSegments.Length} segments");

                List<GeometryBase> allSplitParts = new List<GeometryBase>();

                if (ParentType != ObjectType.Brep)
                {
                    logMessages.Add($"Failed: Geometry type {ParentType} not supported for advanced split");
                    return null;
                }

                Brep brep = GeometryReference as Brep;
                if (brep == null)
                {
                    logMessages.Add($"Failed: Geometry cast to Brep failed for CrvCode {CrvCode}");
                    return null;
                }

                // 分类 SilCrv：边缘类和跨越类
                List<Curve> edgeCurves = new List<Curve>();
                List<Curve> crossingCurves = new List<Curve>();
                foreach (Curve segment in curveSegments)
                {
                    bool isEdgeCurve = false;
                    foreach (BrepEdge edge in brep.Edges)
                    {
                        CurveIntersections intersections = Intersection.CurveCurve(segment, edge, tolerance, tolerance);
                        if (intersections != null)
                        {
                            foreach (var intersection in intersections)
                            {
                                if (intersection.IsOverlap)
                                {
                                    isEdgeCurve = true;
                                    break;
                                }
                            }
                        }
                        if (isEdgeCurve) break;
                    }

                    if (isEdgeCurve)
                    {
                        edgeCurves.Add(segment);
                    }
                    else
                    {
                        crossingCurves.Add(segment);
                    }
                }
                logMessages.Add($"Classified {curveSegments.Length} curves: {edgeCurves.Count} edge, {crossingCurves.Count} crossing");

                // 分类 BrepFace：被跨越的曲面和完整的曲面
                BrepFace[] faces = brep.Faces.ToArray();
                //logMessages.Add($"Processing Brep with {faces.Length} faces");
                List<BrepFace> crossedFaces = new List<BrepFace>();
                List<BrepFace> intactFaces = new List<BrepFace>();
                Dictionary<BrepFace, List<Curve>> faceCutters = new Dictionary<BrepFace, List<Curve>>();

                foreach (BrepFace face in faces)
                {
                    double originalArea = AreaMassProperties.Compute(face)?.Area ?? 0.0;
                    //logMessages.Add($"Analyzing face, area: {originalArea}");

                    List<Curve> cutters = new List<Curve>();
                    foreach (Curve segment in crossingCurves)
                    {
                        Point3d midPoint = segment.PointAt(segment.Domain.Mid);
                        double u, v;
                        if (face.UnderlyingSurface().ClosestPoint(midPoint, out u, out v))
                        {
                            Point3d surfacePoint = face.UnderlyingSurface().PointAt(u, v);
                            if (surfacePoint.DistanceTo(midPoint) < tolerance)
                            {
                                cutters.Add(segment);
                            }
                        }
                    }

                    if (cutters.Count > 0)
                    {
                        crossedFaces.Add(face);
                        faceCutters[face] = cutters;
                        //logMessages.Add($"Face marked as crossed with {cutters.Count} crossing curves");
                    }
                    else
                    {
                        intactFaces.Add(face);
                    }
                }
                logMessages.Add($"Classified faces: {crossedFaces.Count} crossed faces, {intactFaces.Count} intact faces");

                // 分割被跨越的 BrepFace（转换为 Brep 并用 Brep.Split）
                foreach (BrepFace face in crossedFaces)
                {
                    double originalArea = AreaMassProperties.Compute(face)?.Area ?? 0.0;
                    List<Curve> cutters = faceCutters[face];
                    logMessages.Add($"Splitting face (area: {originalArea}) with {cutters.Count} crossing curves");

                    Brep singleFaceBrep = face.ToBrep();
                    if (singleFaceBrep == null)
                    {
                        logMessages.Add($"Warning: Failed to convert face to Brep, retaining original");
                        allSplitParts.Add(face.ToBrep());
                        continue;
                    }

                    //splitting here
                    Brep[] splitBreps = singleFaceBrep.Split(cutters, tolerance);
                    if (splitBreps == null || splitBreps.Length == 0)
                    {
                        logMessages.Add($"Warning: Brep split failed with {cutters.Count} curves, retaining original");
                        allSplitParts.Add(singleFaceBrep);
                    }
                    else
                    {
                        var splitAreas = splitBreps.Select(b => AreaMassProperties.Compute(b)?.Area ?? 0.0);
                        string areasString = "{" + string.Join(", ", splitAreas) + "}";
                        logMessages.Add($"Split successful, resulted in {splitBreps.Length} parts");
                        logMessages.Add($"  Split parts areas: {areasString}");
                        allSplitParts.AddRange(splitBreps);
                        /*foreach (Brep splitBrep in splitBreps)
                        {
                            double partArea = AreaMassProperties.Compute(splitBrep)?.Area ?? 0.0;
                            if (partArea > originalArea * 0.01)
                            {
                                
                            }
                            else
                            {
                                logMessages.Add($"Filtered out fragment with area {partArea} (below threshold)");
                            }
                        }*/
                    }
                }

                // 添加完整的 BrepFace
                foreach (BrepFace face in intactFaces)
                {
                    Brep singleFaceBrep = face.DuplicateFace(true); // 复制面，保留修剪
                    allSplitParts.Add(singleFaceBrep);
                    //logMessages.Add($"Retained intact face, area: {AreaMassProperties.Compute(face)?.Area ?? 0.0}");
                }

                logMessages.Add($"Collected {allSplitParts.Count} split parts");

                if (allSplitParts.Count == 0)
                {
                    logMessages.Add($"Failed: No valid split parts generated for CrvCode {CrvCode}");
                    IsFailedCutter = true;
                    return null;
                }

                return allSplitParts.ToArray();
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception occurred in advanced split for CrvCode {CrvCode}: {ex.Message}");
                logMessages.Add($"Stack Trace: {ex.StackTrace}");
                IsFailedCutter = true;
                return new GeometryBase[] { GeometryReference };
            }
        }




        public void ClassifyJeong2JamParts(GeometryBase[] splitResults, double tolerance, List<string> logMessages)
        {
            JeongParts.Clear();
            JamParts.Clear();

            try
            {
                if (splitResults == null || splitResults.Length != 2)
                {
                    logMessages.Add($"Error: Expected exactly 2 parts for classification in ClassifyJeong2JamParts for CrvCode {CrvCode}, got {splitResults?.Length ?? 0}");
                    return;
                }

                GeometryBase part1 = splitResults[0];
                GeometryBase part2 = splitResults[1];

                if (part1 == null || part2 == null)
                {
                    logMessages.Add($"Error: One or both parts are null in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                    return;
                }

                Point3d center1, center2;

                if (ParentType == ObjectType.Brep)
                {
                    Brep brep1 = part1 as Brep;
                    Brep brep2 = part2 as Brep;

                    if (brep1 == null || brep2 == null || brep1.Faces.Count == 0 || brep2.Faces.Count == 0)
                    {
                        logMessages.Add($"Error: One or both parts are not valid Breps in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                        return;
                    }

                    // Compute center points for Brep faces
                    center1 = SsUtils.ComputeTrimmedSurfaceCenter(brep1.Faces[0], logMessages, 0, tolerance);
                    center2 = SsUtils.ComputeTrimmedSurfaceCenter(brep2.Faces[0], logMessages, 1, tolerance);
                }
                else if (ParentType == ObjectType.Mesh)
                {
                    Mesh mesh1 = part1 as Mesh;
                    Mesh mesh2 = part2 as Mesh;

                    if (mesh1 == null || mesh2 == null || !mesh1.IsValid || !mesh2.IsValid)
                    {
                        logMessages.Add($"Error: One or both parts are not valid Meshes in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                        return;
                    }

                    // Compute Mesh centroids
                    center1 = ComputeMeshCentroid(mesh1);
                    center2 = ComputeMeshCentroid(mesh2);
                }
                else
                {
                    logMessages.Add($"Error: Unsupported ParentType {ParentType} in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                    return;
                }

                if (!center1.IsValid || !center2.IsValid)
                {
                    logMessages.Add($"Error: Invalid center points in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                    return;
                }

                // Project to supportPlane
                Point3d projected1 = supportPlane.ClosestPoint(center1);
                Point3d projected2 = supportPlane.ClosestPoint(center2);

                if (!projected1.IsValid || !projected2.IsValid)
                {
                    logMessages.Add($"Error: Invalid projected points in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                    return;
                }

                // Create rays
                Line ray1 = new Line(center1, supportPlane.Normal, 1000.0);
                Line ray2 = new Line(center2, supportPlane.Normal, 1000.0);

                if (!ray1.IsValid || !ray2.IsValid)
                {
                    logMessages.Add($"Error: Invalid rays in ClassifyJeong2JamParts for CrvCode {CrvCode}");
                    return;
                }

                int count1 = 0, count2 = 0;

                if (ParentType == ObjectType.Brep)
                {
                    Brep brep1 = part1 as Brep;
                    Brep brep2 = part2 as Brep;

                    // Ray1 vs part2
                    var intersections1 = Intersection.CurveBrep(ray1.ToNurbsCurve(), brep2, tolerance, out _, out Point3d[] points1);
                    count1 = intersections1 ? points1.Length : 0;

                    // Ray2 vs part1
                    var intersections2 = Intersection.CurveBrep(ray2.ToNurbsCurve(), brep1, tolerance, out _, out Point3d[] points2);
                    count2 = intersections2 ? points2.Length : 0;
                }
                else if (ParentType == ObjectType.Mesh)
                {
                    Mesh mesh1 = part1 as Mesh;
                    Mesh mesh2 = part2 as Mesh;

                    // Ray1 vs part2
                    double t1 = Intersection.MeshRay(mesh2, new Ray3d(ray1.From, ray1.Direction));
                    count1 = t1 >= 0 ? 1 : 0;

                    // Ray2 vs part1
                    double t2 = Intersection.MeshRay(mesh1, new Ray3d(ray2.From, ray2.Direction));
                    count2 = t2 >= 0 ? 1 : 0;
                }

                // Classify: count == 0 for Jeong (light), count >= 1 for Jam (shadow)
                if (count1 == 0)
                    JeongParts.Add(part1);
                else
                    JamParts.Add(part1);

                if (count2 == 0)
                    JeongParts.Add(part2);
                else
                    JamParts.Add(part2);

                logMessages.Add($"Basic classification complete for CrvCode {CrvCode}: JeongParts: {JeongParts.Count}, JamParts: {JamParts.Count}");
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception in ClassifyJeong2JamParts for CrvCode {CrvCode}: {ex.Message}");
                logMessages.Add($"Stack Trace: {ex.StackTrace}");
                JeongParts.Clear();
                JamParts.Clear();
            }
        }

        // Helper method for Mesh centroid
        private Point3d ComputeMeshCentroid(Mesh mesh)
        {
            var areaProps = AreaMassProperties.Compute(mesh);
            return areaProps != null && areaProps.Centroid.IsValid ? areaProps.Centroid : Point3d.Origin;
        }



        public void ClassifyJeongMultiJamParts(GeometryBase[] splitResults, double tolerance, List<string> logMessages,
    out List<Point3d> centerPoints, out List<Point3d> allIntersectionPoints, out List<int> intersectionCounts)
        {
            JeongParts.Clear();
            JamParts.Clear();

            centerPoints = new List<Point3d>();
            allIntersectionPoints = new List<Point3d>();
            intersectionCounts = new List<int>();

            try
            {
                if (splitResults == null || splitResults.Length == 0)
                {
                    logMessages.Add($"Error: No parts to classify in ClassifyJeongMultiJamParts for CrvCode {CrvCode}");
                    return;
                }

                List<(GeometryBase Part, int IntersectionCount, double Area, double BoundaryLength)> partIntersections = new List<(GeometryBase, int, double, double)>();

                int partIndex = 0;
                foreach (GeometryBase part in splitResults)
                {
                    if (part == null)
                    {
                        logMessages.Add($"Warning: Encountered null part in advanced classification at index {partIndex}");
                        partIndex++;
                        continue;
                    }

                    // 假设 part 是单面 Brep
                    Brep brepPart = part as Brep;
                    if (brepPart == null || brepPart.Faces.Count != 1)
                    {
                        logMessages.Add($"Warning: Part at index {partIndex} is not a single-face Brep, skipping classification");
                        partIndex++;
                        continue;
                    }

                    BrepFace face = brepPart.Faces[0];
                    Surface srf = face.UnderlyingSurface();
                    double partArea = AreaMassProperties.Compute(brepPart)?.Area ?? 0.0;
                    double boundaryLength = brepPart.Edges.Sum(e => e.GetLength());

                    // 获取曲面 Domain
                    Interval uDomain = srf.Domain(0);
                    Interval vDomain = srf.Domain(1);
                    logMessages.Add($"Part {partIndex}, surface domain: U[{uDomain.Min}, {uDomain.Max}], V[{vDomain.Min}, {vDomain.Max}]");

                    // 获取 Trim 信息并计算 UV 中心
                    Point2d uvCenter = Point2d.Unset;
                    int uvSampleCount = 0;
                    double uSum = 0.0, vSum = 0.0;

                    foreach (BrepLoop loop in face.Loops)
                    {
                        // 计算 loop 总长度和最短 trim 长度
                        double loopLength = 0.0;
                        double minTrimLength = double.MaxValue;
                        List<Curve> trimCurves = new List<Curve>();

                        foreach (BrepTrim trim in loop.Trims)
                        {
                            Curve trimCurve = trim.DuplicateCurve();
                            if (trimCurve == null)
                                continue;

                            double trimLength = trimCurve.GetLength();
                            loopLength += trimLength;
                            if (trimLength < minTrimLength)
                                minTrimLength = trimLength;
                            trimCurves.Add(trimCurve);
                        }

                        logMessages.Add($"Part {partIndex}, loop length: {loopLength}, min trim length: {minTrimLength}");

                        // 使用 DivideByLength 采样
                        double sampleLength = 0.5 * minTrimLength;
                        foreach (Curve trimCurve in trimCurves)
                        {
                            double[] tParams = trimCurve.DivideByLength(sampleLength, true);
                            if (tParams == null || tParams.Length == 0)
                            {
                                // 如果采样失败，尝试单个点
                                tParams = new[] { trimCurve.Domain.Mid };
                            }

                            foreach (double t in tParams)
                            {
                                Point3d uv = trimCurve.PointAt(t); // 获取 UV 坐标
                                uSum += uv.X;
                                vSum += uv.Y;
                                uvSampleCount++;
                            }
                        }
                    }

                    if (uvSampleCount > 0)
                    {
                        double uAvg = uSum / uvSampleCount;
                        double vAvg = vSum / uvSampleCount;
                        uvCenter = new Point2d(uAvg, vAvg);
                        logMessages.Add($"Part {partIndex}, trim UV center: ({uAvg}, {vAvg}), samples: {uvSampleCount}");
                    }
                    else
                    {
                        logMessages.Add($"Warning: No valid trim UV points for part {partIndex}, using surface center");
                        uvCenter = new Point2d(uDomain.Mid, vDomain.Mid); // 回退到曲面 Domain 中心
                    }

                    // 使用 PointAt 获取中心点
                    Point3d centerPoint = srf.PointAt(uvCenter.X, uvCenter.Y);
                    if (!centerPoint.IsValid)
                    {
                        logMessages.Add($"Warning: Invalid center point for part {partIndex} at UV ({uvCenter.X}, {uvCenter.Y}), skipping");
                        partIndex++;
                        continue;
                    }

                    // 检查中心点重复
                    if (centerPoints.Any(p => p.DistanceTo(centerPoint) < tolerance))
                    {
                        logMessages.Add($"Warning: Duplicate center point detected for part {partIndex}, area: {partArea}, center: {centerPoint}");
                    }

                    centerPoints.Add(centerPoint);
                    logMessages.Add($"Part {partIndex}, center point: {centerPoint}, area: {partArea}, boundary length: {boundaryLength}");

                    // 投影中心点到 supportPlane
                    Point3d projectedPoint = supportPlane.ClosestPoint(centerPoint);
                    if (!projectedPoint.IsValid)
                    {
                        logMessages.Add($"Warning: Invalid projected point for part {partIndex}, skipping");
                        partIndex++;
                        continue;
                    }

                    // 使用视平面法线
                    Line ray = new Line(centerPoint, supportPlane.Normal, 1000.0);
                    if (!ray.IsValid)
                    {
                        logMessages.Add($"Warning: Invalid ray for part {partIndex}, skipping");
                        partIndex++;
                        continue;
                    }

                    // 计算交点
                    int intersectionCount = 0;
                    List<Point3d> partIntersectionPoints = new List<Point3d>();
                    int otherIndex = 0;
                    foreach (GeometryBase otherPart in splitResults)
                    {
                        if (otherPart == null || otherPart == part)
                        {
                            otherIndex++;
                            continue;
                        }

                        Brep otherBrep = otherPart as Brep;
                        if (otherBrep == null)
                        {
                            otherIndex++;
                            continue;
                        }

                        var intersections = Intersection.CurveBrep(ray.ToNurbsCurve(), otherBrep, tolerance, out Curve[] _, out Point3d[] intersectionPoints);
                        if (intersections && intersectionPoints.Length > 0)
                        {
                            intersectionCount += intersectionPoints.Length;
                            partIntersectionPoints.AddRange(intersectionPoints);
                            logMessages.Add($"Part {partIndex} ray intersects part {otherIndex}, points: [{string.Join(", ", intersectionPoints.Select(p => p.ToString()))}]");
                        }

                        otherIndex++;
                    }

                    allIntersectionPoints.AddRange(partIntersectionPoints);
                    intersectionCounts.Add(intersectionCount);
                    logMessages.Add($"Part {partIndex} classified, intersection count: {intersectionCount}");

                    partIntersections.Add((part, intersectionCount, partArea, boundaryLength));
                    partIndex++;
                }

                // 分类（使用日志中的规则：count == 0 为阳面）
                foreach (var (part, count, area, _) in partIntersections)
                {
                    if (count == 0)
                        JeongParts.Add(part);
                    else
                        JamParts.Add(part);
                }

                logMessages.Add($"Advanced classification complete: JeongParts: {JeongParts.Count}, JamParts: {JamParts.Count}");

                if (JeongParts.Count < 1 || JamParts.Count < 1)
                {
                    logMessages.Add($"Warning: Advanced classification incomplete - JeongParts: {JeongParts.Count}, JamParts: {JamParts.Count}");
                }
            }
            catch (Exception ex)
            {
                logMessages.Add($"Exception in ClassifyJeongMultiJamParts for CrvCode {CrvCode}: {ex.Message}");
                logMessages.Add($"Stack Trace: {ex.StackTrace}");
                JeongParts.Clear();
                JamParts.Clear();
                centerPoints.Clear();
                allIntersectionPoints.Clear();
                intersectionCounts.Clear();
            }
        }
    }
}
