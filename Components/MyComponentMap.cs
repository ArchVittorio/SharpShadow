using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace SharpShadow.Components
{
    public class MyComponentProjectRegions : GH_Component
    {
        public MyComponentProjectRegions()
            : base("ProjectRegions", "ProjReg",
                   "Groups SsRegione objects by affected SsSilhouette geometry and projects boolean regions.",
                   "SharpShadow", "Silhouette")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("SsSilhouettes", "SS", "SsSilhouette objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsRegiones", "SR", "SsRegione objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("SsSettings", "Set", "SsSettings object containing tolerance", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("ProjectedCurves", "PC", "Projected curves grouped by affected geometry", GH_ParamAccess.tree);
            pManager.AddTextParameter("LogMessages", "LM", "Log messages", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize inputs
            List<SsSilhouette> silhouettes = new List<SsSilhouette>();
            List<SsRegione> regiones = new List<SsRegione>();
            SsSettings settings = null;
            List<string> logMessages = new List<string> { "Starting ProjectRegions" };

            if (!DA.GetDataList(0, silhouettes))
            {
                logMessages.Add("Error: Failed to get SsSilhouettes.");
                DA.SetDataList(1, logMessages);
                return;
            }

            if (!DA.GetDataList(1, regiones))
            {
                logMessages.Add("Error: Failed to get SsRegiones.");
                DA.SetDataList(1, logMessages);
                return;
            }

            if (!DA.GetData(2, ref settings) || settings == null)
            {
                logMessages.Add("Error: Failed to get SsSettings or settings is null.");
                DA.SetDataList(1, logMessages);
                return;
            }

            logMessages.Add($"Input: {silhouettes.Count} silhouettes, {regiones.Count} regiones, Tolerance: {settings.Tolerance}");

            if (silhouettes == null || silhouettes.Count == 0)
            {
                logMessages.Add("Error: No silhouettes provided.");
                DA.SetDataList(1, logMessages);
                return;
            }

            if (regiones == null || regiones.Count == 0)
            {
                logMessages.Add("Error: No regiones provided.");
                DA.SetDataList(1, logMessages);
                return;
            }

            double tolerance = settings.Tolerance;

            // Create groups by ParentID
            Dictionary<Guid, List<SsRegione>> groupsByParentID = new Dictionary<Guid, List<SsRegione>>();
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null || silhouette.ParentID == Guid.Empty)
                {
                    logMessages.Add("Warning: Null silhouette or empty ParentID, skipping.");
                    continue;
                }
                groupsByParentID[silhouette.ParentID] = new List<SsRegione>();
            }

            // Assign regiones to groups
            foreach (var regione in regiones)
            {
                if (regione == null || regione.HittedGeobyGuid == null || !regione.HittedGeobyGuid.Any())
                {
                    logMessages.Add("Warning: Null regione or empty HittedGeobyGuid, skipping.");
                    continue;
                }

                if (regione.RegionCurve == null || !regione.RegionCurve.IsValid)
                {
                    logMessages.Add("Warning: Invalid or null RegionCurve, skipping.");
                    continue;
                }

                foreach (var guid in regione.HittedGeobyGuid)
                {
                    if (groupsByParentID.ContainsKey(guid))
                    {
                        groupsByParentID[guid].Add(regione);
                    }
                    else
                    {
                        logMessages.Add($"Warning: HittedGeobyGuid {guid} not found in silhouette ParentIDs, skipping.");
                    }
                }
            }

            // Initialize output tree
            GH_Structure<GH_Curve> projectedCurvesTree = new GH_Structure<GH_Curve>();

            // Process each group
            int groupIndex = 0;
            foreach (var silhouette in silhouettes)
            {
                if (silhouette == null || silhouette.ParentID == Guid.Empty)
                {
                    groupIndex++;
                    continue;
                }

                Guid parentID = silhouette.ParentID;
                if (!groupsByParentID.ContainsKey(parentID))
                {
                    logMessages.Add($"Error: No group found for ParentID {parentID}.");
                    groupIndex++;
                    continue;
                }

                var groupRegions = groupsByParentID[parentID];
                logMessages.Add($"Processing group for ParentID {parentID}: {groupRegions.Count} regions");

                // Get affected geometry
                GeometryBase affectedGeo = null;
                if (silhouette.JeongParts != null && silhouette.JeongParts.Any())
                {
                    affectedGeo = silhouette.JeongParts.First(); // Use first Jeong part
                    logMessages.Add($"Using JeongParts for ParentID {parentID}");
                }
                else
                {
                    affectedGeo = silhouette.GeometryReference;
                    logMessages.Add($"Using GeometryReference for ParentID {parentID}");
                }

                if (affectedGeo == null || !affectedGeo.IsValid)
                {
                    logMessages.Add($"Error: Invalid or null affected geometry for ParentID {parentID}");
                    groupIndex++;
                    continue;
                }

                // Get test plane (use silhouette's supportPlane or fallback)
                if (!settings.GetCameraPlane(out Plane testPlane, out string planeLog))
                {
                    logMessages.Add($"Warning: Failed to get camera plane for ParentID {parentID}: {planeLog}");
                    testPlane = Plane.WorldXY;
                }
                if (!testPlane.IsValid)
                {
                    logMessages.Add($"Warning: Invalid test plane for ParentID {parentID}, using WorldXY");
                    testPlane = Plane.WorldXY;
                }

                // Collect region curves
                List<Curve> regionCurves = groupRegions
                    .Where(r => r.RegionCurve != null && r.RegionCurve.IsValid)
                    .Select(r => r.RegionCurve.DuplicateCurve())
                    .ToList();

                List<GH_Curve> projectedCurves = new List<GH_Curve>();

                if (regionCurves.Any())
                {
                    try
                    {
                        // Compute boolean regions
                        var boolRegions = Curve.CreateBooleanRegions(
                            regionCurves,
                            testPlane,
                            combineRegions: true,
                            tolerance
                        );

                        if (boolRegions != null && boolRegions.RegionCount > 0)
                        {
                            logMessages.Add($"Boolean regions computed for ParentID {parentID}: {boolRegions.RegionCount} regions");

                            // Get closed curves
                            List<Curve> groupResult = new List<Curve>();
                            for (int i = 0; i < boolRegions.RegionCount; i++)
                            {
                                var regionCrv = boolRegions.RegionCurves(i);
                                var joinedCurves = Curve.JoinCurves(regionCrv, tolerance);
                                groupResult.AddRange(joinedCurves.Where(c => c.IsClosed));
                            }

                            // Project curves onto affected geometry
                            foreach (var curve in groupResult)
                            {
                                if (curve == null || !curve.IsValid)
                                    continue;

                                // Project along supportPlane normal
                                Curve[] projected = null;
                                if (affectedGeo is Brep brep)
                                {
                                    projected = Curve.ProjectToBrep(curve, brep, testPlane.Normal, tolerance);
                                }
                                else if (affectedGeo is Mesh mesh)
                                {
                                    projected = Curve.ProjectToMesh(curve, mesh, testPlane.Normal, tolerance);
                                }

                                if (projected != null && projected.Any())
                                {
                                    projectedCurves.AddRange(projected.Select(c => new GH_Curve(c)));
                                    logMessages.Add($"Projected {projected.Length} curves onto geometry for ParentID {parentID}");
                                }
                                else
                                {
                                    logMessages.Add($"Warning: No projection result for curve in ParentID {parentID}");
                                }
                            }
                        }
                        else
                        {
                            logMessages.Add($"Warning: No boolean regions computed for ParentID {parentID}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logMessages.Add($"Exception in boolean regions for ParentID {parentID}: {ex.Message}");
                    }
                }
                else
                {
                    logMessages.Add($"No valid region curves for ParentID {parentID}");
                }

                // Add to tree
                GH_Path path = new GH_Path(groupIndex);
                projectedCurvesTree.AppendRange(projectedCurves, path);
                groupIndex++;
            }

            // Set outputs
            try
            {
                DA.SetDataTree(0, projectedCurvesTree);
                DA.SetDataList(1, logMessages);
            }
            catch (Exception ex)
            {
                logMessages.Add($"Error setting outputs: {ex.Message}");
                DA.SetDataList(1, logMessages);
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
        public override Guid ComponentGuid
        {
            get { return new Guid("35641C55-727C-421E-9BD6-027B6AC7985B"); }
        }
    }
}