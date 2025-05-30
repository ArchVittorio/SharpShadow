using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpShadow.Components
{
    public class MapSsRegioneComponent : GH_Component
    {



        public MapSsRegioneComponent()
            : base("MapSsRegione", "MapReg",
                   "Computes HittedGeobyCode, groups regions, computes boolean regions, and projects curves onto geometries.",
                   "SharpShadow", "Region")
        {
        }


        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SsRegiones", "SR", "SsRegione objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects for CrvCode matching", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSettings", "S", "SsSettings object", GH_ParamAccess.item);
            pManager.AddGeometryParameter("TargetGeometries", "TG", "Geometries to project onto (optional, defaults to JeongParts)", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("GroupedCrvs", "GC", "Boolean region curves after grouping", GH_ParamAccess.tree);
            pManager.AddCurveParameter("ProjectedCurves", "PC", "Projected curves onto geometries", GH_ParamAccess.tree);
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<SsRegione> regions = new List<SsRegione>();
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            SsSettings settings = null;
            List<GeometryBase> targetGeometries = new List<GeometryBase>();
            List<string> logMessages = new List<string>();
            logMessages.Add("Starting MapSsRegione");

            if (!DA.GetDataList(0, regions))
            {
                logMessages.Add("Error: Failed to get SsRegiones.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (!DA.GetDataList(1, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (!DA.GetData(2, ref settings))
            {
                logMessages.Add("Error: Failed to get SsSettings.");
                DA.SetDataList(2, logMessages);
                return;
            }
            DA.GetDataList(3, targetGeometries); // Optional

            logMessages.Add($"Inputs: {regions.Count} regions, {silhouettes.Count} silhouettes, TargetGeometries: {targetGeometries.Count}");

            if (regions == null || regions.Count == 0)
            {
                logMessages.Add("Error: No regions provided.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (settings == null)
            {
                logMessages.Add("Error: SsSettings is null.");
                DA.SetDataList(2, logMessages);
                return;
            }
            if (settings.Tolerance <= 0)
            {
                logMessages.Add("Error: Invalid tolerance in SsSettings.");
                DA.SetDataList(2, logMessages);
                return;
            }

            // Get camera direction
            if (!settings.GetCameraDirection(out Vector3d direction, out string directionLog))
            {
                logMessages.Add(directionLog);
                DA.SetDataList(2, logMessages);
                return;
            }
            logMessages.Add(directionLog);

            // Get camera plane
            if (!settings.GetCameraPlane(out Plane testPlane, out string planeLog))
            {
                logMessages.Add(planeLog);
                DA.SetDataList(2, logMessages);
                return;
            }
            logMessages.Add(planeLog);

            // Compute HittedGeobyCode
            var hittedGeobyCodeLists = new List<List<int>>();
            logMessages.Add("Computing HittedGeobyCode");
            foreach (var region in regions)
            {
                if (region == null)
                {
                    logMessages.Add("Warning: Null region encountered.");
                    hittedGeobyCodeLists.Add(new List<int>());
                    continue;
                }
                try
                {
                    region.ComputeHitGeometries(silhouettes, settings, direction, settings.Tolerance, out var hitLogs);
                    logMessages.AddRange(hitLogs);
                    hittedGeobyCodeLists.Add(region.HittedGeobyCode ?? new List<int>());
                    logMessages.Add($"Region {region.RegionCode}: HittedGeobyCode count: {region.HittedGeobyCode?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Region {region.RegionCode}: Failed to compute HittedGeobyCode: {ex.Message}");
                    hittedGeobyCodeLists.Add(new List<int>());
                }
            }

            // Group regions by hitted silhouette
            var groupedRegions = new List<List<SsRegione>>();
            var groupCrvCodes = new List<int>();
            logMessages.Add("Grouping regions by hitted silhouette");
            for (int i = 0; i < silhouettes.Count; i++)
            {
                groupedRegions.Add(new List<SsRegione>());
                groupCrvCodes.Add(-1);
            }

            for (int i = 0; i < silhouettes.Count; i++)
            {
                var silhouette = silhouettes[i];
                if (silhouette == null)
                {
                    logMessages.Add($"Silhouette {i}: Null silhouette, skipping.");
                    continue;
                }

                int crvCode = silhouette.CrvCode;
                groupCrvCodes[i] = crvCode;
                logMessages.Add($"Processing Silhouette {i}, CrvCode: {crvCode}");

                foreach (var region in regions)
                {
                    if (region == null)
                        continue;
                    if (region.HittedGeobyCode != null && region.HittedGeobyCode.Contains(crvCode))
                    {
                        groupedRegions[i].Add(region);
                        logMessages.Add($"Region {region.RegionCode} added to group for CrvCode {crvCode}");
                    }
                }
                logMessages.Add($"Silhouette {i}, CrvCode {crvCode}: Grouped {groupedRegions[i].Count} regions");
            }

            // Compute BooleanRegions
            var booleanRegionCurves = new List<List<Curve>>();
            logMessages.Add("Computing BooleanRegions");
            for (int i = 0; i < groupedRegions.Count; i++)
            {
                var group = groupedRegions[i];
                var curves = new List<Curve>();
                booleanRegionCurves.Add(new List<Curve>());

                foreach (var region in group)
                {
                    if (region.RegionCurve != null && region.RegionCurve.IsValid)
                        curves.Add(region.RegionCurve);
                }

                if (curves.Count == 0)
                {
                    logMessages.Add($"Silhouette {i}: No valid curves for BooleanRegions.");
                    continue;
                }

                try
                {
                    CurveBooleanRegions boolRegions = Curve.CreateBooleanRegions(
                        curves,
                        testPlane,
                        combineRegions: true,
                        settings.Tolerance
                    );

                    if (boolRegions != null && boolRegions.RegionCount > 0)
                    {
                        for (int j = 0; j < boolRegions.RegionCount; j++)
                        {
                            Curve[] regionCurves = boolRegions.RegionCurves(j);
                            if (regionCurves != null && regionCurves.Length > 0)
                            {
                                Curve[] joinedCurves = Curve.JoinCurves(regionCurves);
                                if (joinedCurves != null && joinedCurves.Length > 0 && joinedCurves[0] != null)
                                {
                                    booleanRegionCurves[i].Add(joinedCurves[0]);
                                    logMessages.Add($"Silhouette {i}, BooleanRegion {j}: Curve added.");
                                }
                            }
                        }
                        logMessages.Add($"Silhouette {i}: Computed {boolRegions.RegionCount} BooleanRegions");
                    }
                }
                catch (Exception ex)
                {
                    logMessages.Add($"Silhouette {i}: Failed to compute BooleanRegions: {ex.Message}");
                }
            }

            // Project BooleanRegionCurves
            var projectedCurves = new List<List<Curve>>();
            logMessages.Add("Projecting BooleanRegionCurves");
            for (int i = 0; i < booleanRegionCurves.Count; i++)
            {
                var boolCurves = booleanRegionCurves[i];
                projectedCurves.Add(new List<Curve>());

                if (boolCurves.Count == 0)
                {
                    logMessages.Add($"Silhouette {i}: No BooleanRegionCurves to project.");
                    continue;
                }

                int crvCode = groupCrvCodes[i];
                if (crvCode == -1)
                {
                    logMessages.Add($"Silhouette {i}: No valid CrvCode for projection.");
                    continue;
                }

                SsSilhouette silhouette = silhouettes.Find(s => s?.CrvCode == crvCode);
                if (silhouette == null)
                {
                    logMessages.Add($"Silhouette {i}, CrvCode {crvCode}: Corresponding SsSilhouette not found.");
                    continue;
                }

                // Get target geometries
                List<GeometryBase> geometriesToProject = targetGeometries.Any() ? targetGeometries : silhouette.JeongParts ?? new List<GeometryBase>();
                if (!geometriesToProject.Any())
                {
                    logMessages.Add($"Silhouette {i}, CrvCode {crvCode}: No target geometries for projection.");
                    continue;
                }

                foreach (var curve in boolCurves)
                {
                    if (curve == null || !curve.IsValid)
                    {
                        logMessages.Add($"Silhouette {i}: Invalid BooleanRegionCurve, skipping projection.");
                        continue;
                    }

                    try
                    {
                        List<Curve> projCurves = new List<Curve>();
                        switch (silhouette.ParentType)
                        {
                            case ObjectType.Brep:
                                var breps = geometriesToProject.Where(g => g is Brep && ((Brep)g).IsValid).Cast<Brep>().ToList();
                                if (breps.Count > 0)
                                {
                                    Curve[] brepProjCurves = Curve.ProjectToBrep(curve, breps, direction, settings.Tolerance);
                                    if (brepProjCurves != null && brepProjCurves.Length > 0)
                                    {
                                        projCurves.AddRange(brepProjCurves.Where(c => c != null && c.IsValid));
                                        logMessages.Add($"Silhouette {i}: Projected curve onto {brepProjCurves.Length} Brep curves.");
                                    }
                                }
                                break;

                            case ObjectType.Mesh:
                                var meshes = geometriesToProject.Where(g => g is Mesh && ((Mesh)g).IsValid).Cast<Mesh>().ToList();
                                if (meshes.Count > 0)
                                {
                                    double[] tParams = curve.DivideByCount(100, true);
                                    if (tParams != null && tParams.Length > 0)
                                    {
                                        Point3d[] samplePoints = tParams.Select(t => curve.PointAt(t)).ToArray();
                                        List<Point3d> meshProjPoints = new List<Point3d>();
                                        foreach (var mesh in meshes)
                                        {
                                            Point3d[] meshPoints = Intersection.ProjectPointsToMeshesEx(
                                                new[] { mesh }, samplePoints, direction, settings.Tolerance, out int[] meshIndices);
                                            if (meshPoints != null && meshPoints.Length > 0)
                                                meshProjPoints.AddRange(meshPoints);
                                        }
                                        if (meshProjPoints.Count >= 2)
                                        {
                                            Curve projCurve = Curve.CreateInterpolatedCurve(meshProjPoints, 3);
                                            if (projCurve != null && projCurve.IsValid)
                                                projCurves.Add(projCurve);
                                        }
                                    }
                                    if (projCurves.Any())
                                        logMessages.Add($"Silhouette {i}: Projected curve onto Mesh.");
                                }
                                break;

                            case ObjectType.Surface:
                                var surfaceBreps = new List<Brep>();
                                foreach (var geo in geometriesToProject)
                                {
                                    if (geo is Surface surface && surface.IsValid)
                                    {
                                        Brep convertedBrep = Brep.CreateFromSurface(surface);
                                        if (convertedBrep != null && convertedBrep.IsValid)
                                            surfaceBreps.Add(convertedBrep);
                                    }
                                }
                                if (surfaceBreps.Count > 0)
                                {
                                    Curve[] surfaceProjCurves = Curve.ProjectToBrep(curve, surfaceBreps, direction, settings.Tolerance);
                                    if (surfaceProjCurves != null && surfaceProjCurves.Length > 0)
                                    {
                                        projCurves.AddRange(surfaceProjCurves.Where(c => c != null && c.IsValid));
                                        logMessages.Add($"Silhouette {i}: Projected curve onto {surfaceProjCurves.Length} Surface curves.");
                                    }
                                }
                                break;

                            default:
                                logMessages.Add($"Silhouette {i}, CrvCode {crvCode}: Unsupported ParentType {silhouette.ParentType}.");
                                break;
                        }
                        projectedCurves[i].AddRange(projCurves);
                        if (projCurves.Any())
                            logMessages.Add($"Silhouette {i}: Added {projCurves.Count} projected curves.");
                    }
                    catch (Exception ex)
                    {
                        logMessages.Add($"Silhouette {i}: Failed to project curve: {ex.Message}");
                    }
                }
            }

            // Set outputs
            try
            {
                var groupedTree = new Grasshopper.DataTree<Curve>();
                for (int i = 0; i < booleanRegionCurves.Count; i++)
                    groupedTree.AddRange(booleanRegionCurves[i], new Grasshopper.Kernel.Data.GH_Path(i));
                DA.SetDataTree(0, groupedTree);

                var projectedTree = new Grasshopper.DataTree<Curve>();
                for (int i = 0; i < projectedCurves.Count; i++)
                    projectedTree.AddRange(projectedCurves[i], new Grasshopper.Kernel.Data.GH_Path(i));
                DA.SetDataTree(1, projectedTree);

                DA.SetDataList(2, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(2, logMessages);
            }
        }


        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("e5f6g7h8-i9j0-1234-abcd-8678901234ab");
    }
}