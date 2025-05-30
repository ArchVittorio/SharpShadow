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
    public class SsRegione
    {
        // Fields

        public Curve RegionCurve { get; private set; }
        public int RegionCode;
        private static int _nextNumber = 1;
        public Point3d CharCentroid;
        // not important
        public double Area;

        // Regione to Silhouette relation
        public List<int> ContainedbySilCode;
        public List<Guid> ContainedbySilGuid;  
        public List<int> HittedGeobyCode;     // Which SsSilhouettes contain this region
        public List<Guid> HittedGeobyGuid;         // Which Geometries are hitted by the ray from this region

        // regiones to regiones relation
        public List<int> ContainRgCode;      // Which other SsRegiones this region contains
        public bool IsIsolated;           // True if this region is isolated from other regions



        // methods
        // Private constructor for single region
        private SsRegione(Curve regionCurve, Plane testPlane, double tolerance)
        {
            RegionCode = _nextNumber++;
            ContainedbySilCode = new List<int>();
            ContainRgCode = new List<int>();

            if (regionCurve != null && regionCurve.IsClosed && regionCurve.IsPlanar())
            {
                Area = AreaMassProperties.Compute(regionCurve).Area;
            }
            else
            {
                Area = 0.0;
            }

            CharCentroid = CalculateCharacteristicPoint(regionCurve, testPlane, tolerance);
        }

        // Static method to create regions from SsSilhouettes
        private SsRegione(Curve regionCurve, Plane testPlane, double tolerance, out List<string> logMessages)
        {
            logMessages = new List<string>();
            logMessages.Add("Creating SsRegione instance");

            RegionCurve = regionCurve;
            RegionCode = _nextNumber++;
            ContainedbySilCode = new List<int>();
            ContainedbySilGuid = new List<Guid>();
            ContainRgCode = new List<int>();
            logMessages.Add($"Region {RegionCode}: Initialized properties, RegionCurve = {(regionCurve != null ? "non-null" : "null")}");

            if (regionCurve == null)
            {
                logMessages.Add($"Region {RegionCode}: RegionCurve is null.");
                return;
            }

            try
            {
                if (!regionCurve.IsClosed)
                {
                    logMessages.Add($"Region {RegionCode}: RegionCurve is not closed.");
                }
                else if (!regionCurve.IsPlanar(tolerance))
                {
                    logMessages.Add($"Region {RegionCode}: RegionCurve is not planar.");
                }
                else
                {
                    var areaProps = AreaMassProperties.Compute(regionCurve);
                    if (areaProps != null)
                    {
                        Area = areaProps.Area;
                        CharCentroid = areaProps.Centroid;
                        logMessages.Add($"Region {RegionCode}: Area = {Area}, Centroid = {CharCentroid}");
                    }
                    else
                    {
                        logMessages.Add($"Region {RegionCode}: Failed to compute area properties.");
                    }
                }
            }
            catch (Exception ex)
            {
                logMessages.Add($"Region {RegionCode}: Exception in constructor: {ex.Message}");
            }
        }




        //Create SsRegiones from input silhouette, using Curve.CreateBooleanRegions. Register the info of 
        //ContainedbySilCode + ContainRgCode
        public static List<SsRegione> CreateRegionsFromSilhouettes(List<SsSilhouette> silhouettes, Plane testPlane, double tolerance, out List<string> logMessages)
        {
            logMessages = new List<string>();
            List<SsRegione> regions = new List<SsRegione>();
            logMessages.Add($"Creating regions from {silhouettes?.Count ?? 0} silhouettes");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("No silhouettes provided.");
                return regions;
            }

            // Step 1: Collect PlanedCurves and map to SsSilhouette data
            List<Curve> planedCurves = new List<Curve>();
            Dictionary<Curve, (int CrvCode, Guid ParentID)> curveToSilData = new Dictionary<Curve, (int, Guid)>();
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null || silhouette.PlanedCurve == null || !silhouette.PlanedCurve.IsValid)
                {
                    logMessages.Add($"Skipped invalid silhouette, ParentID: {silhouette?.ParentID}");
                    continue;
                }
                planedCurves.Add(silhouette.PlanedCurve);
                curveToSilData[silhouette.PlanedCurve] = (silhouette.CrvCode, silhouette.ParentID);
            }
            logMessages.Add($"Collected {planedCurves.Count} valid curves");

            if (planedCurves.Count == 0)
            {
                logMessages.Add("No valid curves for region creation.");
                return regions;
            }

            // Step 2: Compute Boolean regions
            CurveBooleanRegions boolRegions = null;
            try
            {
                boolRegions = Curve.CreateBooleanRegions(planedCurves, testPlane, false, tolerance);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Boolean region calculation failed: {ex.Message}");
            }

            if (boolRegions == null || boolRegions.RegionCount == 0)
            {
                logMessages.Add("No regions generated.");
                return regions;
            }
            logMessages.Add($"Computed {boolRegions.RegionCount} regions");

            // Step 3: Create SsRegione instances
            List<Curve> regionCurves = new List<Curve>();
            for (int i = 0; i < boolRegions.RegionCount; i++)
            {
                try
                {
                    Curve[] regionCurve = boolRegions.RegionCurves(i);
                    if (regionCurve == null || regionCurve.Length == 0)
                    {
                        logMessages.Add($"Region {i}: No region curves.");
                        continue;
                    }

                    Curve[] joinedCurve = Curve.JoinCurves(regionCurve);
                    if (joinedCurve == null || joinedCurve.Length == 0 || joinedCurve[0] == null)
                    {
                        logMessages.Add($"Region {i}: Failed to join curves.");
                        continue;
                    }

                    regionCurves.Add(joinedCurve[0]);
                    var region = new SsRegione(joinedCurve[0], testPlane, tolerance, out var regionLogs);
                    regions.Add(region);
                    logMessages.AddRange(regionLogs);
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Region {i}: Failed to create region: {ex.Message}");
                }
            }

            // Step 4: Set fields for SsRegione
            for (int i = 0; i < regions.Count; i++)
            {
                try
                {
                    SsRegione regionA = regions[i];
                    Curve curveA = regionCurves[i];
                    Point3d centroidA = regionA.CharCentroid;
                    regionA.IsIsolated = false;

                    if (centroidA == Point3d.Unset)
                    {
                        logMessages.Add($"Region {regionA.RegionCode}: No centroid, skipping containment.");
                        continue;
                    }

                    int silCodeCount = 0, rgCodeCount = 0;
                    // Check if contained by original PlanedCurves
                    foreach (Curve inputCurve in planedCurves)
                    {
                        if (curveToSilData.ContainsKey(inputCurve) &&
                            SsUtils.IsPointInsideCurve(inputCurve, centroidA, testPlane, tolerance))
                        {
                            var (crvCode, parentID) = curveToSilData[inputCurve];
                            regionA.ContainedbySilCode.Add(crvCode);
                            regionA.ContainedbySilGuid.Add(parentID);
                            silCodeCount++;
                        }
                    }

                    // Check containment with other SsRegiones
                    for (int j = 0; j < regions.Count; j++)
                    {
                        if (i == j) continue;
                        Curve curveB = regionCurves[j];
                        if (Curve.PlanarClosedCurveRelationship(curveA, curveB, testPlane, tolerance) == RegionContainment.BInsideA)
                        {
                            regionA.ContainRgCode.Add(regions[j].RegionCode);
                            rgCodeCount++;
                        }
                    }
                    logMessages.Add($"Region {regionA.RegionCode}: Added {silCodeCount} silhouette codes, {rgCodeCount} region codes");
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Region {regions[i].RegionCode}: Failed to set fields: {ex.Message}");
                }
            }

            logMessages.Add($"Returning {regions.Count} regions");
            return regions;
        }

        // Compute the hitted geo info into the feilds of SsRegione. From the ContainedbySilCode to compute it.
        public void ComputeHitGeometries(List<SsSilhouette> silhouettes, SsSettings settings, Vector3d direction, double tolerance, out List<string> logMessages)
        {
            logMessages = new List<string>();
            logMessages.Add($"Computing hit geometries for Region {RegionCode}");

            // Initialize hit lists to avoid null reference errors
            HittedGeobyCode = new List<int>();
            HittedGeobyGuid = new List<Guid>();

            // Early validation
            if (CharCentroid == Point3d.Unset)
            {
                logMessages.Add($"Region {RegionCode}: No valid centroid, skipping.");
                return;
            }

            if (ContainedbySilCode == null || ContainedbySilCode.Count == 0)
            {
                logMessages.Add($"Region {RegionCode}: No ContainedbySilCode entries, skipping.");
                return;
            }

            // Early exit for single silhouette case
            if (ContainedbySilCode.Count == 1)
            {
                logMessages.Add($"Region {RegionCode}: ContainedbySilCode has single entry, setting hit lists to empty.");
                return;
            }

            logMessages.Add($"Region {RegionCode}: ContainedbySilCode: [{string.Join(", ", ContainedbySilCode)}]");

            // Map CrvCode to SsSilhouette
            Dictionary<int, SsSilhouette> codeToSilhouette = new Dictionary<int, SsSilhouette>();
            logMessages.Add($"Region {RegionCode}: Available silhouette CrvCodes: [{string.Join(", ", silhouettes?.Where(s => s != null).Select(s => s.CrvCode) ?? Enumerable.Empty<int>())}]");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add($"Region {RegionCode}: Silhouettes list is null or empty.");
                return;
            }

            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null)
                {
                    logMessages.Add($"Region {RegionCode}: Encountered null silhouette.");
                    continue;
                }
                if (silhouette.PlanedCurve == null || !silhouette.PlanedCurve.IsValid)
                {
                    logMessages.Add($"Region {RegionCode}: Invalid PlanedCurve for CrvCode: {silhouette.CrvCode}, ParentID: {silhouette.ParentID}");
                    continue;
                }
                codeToSilhouette[silhouette.CrvCode] = silhouette;
            }

            // Compute distances to geometries
            var hitDistances = new List<(double Distance, int CrvCode, Guid ParentID)>();
            foreach (int crvCode in ContainedbySilCode)
            {
                if (!codeToSilhouette.TryGetValue(crvCode, out var silhouette))
                {
                    logMessages.Add($"Region {RegionCode}: Silhouette for CrvCode {crvCode} not found.");
                    continue;
                }

                logMessages.Add($"Region {RegionCode}: Processing CrvCode {crvCode}, ParentType: {silhouette.ParentType}");

                try
                {
                    // Collect geometries (JeongParts or GeometryReference)
                    List<(GeometryBase Geo, ObjectType Type)> geometries = new List<(GeometryBase, ObjectType)>();
                    bool fromJeongParts = false;

                    if (silhouette.JeongParts != null && silhouette.JeongParts.Count > 0)
                    {
                        fromJeongParts = true;
                        logMessages.Add($"Region {RegionCode}: Processing JeongParts for CrvCode {crvCode}, Count: {silhouette.JeongParts.Count}");
                        foreach (var geom in silhouette.JeongParts)
                        {
                            if (geom == null)
                            {
                                logMessages.Add($"Region {RegionCode}: Null geometry in JeongParts for CrvCode {crvCode}.");
                                continue;
                            }
                            geometries.Add((geom, silhouette.ParentType));
                            logMessages.Add($"Region {RegionCode}: Added geometry of type {geom.GetType().Name} from JeongParts for CrvCode {crvCode}.");
                        }
                    }
                    else if (silhouette.GeometryReference != null)
                    {
                        logMessages.Add($"Region {RegionCode}: Processing GeometryReference for CrvCode {crvCode}, Type: {silhouette.GeometryReference.GetType().Name}");
                        geometries.Add((silhouette.GeometryReference, silhouette.ParentType));
                        logMessages.Add($"Region {RegionCode}: Added geometry of type {silhouette.GeometryReference.GetType().Name} from GeometryReference for CrvCode {crvCode}.");
                    }
                    else
                    {
                        logMessages.Add($"Region {RegionCode}: No valid geometry for CrvCode {crvCode} (JeongParts and GeometryReference are null).");
                        continue;
                    }

                    if (geometries.Count == 0)
                    {
                        logMessages.Add($"Region {RegionCode}: No valid geometries for CrvCode {crvCode} (from {(fromJeongParts ? "JeongParts" : "GeometryReference")}).");
                        continue;
                    }

                    // Process each geometry based on type
                    foreach (var (geo, type) in geometries)
                    {
                        if (geo == null || !geo.IsValid)
                        {
                            logMessages.Add($"Region {RegionCode}: Invalid geometry for CrvCode {crvCode}, Type: {type}.");
                            continue;
                        }

                        Point3d[] projectedPoints = null;
                        switch (type)
                        {
                            case ObjectType.Brep:
                                Brep brep = geo as Brep;
                                if (brep != null && brep.IsValid)
                                {
                                    projectedPoints = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBrepsEx(
                                        new[] { brep }, new[] { CharCentroid }, direction, tolerance, out int[] brepIndices);
                                    logMessages.Add($"Region {RegionCode}: Brep projection for CrvCode {crvCode}, Hits: {(projectedPoints?.Length ?? 0)}");
                                }
                                else
                                {
                                    logMessages.Add($"Region {RegionCode}: Invalid Brep for CrvCode {crvCode}.");
                                }
                                break;

                            case ObjectType.Mesh:
                                Mesh mesh = geo as Mesh;
                                if (mesh != null && mesh.IsValid)
                                {
                                    projectedPoints = Rhino.Geometry.Intersect.Intersection.ProjectPointsToMeshesEx(
                                        new[] { mesh }, new[] { CharCentroid }, direction, tolerance, out int[] meshIndices);
                                    logMessages.Add($"Region {RegionCode}: Mesh projection for CrvCode {crvCode}, Hits: {(projectedPoints?.Length ?? 0)}");
                                }
                                else
                                {
                                    logMessages.Add($"Region {RegionCode}: Invalid Mesh for CrvCode {crvCode}.");
                                }
                                break;

                            case ObjectType.Surface:
                                Surface surface = geo as Surface;
                                if (surface != null && surface.IsValid)
                                {
                                    Brep convertedBrep = Brep.CreateFromSurface(surface);
                                    if (convertedBrep != null && convertedBrep.IsValid)
                                    {
                                        projectedPoints = Rhino.Geometry.Intersect.Intersection.ProjectPointsToBrepsEx(
                                            new[] { convertedBrep }, new[] { CharCentroid }, direction, tolerance, out int[] brepIndices);
                                        logMessages.Add($"Region {RegionCode}: Surface converted to Brep for CrvCode {crvCode}, Hits: {(projectedPoints?.Length ?? 0)}");
                                    }
                                    else
                                    {
                                        logMessages.Add($"Region {RegionCode}: Failed to convert Surface to Brep for CrvCode {crvCode}.");
                                    }
                                }
                                else
                                {
                                    logMessages.Add($"Region {RegionCode}: Invalid Surface for CrvCode {crvCode}.");
                                }
                                break;

                            case ObjectType.None:
                                logMessages.Add($"Region {RegionCode}: Geometry type None for CrvCode {crvCode}, skipping.");
                                break;

                            default:
                                logMessages.Add($"Region {RegionCode}: Unsupported geometry type {type} for CrvCode {crvCode}.");
                                break;
                        }

                        if (projectedPoints != null && projectedPoints.Length > 0)
                        {
                            double minDistance = double.MaxValue;
                            foreach (var point in projectedPoints)
                            {
                                double distance = CharCentroid.DistanceTo(point);
                                if (distance < minDistance)
                                {
                                    minDistance = distance;
                                }
                            }
                            hitDistances.Add((minDistance, crvCode, silhouette.ParentID));
                            logMessages.Add($"Region {RegionCode}: Projected CrvCode {crvCode}, MinDistance: {minDistance}");
                        }
                        else
                        {
                            logMessages.Add($"Region {RegionCode}: No projection points for CrvCode {crvCode}, Type: {type}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Region {RegionCode}: Projection failed for CrvCode {crvCode}: {ex.Message}");
                }
            }

            // Sort by distance and skip the shortest
            if (hitDistances.Count > 1)
            {
                hitDistances.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                for (int i = 1; i < hitDistances.Count; i++) // Skip shortest
                {
                    HittedGeobyCode.Add(hitDistances[i].CrvCode);
                    HittedGeobyGuid.Add(hitDistances[i].ParentID);
                }
                logMessages.Add($"Region {RegionCode}: Added {HittedGeobyCode.Count} hit geometries");
            }
            else
            {
                logMessages.Add($"Region {RegionCode}: Insufficient hits ({hitDistances.Count}) to populate hit lists.");
            }
        }



        // Helper method for region containment test
        private static bool RegionContainmentTest(Curve regionCurve, Curve testCurve, Plane plane, double tolerance)
        {
            // Simplified containment test: Check if testCurve's centroid is inside regionCurve
            var centroid = AreaMassProperties.Compute(testCurve)?.Centroid;
            if (centroid == null || !centroid.HasValue)
                return false;

            var pointContainment = regionCurve.Contains(centroid.Value, plane, tolerance);
            return pointContainment == PointContainment.Inside;
        }

        // Method to calculate characteristic point
        private Point3d CalculateCharacteristicPoint(Curve regionCurve, Plane testPlane, double tolerance)
        {
            if (regionCurve == null || !regionCurve.IsClosed || !regionCurve.IsPlanar())
            {
                return Point3d.Unset;
            }

            AreaMassProperties amp = AreaMassProperties.Compute(regionCurve);
            if (amp == null || !SsUtils.IsPointInsideCurve(regionCurve, amp.Centroid, testPlane, tolerance))
            {
                return Point3d.Unset;
            }

            return amp.Centroid;
        }

        // Method to reset the code number
        public static void ResetNumber()
        {
            _nextNumber = 1;
        } 




    }
}
